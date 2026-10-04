using System;
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

    public FlasherViewModel Flasher { get; } = new();

    public DashboardViewModel()
    {
        RefreshPorts();
        Flasher.CheckFirmware();
        Flasher.GetDeviceHardware = () => IsConnected ? Hardware : null;
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
        if (SelectedPort == null)
        {
            StatusMessage = "No COM port selected.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Connecting to {SelectedPort.PortName}...";

        try
        {
            Pm3ProcessService.Instance.SetPort(SelectedPort.PortName);
            Flasher.TargetPort = SelectedPort.PortName;

            bool ok = await Pm3ProcessService.Instance.StartInteractiveSessionAsync(SelectedPort.PortName, 12000);
            if (!ok)
            {
                throw new Exception($"Failed to start Proxmark3 session on {SelectedPort.PortName}. Please ensure no other process is holding the port.");
            }

            var hw = await HardwareService.Instance.GetHardwareInfoAsync();
            Hardware = hw;
            IsConnected = true;
            StatusMessage = $"Connected to {SelectedPort.PortName} ({hw.Model})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
            Pm3ProcessService.Instance.StopInteractiveSession();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Disconnect()
    {
        Pm3ProcessService.Instance.StopInteractiveSession();
        IsConnected = false;
        IsShowingHardwareDetails = false;
        IsShowingOsDetails = false;
        StatusMessage = "Disconnected.";
    }

    [RelayCommand]
    public async Task MeasureAntennaAsync()
    {
        if (SelectedPort == null) return;
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
