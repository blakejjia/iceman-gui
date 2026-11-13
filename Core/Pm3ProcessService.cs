using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace iceman_gui.Core;

public enum Pm3Failure
{
    None,
    TimedOut,
    SessionLost,
    NotConnected,
    StartFailed,
    Unsupported
}

public sealed class Pm3Result
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public Pm3Failure Failure { get; init; }
    public string? Error { get; init; }

    public static Pm3Result Ok(string output) => new() { Success = true, Output = output };

    public static Pm3Result Fail(Pm3Failure failure, string error) =>
        new() { Failure = failure, Error = error };
}

public sealed class Pm3CommandException : Exception
{
    public Pm3Result Result { get; }

    public Pm3CommandException(string command, Pm3Result result)
        : base($"{command} failed ({result.Failure}): {result.Error}")
    {
        Result = result;
    }
}

/// <summary>
/// Owns the single Proxmark3 client process and exposes it as a command API.
/// The client is a line-oriented REPL with no end-of-command signal, so each
/// command is followed by a unique "rem" marker whose "remark:" echo closes the
/// response. Commands cannot be interrupted once started (stdin is not read
/// until they return), so a timeout kills the session instead.
/// </summary>
public class Pm3ProcessService
{
    private static readonly Pm3ProcessService _instance = new();
    public static Pm3ProcessService Instance => _instance;

    private static readonly Regex PromptEchoRegex = new(
        @"^\[[^\]]+\]\s+(?:pm3|pm5)\s+-->", RegexOptions.Compiled);

    private static readonly Regex ProgressRegex = new(
        @"^\[[\\|/\-]\]\s*\d+$", RegexOptions.Compiled);

    private enum CommandOutcome
    {
        Completed,
        TimedOut,
        SessionLost
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private TaskCompletionSource<(CommandOutcome Outcome, string Output)>? _pending;
    private StringBuilder? _buffer;
    private string? _marker;

    public string CurrentPort { get; private set; } = string.Empty;

    public bool IsConnected
    {
        get
        {
            Process? process = _process;
            return process != null && !process.HasExited && _stdin != null;
        }
    }

    public ProcessStartInfo CreateBaseStartInfo(string arguments)
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved)
        {
            env.Resolve();
        }

