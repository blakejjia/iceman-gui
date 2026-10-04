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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachConsole(ATTACH_PARENT_PROCESS);
            bool success = await HardwareVerificationRunner.RunAllAsync();
            Shutdown(success ? 0 : 1);
            return;
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
