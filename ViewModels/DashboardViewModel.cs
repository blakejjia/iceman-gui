using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<PortItem> _ports = new();

    [ObservableProperty]
    private PortItem? _selectedPort;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _hasConnectionError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready. Select a port to connect.";

    [ObservableProperty]
    private HardwareInfo _hardware = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingMainDashboard))]
    private bool _isShowingHardwareDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingMainDashboard))]
    private bool _isShowingOsDetails;

    public bool IsShowingMainDashboard => !IsShowingHardwareDetails && !IsShowingOsDetails;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "Dark", "Light", "System" };

    [ObservableProperty]
    private string _selectedTheme = ThemeService.Current.ToString();

    partial void OnSelectedThemeChanged(string value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out AppTheme theme))
        {
            ThemeService.SetTheme(theme);
        }
    }

    public double DeviceControlsOpacity => IsConnected ? 1.0 : 0.35;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(DeviceControlsOpacity));

    public FlasherViewModel Flasher { get; } = new();

    public DashboardViewModel()
    {
        RefreshPorts();
        Flasher.CheckFirmware();
        Flasher.GetDeviceHardware = () => IsConnected ? Hardware : null;
        Flasher.RequestConnectAfterFlash = async () =>
        {
            BackToDashboard();
            await ConnectAsync();
        };

        _ = TryAutoConnectAsync();
    }

    private async Task TryAutoConnectAsync()
    {
        try
        {
            PortItem? detected = Ports.FirstOrDefault(port => port.IsProxmark && port.IsDetected);
            if (detected == null)
            {
                return;
            }

            SelectedPort = detected;
            await ConnectAsync();
        }
        catch
        {
            // Auto-connect is best-effort and must not affect startup.
        }
    }

    partial void OnSelectedPortChanged(PortItem? value)
    {
        if (value != null)
        {
            Flasher.TargetPort = value.PortName;
        }
    }

    [RelayCommand]
    public void ShowHardwareDetails()
    {
        IsShowingOsDetails = false;
        IsShowingHardwareDetails = true;
    }

    [RelayCommand]
    public void ShowOsDetails()
    {
        IsShowingHardwareDetails = false;
        IsShowingOsDetails = true;
    }

    [RelayCommand]
    public void BackToDashboard()
    {
        IsShowingHardwareDetails = false;
        IsShowingOsDetails = false;
    }

    [RelayCommand]
    public void RefreshPorts()
    {
        Ports.Clear();
        var list = SerialPortDetector.GetAvailablePorts();
        foreach (var p in list)
        {
            Ports.Add(p);
        }

        // Auto-select Proxmark3 port if found
        SelectedPort = Ports.FirstOrDefault(p => p.IsProxmark) ?? Ports.FirstOrDefault();
        if (SelectedPort != null)
        {
            Flasher.TargetPort = SelectedPort.PortName;
        }
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (IsConnected || IsBusy)
        {
            return;
        }

        if (SelectedPort == null)
        {
            StatusMessage = "No COM port selected.";
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Connecting to device");
        IsBusy = true;
        HasConnectionError = false;
        StatusMessage = $"Connecting to {SelectedPort.PortName}...";

        try
        {
            Flasher.TargetPort = SelectedPort.PortName;

            bool ok = await Pm3ProcessService.Instance.ConnectAsync(SelectedPort.PortName, 15_000);
            if (!ok)
            {
                throw new Exception($"Failed to start Proxmark3 session on {SelectedPort.PortName}. Please ensure no other process is holding the port.");
            }

            var hw = await HardwareService.Instance.GetHardwareInfoAsync();
            Hardware = hw;
            IsConnected = true;
            HasConnectionError = false;
            StatusMessage = $"Connected to {SelectedPort.PortName} ({hw.Model})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
            HasConnectionError = true;
            await Pm3ProcessService.Instance.DisconnectAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        await Pm3ProcessService.Instance.DisconnectAsync();
        IsConnected = false;
        HasConnectionError = false;
        IsShowingHardwareDetails = false;
        IsShowingOsDetails = false;
        StatusMessage = "Disconnected.";
    }

    [RelayCommand]
    public async Task MeasureAntennaAsync()
    {
        if (SelectedPort == null) return;
        using var activity = Pm3Activity.Instance.Begin("Tuning antenna");
        IsBusy = true;
        StatusMessage = "Tuning antenna (hw tune)...";

        try
        {
            await HardwareService.Instance.MeasureAntennaAsync(Hardware);
            OnPropertyChanged(nameof(Hardware));
            StatusMessage = $"Antenna tuned. LF: {Hardware.LfTuneStatus}, HF: {Hardware.HfTuneStatus}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Antenna tune failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
