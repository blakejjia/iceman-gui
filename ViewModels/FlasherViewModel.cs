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

    [ObservableProperty]
    private bool _isFlashSuccess;

    public Func<Models.HardwareInfo?>? GetDeviceHardware { get; set; }
    public Func<Task>? RequestConnectAfterFlash { get; set; }

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

        // Check if device is already verified to be running Iceman firmware
        var hw = GetDeviceHardware?.Invoke();
        if (hw != null && hw.IsVerifiedIceman)
        {
            string deviceDesc = !string.IsNullOrWhiteSpace(hw.Model) ? hw.Model : "Proxmark3";
            string osInfo = !string.IsNullOrWhiteSpace(hw.OsVersion) ? $"\n• Installed OS: {hw.OsVersion}" : "";
            string clientInfo = !string.IsNullOrWhiteSpace(hw.ClientVersion) ? $"\n• Client Build: {hw.ClientVersion}" : "";

            bool proceed = false;
            try
            {
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                    Title = "Device Already Running Iceman Firmware",
                    Content = $"Your {deviceDesc} is already running verified Iceman firmware!{osInfo}{clientInfo}\n\nYou are already ready to go! There is usually no need to re-flash unless you are recovering from an error or manually updating.\n\nAre you sure you want to flash again?",
                    PrimaryButtonText = "Flash Anyway",
                    CloseButtonText = "Cancel"
                };

                var dialogResult = await msgBox.ShowDialogAsync();
                proceed = (dialogResult == Wpf.Ui.Controls.MessageBoxResult.Primary);
            }
            catch
            {
                var result = System.Windows.MessageBox.Show(
                    $"Your {deviceDesc} is already running verified Iceman firmware!{osInfo}{clientInfo}\n\nYou are already ready to go! There is usually no need to re-flash unless you are recovering from an error or manually updating.\n\nAre you sure you want to flash again?",
                    "Device Already Running Iceman Firmware",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                proceed = (result == System.Windows.MessageBoxResult.Yes);
            }

            if (!proceed)
            {
                StatusMessage = "Flashing cancelled: Device is already running verified Iceman firmware.";
                return;
            }
        }

        HasFlashingStarted = true;
        IsFlashing = true;
        IsFlashSuccess = false;
        StatusMessage = $"Starting flash on {port}...";
        FlashLog = $"[*] Initiating safe firmware flash on {port}...\n[*] Arguments: --unlock-bootloader --image bootrom.elf --image fullimage.elf\n\n";

        try
        {
            // Release COM port so flasher can access it
            Pm3ProcessService.Instance.StopInteractiveSession();

            // Flash with bootloader unlock always enabled
            string result = await FlasherService.Instance.FlashDeviceAsync(port, flashBootloader: true);
            FlashLog += result;

            if (!result.Contains("[!] Error", StringComparison.OrdinalIgnoreCase) && !result.Contains("Error during flashing", StringComparison.OrdinalIgnoreCase))
            {
                IsFlashSuccess = true;
                StatusMessage = "Flashing succeeded! The device has rebooted into Iceman OS. Click 'Connect to Device' to begin.";
            }
            else
            {
                IsFlashSuccess = false;
                StatusMessage = "Flashing completed with errors. Check log output.";
            }
        }
        catch (Exception ex)
        {
            IsFlashSuccess = false;
            FlashLog += $"\n[!] Error during flashing: {ex.Message}\n";
            StatusMessage = $"Flash failed: {ex.Message}";
        }
        finally
        {
            IsFlashing = false;
        }
    }

    [RelayCommand]
    public async Task ConnectDeviceAfterFlashAsync()
    {
        if (RequestConnectAfterFlash != null)
        {
            await RequestConnectAfterFlash.Invoke();
        }
    }

    [RelayCommand]
    public void OpenRecoveryGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/RfidResearchGroup/proxmark3/blob/master/doc/jtag_notes.md",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FlashLog += $"\n[!] Could not open web guide: {ex.Message}\n";
        }
    }
}
