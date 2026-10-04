using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace iceman_gui.Core;

public class Pm3OutputEventArgs : EventArgs
{
    public string Line { get; }
    public bool IsError { get; }

    public Pm3OutputEventArgs(string line, bool isError = false)
    {
        Line = line;
        IsError = isError;
    }
}

public class Pm3ProcessService
{
    private static Pm3ProcessService? _instance;
    public static Pm3ProcessService Instance => _instance ??= new Pm3ProcessService();

    private Process? _interactiveProcess;
    private StreamWriter? _interactiveStdin;
    private readonly SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1);
    private Process? _currentActiveProcess;

    // Interactive command routing state
    private TaskCompletionSource<string>? _currentCommandTcs;
    private string? _currentCommandMarker;
    private StringBuilder? _currentCommandOutput;

    public event EventHandler<Pm3OutputEventArgs>? OutputReceived;
    public event EventHandler<bool>? ConnectionStateChanged;

    public string CurrentPort { get; private set; } = string.Empty;
    public bool IsConnected { get; private set; }
    public bool IsBusy { get; private set; }

    public void SetPort(string port)
    {
        CurrentPort = port;
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

        // Inject runtime environment with proper trailing slashes for Qt
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.EnvironmentVariables["PATH"] = $"{env.LibsDirectory};{env.ShellDirectory};{currentPath}";
        
        string libsSlash = env.LibsDirectory.TrimEnd('\\') + "\\";
        psi.EnvironmentVariables["QT_PLUGIN_PATH"] = libsSlash;
        psi.EnvironmentVariables["QT_QPA_PLATFORM_PLUGIN_PATH"] = libsSlash;
        psi.EnvironmentVariables["HOME"] = env.ClientDirectory;
        psi.EnvironmentVariables["MSYSTEM"] = "MINGW64";

        return psi;
    }

    /// <summary>
    /// Executes a PM3 command. Uses the persistent interactive session if active,
    /// or starts one if port is set, with fallback to single standalone execution.
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default, int timeoutMs = 60000)
    {
        await _commandLock.WaitAsync(ct);
        IsBusy = true;

        try
        {
            // If session is not running but we have a port, try to start the persistent session
            if ((_interactiveProcess == null || _interactiveProcess.HasExited) && !string.IsNullOrWhiteSpace(CurrentPort))
            {
                await StartInteractiveSessionInternalAsync(CurrentPort, 5000);
            }

            if (_interactiveProcess != null && !_interactiveProcess.HasExited && _interactiveStdin != null)
            {
                return await ExecuteInteractiveCommandInternalAsync(command, ct, timeoutMs);
            }

            // Fallback: standalone one-shot process execution
            return await ExecuteStandaloneCommandInternalAsync(command, ct, timeoutMs);
        }
        finally
        {
            IsBusy = false;
            _commandLock.Release();
        }
    }

    private async Task<string> ExecuteInteractiveCommandInternalAsync(string command, CancellationToken ct, int timeoutMs)
    {
        if (_interactiveStdin == null || _interactiveProcess == null || _interactiveProcess.HasExited)
        {
            throw new InvalidOperationException("Interactive session is not available.");
        }

        string marker = $"END_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sb = new StringBuilder();

        _currentCommandMarker = marker;
        _currentCommandOutput = sb;
        _currentCommandTcs = tcs;

        try
        {
            _interactiveStdin.WriteLine(command);
            _interactiveStdin.WriteLine($"rem {marker}");
            await _interactiveStdin.FlushAsync();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);

            using (linkedCts.Token.Register(() =>
            {
                // PM3 cancels current reader/search upon receiving Enter (newline)
                try
                {
                    _interactiveStdin?.WriteLine();
                    _interactiveStdin?.Flush();
                }
                catch { }

                tcs.TrySetCanceled(linkedCts.Token);
            }))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _currentCommandTcs = null;
            _currentCommandMarker = null;
            _currentCommandOutput = null;
        }
    }

    private async Task<string> ExecuteStandaloneCommandInternalAsync(string command, CancellationToken ct, int timeoutMs)
    {
        var outputBuilder = new StringBuilder();
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string portArg = string.IsNullOrWhiteSpace(CurrentPort) ? "" : $"{CurrentPort} ";
        string args = $"{portArg}-f -w -c \"{command}\"";

        var psi = CreateBaseStartInfo(args);
        using var proc = new Process { StartInfo = psi };
        _currentActiveProcess = proc;

        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                OutputReceived?.Invoke(this, new Pm3OutputEventArgs(e.Data));
            }
        };

        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                OutputReceived?.Invoke(this, new Pm3OutputEventArgs(e.Data, true));
            }
        };

        proc.Start();
        ChildProcessTracker.AddProcess(proc);

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeoutMs);

        try
        {
            await proc.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(true); } catch { }
            }
            throw;
        }
        finally
        {
            _currentActiveProcess = null;
        }

        return outputBuilder.ToString();
    }

    /// <summary>
    /// Starts persistent interactive terminal session asynchronously and waits for ready prompt.
    /// </summary>
    public async Task<bool> StartInteractiveSessionAsync(string port, int timeoutMs = 8000)
    {
        await _commandLock.WaitAsync();
        try
        {
            return await StartInteractiveSessionInternalAsync(port, timeoutMs);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<bool> StartInteractiveSessionInternalAsync(string port, int timeoutMs = 8000)
    {
        if (_interactiveProcess != null && !_interactiveProcess.HasExited && CurrentPort == port && IsConnected)
        {
            return true;
        }

        StopInteractiveSession();

        CurrentPort = port;
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string args = string.IsNullOrWhiteSpace(port) ? "-w -f" : $"{port} -w -f";
        var psi = CreateBaseStartInfo(args);

        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _interactiveProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _interactiveProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;

                OutputReceived?.Invoke(this, new Pm3OutputEventArgs(e.Data));

                string clean = AnsiColorParser.StripAnsi(e.Data);

                // Check for session ready prompt
                if (clean.Contains("pm3 -->") || clean.Contains("Communicating with Proxmark3 over"))
                {
                    readyTcs.TrySetResult(true);
                }

                // Check active interactive command sentinel
                if (_currentCommandMarker != null && _currentCommandTcs != null)
                {
                    if (clean.Contains("remark:") && clean.Contains(_currentCommandMarker))
                    {
                        var tcs = _currentCommandTcs;
                        var result = _currentCommandOutput?.ToString() ?? string.Empty;
                        _currentCommandTcs = null;
                        _currentCommandMarker = null;
                        _currentCommandOutput = null;
                        tcs.TrySetResult(result);
                    }
                    else if (!clean.Contains($"rem {_currentCommandMarker}"))
                    {
                        // Append standard command output line (excluding the rem marker echo)
                        _currentCommandOutput?.AppendLine(e.Data);
                    }
                }
            };

            _interactiveProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                OutputReceived?.Invoke(this, new Pm3OutputEventArgs(e.Data, true));
            };

            _interactiveProcess.Exited += (s, e) =>
            {
                readyTcs.TrySetResult(false);
                if (_currentCommandTcs != null)
                {
                    _currentCommandTcs.TrySetException(new IOException("Proxmark3 session exited unexpectedly."));
                    _currentCommandTcs = null;
                }
                IsConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
            };

            _interactiveProcess.Start();
            ChildProcessTracker.AddProcess(_interactiveProcess);

            _interactiveStdin = _interactiveProcess.StandardInput;
            _interactiveProcess.BeginOutputReadLine();
            _interactiveProcess.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(timeoutMs);
            using (cts.Token.Register(() => readyTcs.TrySetResult(false)))
            {
                bool ready = await readyTcs.Task;
                if (ready)
                {
                    IsConnected = true;
                    ConnectionStateChanged?.Invoke(this, true);
                    return true;
                }
                else
                {
                    StopInteractiveSession();
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, new Pm3OutputEventArgs($"[!] Failed to start session: {ex.Message}", true));
            StopInteractiveSession();
            return false;
        }
    }

    public bool StartInteractiveSession(string port)
    {
        return StartInteractiveSessionAsync(port).GetAwaiter().GetResult();
    }

    public void SendInteractiveInput(string input)
    {
        if (_interactiveStdin != null && _interactiveProcess != null && !_interactiveProcess.HasExited)
        {
            _interactiveStdin.WriteLine(input);
        }
    }

    public void StopInteractiveSession()
    {
        if (_interactiveProcess != null)
        {
            try
            {
                if (!_interactiveProcess.HasExited)
                {
                    _interactiveStdin?.WriteLine("quit");
                    if (!_interactiveProcess.WaitForExit(1000))
                    {
                        _interactiveProcess.Kill(true);
                    }
                }
            }
            catch { }
            finally
            {
                _interactiveProcess.Dispose();
                _interactiveProcess = null;
                _interactiveStdin = null;
                IsConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
            }
        }
    }

    public void CancelCurrent()
    {
        try
        {
            if (_interactiveStdin != null && _interactiveProcess != null && !_interactiveProcess.HasExited)
            {
                _interactiveStdin.WriteLine();
                _interactiveStdin.Flush();
            }
        }
        catch { }

        if (_currentActiveProcess != null && !_currentActiveProcess.HasExited)
        {
            try
            {
                _currentActiveProcess.Kill(true);
            }
            catch { }
        }
    }
}
