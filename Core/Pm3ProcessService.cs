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
    /// Executes a single PM3 command with flush flag (-f) and wait flag (-w).
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default, int timeoutMs = 60000)
    {
        await _commandLock.WaitAsync(ct);
        IsBusy = true;
        var outputBuilder = new StringBuilder();

        try
        {
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

            return outputBuilder.ToString();
        }
        finally
        {
            _currentActiveProcess = null;
            IsBusy = false;
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Starts persistent interactive terminal session.
    /// </summary>
    public bool StartInteractiveSession(string port)
    {
        StopInteractiveSession();

        CurrentPort = port;
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string args = string.IsNullOrWhiteSpace(port) ? "-w -f" : $"{port} -w -f";
        var psi = CreateBaseStartInfo(args);

        try
        {
            _interactiveProcess = new Process { StartInfo = psi };
            _interactiveProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(this, new Pm3OutputEventArgs(e.Data));
                }
            };
            _interactiveProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(this, new Pm3OutputEventArgs(e.Data, true));
                }
            };

            _interactiveProcess.Start();
            ChildProcessTracker.AddProcess(_interactiveProcess);

            _interactiveStdin = _interactiveProcess.StandardInput;
            _interactiveProcess.BeginOutputReadLine();
            _interactiveProcess.BeginErrorReadLine();

            IsConnected = true;
            ConnectionStateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, new Pm3OutputEventArgs($"[!] Failed to start session: {ex.Message}", true));
            IsConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
            return false;
        }
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
