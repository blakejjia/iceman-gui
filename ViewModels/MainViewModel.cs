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
    private TerminalViewModel _terminal = new();

    [ObservableProperty]
    private string _activePort = "No Port";

    [ObservableProperty]
    private bool _isConnected;

    public MainViewModel()
    {
        Dashboard.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Dashboard.SelectedPort))
            {
                ActivePort = Dashboard.SelectedPort?.PortName ?? "No Port";
            }
            if (e.PropertyName == nameof(Dashboard.IsConnected))
            {
                IsConnected = Dashboard.IsConnected;
            }
        };
    }

    [RelayCommand]
    public void CancelExecution()
    {
        Pm3ProcessService.Instance.CancelCurrent();
    }
}
