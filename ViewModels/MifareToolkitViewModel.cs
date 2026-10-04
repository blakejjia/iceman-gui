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

    [ObservableProperty]
    private string _newUidInput = "11223344";

    [ObservableProperty]
    private bool _isGen2Cuid = true;

    [ObservableProperty]
    private MifareBlock? _selectedBlock;

    [ObservableProperty]
    private string _editBlockDataHex = string.Empty;

    partial void OnSelectedBlockChanged(MifareBlock? value)
    {
        if (value != null)
        {
            EditBlockDataHex = value.DataHex;
        }
    }

    [RelayCommand]
    public void EditBlock(MifareBlock? block)
    {
        if (block == null) return;
        SelectedBlock = block;
        EditBlockDataHex = block.DataHex;
        StatusMessage = $"Selected Block {block.BlockNumber:D2} for editing. Modify hex data and click 'Write Block'.";
    }

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
    public async Task ChangeUidAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUidInput))
        {
            StatusMessage = "Please enter an 8-character hex UID (4 bytes).";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Writing new UID [{NewUidInput}] to card...";

        try
        {
            var (success, msg) = await MifareService.Instance.ChangeUidAsync(NewUidInput, IsGen2Cuid);
            StatusMessage = msg;
            if (success)
            {
                CardData.Uid = NewUidInput.ToUpperInvariant();
                OnPropertyChanged(nameof(CardData));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"UID change error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task WriteSelectedBlockAsync()
    {
        if (SelectedBlock == null)
        {
            StatusMessage = "Select a block from the sector list first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditBlockDataHex) || EditBlockDataHex.Length != 32)
        {
            StatusMessage = "Block data must be exactly 32 hex characters (16 bytes).";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Writing block {SelectedBlock.BlockNumber}...";

        try
        {
            int secNum = SelectedBlock.BlockNumber / 4;
            string keyA = CardData.Sectors[secNum].KeyA;

            bool ok = await MifareService.Instance.WriteBlockAsync(SelectedBlock.BlockNumber, keyA, EditBlockDataHex);
            if (ok)
            {
                SelectedBlock.DataHex = EditBlockDataHex.ToUpperInvariant();
                SelectedBlock.DataAscii = MifareBlock.HexToAscii(EditBlockDataHex);
                OnPropertyChanged(nameof(SelectedBlock));
                OnPropertyChanged(nameof(CardData));
                StatusMessage = $"Block {SelectedBlock.BlockNumber} written successfully!";
            }
            else
            {
                StatusMessage = $"Failed to write block {SelectedBlock.BlockNumber}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Write error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
