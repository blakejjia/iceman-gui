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
    private MifareToolkitViewModel _mifareToolkit = new();

    [ObservableProperty]
    private FlasherViewModel _flasher = new();

    [ObservableProperty]
    private string _activePort = "Disconnected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isMifareDetected;

    [ObservableProperty]
    private string _selectedPageTag = "dashboard";

    public bool IsDashboardActive => SelectedPageTag == "dashboard";
    public bool IsScannerActive => SelectedPageTag == "scanner";
    public bool IsMifareActive => SelectedPageTag == "mifare";
    public bool IsFlasherActive => SelectedPageTag == "flasher";

    partial void OnSelectedPageTagChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsScannerActive));
        OnPropertyChanged(nameof(IsMifareActive));
        OnPropertyChanged(nameof(IsFlasherActive));
    }

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
                    IsMifareDetected = false;
                    SelectedPageTag = "dashboard";
                }
                else
                {
                    ActivePort = Dashboard.SelectedPort?.PortName ?? "Connected";
                }
            }
        };

        Dashboard.RequestNavigation += tag => SelectedPageTag = tag;

        TagScanner.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TagScanner.CurrentTag))
            {
                var tag = TagScanner.CurrentTag;
                if (tag != null && (tag.IsMifare || 
                    tag.TagType.Contains("MIFARE", StringComparison.OrdinalIgnoreCase) ||
                    tag.TagType.Contains("ISO14443-A", StringComparison.OrdinalIgnoreCase)))
                {
                    IsMifareDetected = true;
                    // Also seed MifareToolkit with this card's UID
                    if (!string.IsNullOrEmpty(tag.Uid))
                    {
                        MifareToolkit.CardData.Uid = tag.Uid;
                    }
                }
            }
        };

        TagScanner.RequestNavigation += tag => SelectedPageTag = tag;
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
                cmdArgs = $"/k \"cd /d \"{env.ClientDirectory}\" && if exist setup.bat call setup.bat && proxmark3.exe {port} -w\"";
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

    [RelayCommand]
    public void CancelExecution()
    {
        Pm3ProcessService.Instance.CancelCurrent();
    }
}
