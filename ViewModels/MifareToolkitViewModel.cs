using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class MifareToolkitViewModel : ObservableObject
{
    [ObservableProperty]
    private MifareCardData _cardData = MifareCardData.CreateEmpty1K();

    public event Action<string>? RequestNavigation;

    [RelayCommand]
    public void BackToScanner() => RequestNavigation?.Invoke("scanner");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready. Check keys or dump sectors.";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [RelayCommand]
    public void CopyHex(string? hex)
    {
        if (!string.IsNullOrEmpty(hex))
        {
            Clipboard.SetText(hex);
            StatusMessage = $"Copied hex to clipboard: {hex}";
        }
    }

    [RelayCommand]
    public async Task CheckKeysAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking default keys (hf mf chk --1k)...";

        try
        {
            var card = await MifareService.Instance.CheckKeysAsync();
            CardData = card;
            StatusMessage = "Key check completed! Found keys for sectors.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Key check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ReadAllBlocksAsync()
    {
        IsBusy = true;
        StatusMessage = "Reading all 64 blocks...";
        ProgressValue = 0;

        var progress = new Progress<(int current, int total)>(p =>
        {
            ProgressValue = (int)((double)p.current / p.total * 100);
            ProgressText = $"Reading block {p.current} / {p.total}...";
        });

        try
        {
            await MifareService.Instance.ReadAllBlocksAsync(CardData, progress);
            OnPropertyChanged(nameof(CardData));
            StatusMessage = $"Full dump complete! UID: {CardData.Uid}, 64 blocks loaded.";
            ProgressText = "Complete (64/64)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }


    [RelayCommand]
    public async Task WriteBlockDirectAsync(MifareBlock? block)
    {
        if (block == null) return;

        if (!block.IsValid)
        {
            StatusMessage = $"Cannot write block {block.BlockNumber:D2}: {block.ValidationTip}";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Writing block {block.BlockNumber:D2}...";

        try
        {
            int secNum = block.BlockNumber / 4;
            string keyA = CardData.Sectors[secNum].KeyA;
            string cleanHex = block.EditHex.Trim().ToUpperInvariant();

            bool ok = await MifareService.Instance.WriteBlockAsync(block.BlockNumber, keyA, cleanHex);
            if (ok)
            {
                block.MarkSaved();
                StatusMessage = $"Block {block.BlockNumber:D2} written successfully!";
            }
            else
            {
                StatusMessage = $"Failed to write block {block.BlockNumber:D2}. Check keys or card permissions.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Write error on block {block.BlockNumber:D2}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void RevertBlock(MifareBlock? block)
    {
        if (block == null) return;
        block.Revert();
        StatusMessage = $"Reverted block {block.BlockNumber:D2} to original dump data.";
    }

    [RelayCommand]
    public void SaveDumpJson()
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string safeUid = string.IsNullOrEmpty(CardData.Uid) ? "card" : CardData.Uid;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(env.DumpsDirectory, $"mifare_1k_{safeUid}_{timestamp}.json");

        try
        {
            CardData.SaveAsJson(path);
            StatusMessage = $"Saved dump to: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SaveDumpEml()
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string safeUid = string.IsNullOrEmpty(CardData.Uid) ? "card" : CardData.Uid;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(env.DumpsDirectory, $"mifare_1k_{safeUid}_{timestamp}.eml");

        try
        {
            CardData.SaveAsEml(path);
            StatusMessage = $"Saved EML dump to: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save error: {ex.Message}";
        }
    }
}
