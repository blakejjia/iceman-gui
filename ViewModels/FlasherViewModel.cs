using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class FlasherViewModel : ObservableObject
{
    [ObservableProperty]
    private string _firmwareFilesStatus = string.Empty;

    [ObservableProperty]
    private bool _unlockBootloader;

    [ObservableProperty]
    private bool _isFlashing;

    [ObservableProperty]
    private string _statusMessage = "Ready. Verify firmware files before flashing.";

    [ObservableProperty]
    private string _flashLog = string.Empty;

    public FlasherViewModel()
    {
        CheckFirmware();
    }

    [RelayCommand]
    public void CheckFirmware()
    {
        var (hasBoot, hasFull, bootPath, fullPath) = FlasherService.Instance.CheckFirmwareFiles();
        FirmwareFilesStatus = $"bootrom.elf: {(hasBoot ? "Found" : "Missing")}\nfullimage.elf: {(hasFull ? "Found" : "Missing")}";
    }

    [RelayCommand]
    public async Task FlashAsync()
    {
        string port = Pm3ProcessService.Instance.CurrentPort;
        if (string.IsNullOrWhiteSpace(port))
        {
            StatusMessage = "No COM port selected. Connect or select port in Dashboard first.";
            return;
        }

        IsFlashing = true;
        StatusMessage = $"Starting flash on {port}...";
        FlashLog = $"[*] Flashing device on {port}...\n[*] Unlock Bootloader: {UnlockBootloader}\n";

        try
        {
            string result = await FlasherService.Instance.FlashDeviceAsync(port, UnlockBootloader);
            FlashLog += result;
            StatusMessage = "Flashing operation completed. Check log output.";
        }
        catch (Exception ex)
        {
            FlashLog += $"\n[!] Error: {ex.Message}";
            StatusMessage = $"Flash failed: {ex.Message}";
        }
        finally
        {
            IsFlashing = false;
        }
    }
}
