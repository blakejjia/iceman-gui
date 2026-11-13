using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;

namespace iceman_gui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private DashboardViewModel _dashboard = new();

    [ObservableProperty]
    private TagScannerViewModel _tagScanner = new();

    [ObservableProperty]
    private LibraryViewModel _library = new();

    [ObservableProperty]
    private WriteViewModel _write = new();

    [ObservableProperty]
    private EditUidViewModel _editUid = new();

    public FlasherViewModel Flasher => Dashboard.Flasher;

    [ObservableProperty]
    private string _activePort = "Disconnected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _selectedPageTag = "dashboard";

    public bool IsDashboardActive => SelectedPageTag == "dashboard";
    public bool IsScannerActive => SelectedPageTag == "scanner";
    public bool IsLibraryActive => SelectedPageTag == "library";
    public bool IsWriteActive => SelectedPageTag == "write";
    public bool IsEditUidActive => SelectedPageTag == "edituid";

    public double DeviceUiOpacity => IsConnected ? 1.0 : 0.35;

    public System.Windows.Media.Brush ConnectionBrush =>
        IsConnected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(DeviceUiOpacity));
        OnPropertyChanged(nameof(ConnectionBrush));
    }

    partial void OnSelectedPageTagChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsScannerActive));
        OnPropertyChanged(nameof(IsLibraryActive));
        OnPropertyChanged(nameof(IsWriteActive));
        OnPropertyChanged(nameof(IsEditUidActive));

        if (value == "library")
        {
            Library.Refresh();
        }
        else if (value == "write")
        {
            Write.RefreshSources();
        }
    }

    public Pm3Activity Activity => Pm3Activity.Instance;

    public MainViewModel()
    {
        Dashboard.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Dashboard.SelectedPort))
            {
                ActivePort = Dashboard.SelectedPort?.PortName ?? "Disconnected";
            }
            if (e.PropertyName == nameof(Dashboard.IsConnected))
            {
                IsConnected = Dashboard.IsConnected;
                if (!IsConnected)
                {
                    SelectedPageTag = "dashboard";
                }
                else
                {
                    ActivePort = Dashboard.SelectedPort?.PortName ?? "Connected";
                }
            }
        };

        TagScanner.RequestNavigation += tag => SelectedPageTag = tag;

        Library.WriteToCardRequested += entry =>
        {
            Write.SelectSource(entry);
            SelectedPageTag = "write";
        };
    }

    [RelayCommand]
    public void NavigateTo(string tag)
    {
        SelectedPageTag = tag;
    }

    [RelayCommand]
    public void OpenExternalTerminal()
    {
        try
        {
            var env = Pm3EnvironmentResolver.Instance;
            if (!env.IsResolved) env.Resolve();

            string port = Dashboard.SelectedPort?.PortName ?? "COM9";
            string rootDir = env.RootDirectory;

            string batScript = File.Exists(Path.Combine(rootDir, "pm3.bat"))
                ? Path.Combine(rootDir, "pm3.bat")
                : (File.Exists(Path.Combine(env.ClientDirectory, "pm3.bat")) ? Path.Combine(env.ClientDirectory, "pm3.bat") : string.Empty);

            string cmdArgs;
            if (!string.IsNullOrEmpty(batScript))
            {
                cmdArgs = $"/k \"cd /d \"{Path.GetDirectoryName(batScript)}\" && call \"{batScript}\" {port}\"";
            }
            else
            {
                // Direct fallback using setup.bat in client directory
                cmdArgs = $"/k \"cd /d \"{env.ClientDirectory}\" && if exist \"{env.ClientDirectory}\\setup.bat\" call \"{env.ClientDirectory}\\setup.bat\" && \"{env.ClientDirectory}\\proxmark3.exe\" {port} -w\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                WorkingDirectory = env.ClientDirectory,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not launch external terminal: {ex.Message}", "Terminal Launch Error");
        }
    }
}
