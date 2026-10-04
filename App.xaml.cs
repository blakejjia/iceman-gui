using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using iceman_gui.Core;

namespace iceman_gui;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isHandlingException = false;
        DispatcherUnhandledException += (s, args) =>
        {
            if (isHandlingException) return;
            isHandlingException = true;
            try
            {
                string crashInfo = $"[CRASH {DateTime.Now}] {args.Exception.Message}\n\n{args.Exception}";
                System.IO.File.WriteAllText("crash.log", crashInfo);
                Console.WriteLine(crashInfo);
                MessageBox.Show(crashInfo, "Proxmark3 Manager Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
            isHandlingException = false;
        };

        if (e.Args.Contains("--test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachConsole(ATTACH_PARENT_PROCESS);
            bool success = HardwareVerificationRunner.RunAllAsync().GetAwaiter().GetResult();
            Shutdown(success ? 0 : 1);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();
    }
}
