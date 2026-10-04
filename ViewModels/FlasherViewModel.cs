using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;
using iceman_gui.Core;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class FlasherViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFlash))]
    private bool _isFirmwareReady;

    [ObservableProperty]
    private InfoBarSeverity _firmwareSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _firmwareStatusText = "Checking firmware binaries...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFlash))]
    private bool _isFlashing;

    [ObservableProperty]
    private bool _hasFlashingStarted;

    [ObservableProperty]
    private string _statusMessage = "Ready to flash.";

    [ObservableProperty]
    private string _flashLog = string.Empty;

    [ObservableProperty]
    private string _targetPort = string.Empty;

    public bool CanFlash => IsFirmwareReady && !IsFlashing;

    public FlasherViewModel()
    {
        CheckFirmware();
    }

    [RelayCommand]
    public void CheckFirmware()
    {
        var (hasBoot, hasFull, bootPath, fullPath) = FlasherService.Instance.CheckFirmwareFiles();
        IsFirmwareReady = hasBoot && hasFull;

        if (IsFirmwareReady)
        {
            FirmwareSeverity = InfoBarSeverity.Success;
            FirmwareStatusText = "Ready: Official Iceman firmware binaries (bootrom.elf & fullimage.elf) verified.";
        }
        else
        {
            FirmwareSeverity = InfoBarSeverity.Warning;
            if (!hasBoot && !hasFull)
            {
                FirmwareStatusText = "Not Ready: Missing bootrom.elf and fullimage.elf in client directory.";
            }
            else if (!hasBoot)
            {
                FirmwareStatusText = "Not Ready: Missing bootrom.elf in client directory.";
            }
            else
            {
                FirmwareStatusText = "Not Ready: Missing fullimage.elf in client directory.";
            }
        }
    }

    [RelayCommand]
    public async Task FlashAsync()
    {
        string port = !string.IsNullOrWhiteSpace(TargetPort) && TargetPort != "Disconnected"
            ? TargetPort 
            : Pm3ProcessService.Instance.CurrentPort;

        if (string.IsNullOrWhiteSpace(port) || port == "Disconnected")
        {
            StatusMessage = "No COM port selected. Please select a port on the Dashboard first.";
            return;
        }

        HasFlashingStarted = true;
        IsFlashing = true;
        StatusMessage = $"Starting flash on {port}...";
        FlashLog = $"[*] Initiating safe firmware flash on {port}...\n[*] Arguments: --unlock-bootloader --image bootrom.elf --image fullimage.elf\n\n";

        try
        {
            // Flash with bootloader unlock always enabled
            string result = await FlasherService.Instance.FlashDeviceAsync(port, flashBootloader: true);
            FlashLog += result;
            StatusMessage = "Flashing operation finished. Check log output.";
        }
        catch (Exception ex)
        {
            FlashLog += $"\n[!] Error during flashing: {ex.Message}\n";
            StatusMessage = $"Flash failed: {ex.Message}";
        }
        finally
        {
            IsFlashing = false;
        }
    }

    [RelayCommand]
    public void OpenRecoveryGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/RfidResearchGroup/proxmark3/blob/master/doc/recovery.md",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FlashLog += $"\n[!] Could not open web guide: {ex.Message}\n";
        }
    }
}
