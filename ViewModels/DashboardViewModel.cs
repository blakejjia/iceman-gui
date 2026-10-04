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

    public DashboardViewModel()
    {
        RefreshPorts();
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
            var hw = await HardwareService.Instance.GetHardwareInfoAsync();
            Hardware = hw;
            IsConnected = true;
            StatusMessage = $"Connected to {SelectedPort.PortName} ({hw.Model})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
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
            // Trigger property change notification
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