        if (string.IsNullOrEmpty(env.ExecutablePath) || !File.Exists(env.ExecutablePath))
        {
            throw new FileNotFoundException($"Bundled Proxmark3 client was not found. Expected 'client\\proxmark3.exe' alongside 'iceman-gui.exe' in '{AppDomain.CurrentDomain.BaseDirectory}'. Please ensure the application package was extracted completely.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = env.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = env.ClientDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.EnvironmentVariables["PATH"] = $"{env.LibsDirectory};{env.ShellDirectory};{currentPath}";

        string libsSlash = env.LibsDirectory.TrimEnd('\\') + "\\";
        psi.EnvironmentVariables["QT_PLUGIN_PATH"] = libsSlash;
        psi.EnvironmentVariables["QT_QPA_PLATFORM_PLUGIN_PATH"] = libsSlash;
        psi.EnvironmentVariables["HOME"] = AppPaths.ClientHome;
        psi.EnvironmentVariables["MSYSTEM"] = "MINGW64";

        return psi;
    }

    public async Task<bool> ConnectAsync(string port, int timeoutMs = 15000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            return false;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (IsConnected && string.Equals(CurrentPort, port, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await StopSessionAsync(graceful: false);
            CurrentPort = port;

            if (await StartSessionWithRetryAsync(timeoutMs, ct))
            {
                return true;
            }

            CurrentPort = string.Empty;
            await StopSessionAsync(graceful: false);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StopSessionAsync(graceful: true);
            CurrentPort = string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Executes a command and returns its console output. If no session is
    /// running, one is started on the current port; after an explicit
    /// DisconnectAsync the current port is cleared and commands fail instead.
    /// </summary>
    public async Task<Pm3Result> ExecuteAsync(string command, int timeoutMs = 60000, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!IsConnected)
            {
                if (string.IsNullOrWhiteSpace(CurrentPort))
                {
                    return Pm3Result.Fail(Pm3Failure.NotConnected, "No Proxmark3 port is selected.");
                }

                if (!await StartSessionWithRetryAsync(15000, ct))
                {
                    await StopSessionAsync(graceful: false);
                    return Pm3Result.Fail(Pm3Failure.StartFailed,
                        $"Could not start the Proxmark3 session on {CurrentPort}. If the device was just interrupted, unplug and replug it, then reconnect.");
                }
            }

            string marker = "END_" + Guid.NewGuid().ToString("N");
            var (outcome, output) = await SendInternalAsync(command, marker, timeoutMs, ct);

            switch (outcome)
            {
                case CommandOutcome.Completed:
                    return Pm3Result.Ok(output);

                case CommandOutcome.SessionLost:
                    await StopSessionAsync(graceful: false);
                    return Pm3Result.Fail(Pm3Failure.SessionLost, "The Proxmark3 session ended unexpectedly.");

                default:
                    // A running command cannot be interrupted through stdin, so
                    // the only safe recovery is to drop the session; the next
                    // command reconnects on the same port.
                    await StopSessionAsync(graceful: false);
                    return Pm3Result.Fail(Pm3Failure.TimedOut, $"'{command}' did not finish within {timeoutMs / 1000.0:0.#}s.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops the interactive session and holds the device while <paramref name="work"/>
    /// runs, so operations such as flashing cannot race a queued command.
    /// </summary>
    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StopSessionAsync(graceful: false);
            return await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> StartSessionWithRetryAsync(int timeoutMs, CancellationToken ct)
    {
        if (await StartSessionAsync(timeoutMs, ct))
        {
            return true;
        }

        // A killed client can make the USB CDC port disappear for a moment.
        // Give the device a chance to re-enumerate, then try once more with
        // whatever Proxmark port is currently present.
        await StopSessionAsync(graceful: false);
        await Task.Delay(800, ct);

        var device = SerialPortDetector.GetAvailablePorts().FirstOrDefault(p => p.IsProxmark);
        if (device != null)
        {
            CurrentPort = device.PortName;
        }

        return await StartSessionAsync(timeoutMs, ct);
    }

    private async Task<bool> StartSessionAsync(int timeoutMs, CancellationToken ct)
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved && !env.Resolve())
        {
            return false;
        }

        if (string.IsNullOrEmpty(env.ExecutablePath) || !File.Exists(env.ExecutablePath))
        {
            return false;
        }

        ClientHome.EnsureSeeded(Path.Combine(env.ClientDirectory, ".proxmark3", "preferences.json"));

        ProcessStartInfo psi;
        try
        {
            psi = CreateBaseStartInfo($"{CurrentPort} -w -f");
        }
        catch
        {
            return false;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);
        process.Exited += (_, _) => HandleExit(process);

        try
        {
            process.Start();
            ChildProcessTracker.AddProcess(process);

            _process = process;
            _stdin = process.StandardInput;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            process.Dispose();
            _process = null;
            _stdin = null;
            return false;
        }

        string marker = "READY_" + Guid.NewGuid().ToString("N");
        var (outcome, _) = await SendInternalAsync($"rem {marker}", marker, timeoutMs, ct);
        return outcome == CommandOutcome.Completed;
    }

    private async Task<(CommandOutcome Outcome, string Output)> SendInternalAsync(
        string command, string marker, int timeoutMs, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(CommandOutcome Outcome, string Output)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_sync)
        {
            _pending = tcs;
            _marker = marker;
            _buffer = new StringBuilder();
        }

        try
        {
            StreamWriter? stdin = _stdin;
            if (stdin == null)
            {
                ClearPending();
                return (CommandOutcome.SessionLost, string.Empty);
            }

            // The client reads stdin only between commands, so the marker is
            // queued right behind the command and runs when it returns.
            stdin.WriteLine(command);
            stdin.WriteLine($"rem {marker}");
            await stdin.FlushAsync();
        }
        catch
        {
            ClearPending();
            return (CommandOutcome.SessionLost, string.Empty);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = Task.Delay(timeoutMs, cts.Token);
        var finished = await Task.WhenAny(tcs.Task, timeout);

        if (finished == tcs.Task)
        {
            cts.Cancel();
            return await tcs.Task;
        }

        ClearPending();
        return (CommandOutcome.TimedOut, string.Empty);
    }

    private void HandleLine(string? rawLine)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return;
        }

        foreach (string segment in rawLine.Split('\r'))
        {
            string clean = AnsiColorParser.StripAnsi(segment).TrimEnd();
            if (clean.Length == 0 || ProgressRegex.IsMatch(clean))
            {
                continue;
            }

            lock (_sync)
            {
                // "remark:" lines are our own sentinels (command markers and
                // the connect handshake); they never belong to command output,
                // even when a late one arrives after the next command started.
                if (clean.Contains("remark:", StringComparison.OrdinalIgnoreCase))
                {
                    var pendingMarker = _pending;
                    if (pendingMarker != null &&
                        _marker != null &&
                        clean.Contains(_marker, StringComparison.Ordinal))
                    {
                        string output = _buffer?.ToString() ?? string.Empty;
                        ClearPendingLocked();
                        pendingMarker.TrySetResult((CommandOutcome.Completed, output));
                    }

                    continue;
                }

                if (PromptEchoRegex.IsMatch(clean))
                {
                    continue;
                }

                var pending = _pending;
                if (pending == null)
                {
                    // Startup banner or a late line between commands.
                    continue;
                }

                _buffer?.AppendLine(clean);
            }
        }
    }

    private void HandleExit(Process process)
    {
        lock (_sync)
        {
            _pending?.TrySetResult((CommandOutcome.SessionLost, string.Empty));
            ClearPendingLocked();
        }

        if (ReferenceEquals(_process, process))
        {
            _process = null;
            _stdin = null;
        }
        // Ownership of the Process object stays with StopSessionAsync, which
        // kills and waits for it; disposing it here races with that path.
    }

    private async Task StopSessionAsync(bool graceful)
    {
        Process? process = _process;
        StreamWriter? stdin = _stdin;
        _process = null;
        _stdin = null;

        lock (_sync)
        {
            _pending?.TrySetResult((CommandOutcome.SessionLost, string.Empty));
            ClearPendingLocked();
        }

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                if (graceful && stdin != null)
                {
                    try
                    {
                        stdin.WriteLine("quit");
                        stdin.Flush();
                    }
                    catch
                    {
                    }

                    try
                    {
                        using var quitWait = new CancellationTokenSource(800);
                        await process.WaitForExitAsync(quitWait.Token);
                    }
                    catch
                    {
                    }
                }

                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }
                }

                // Wait for the OS to reap the process before the next connect
                // attempt tries to open the COM port.
                try
                {
                    using var reapWait = new CancellationTokenSource(5000);
                    await process.WaitForExitAsync(reapWait.Token);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                stdin?.Dispose();
            }
            catch
            {
            }

            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }
    }

    private void ClearPending()
    {
        lock (_sync)
        {
            ClearPendingLocked();
        }
    }

    private void ClearPendingLocked()
    {
        _pending = null;
        _marker = null;
        _buffer = null;
    }
}
