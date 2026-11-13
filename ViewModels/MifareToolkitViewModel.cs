using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

/// <summary>
/// Edits a saved backup. Changes are written back to the dump files, never to
/// a card: writing to hardware only happens through the Write wizard.
/// </summary>
public partial class MifareToolkitViewModel : ObservableObject
{
    [ObservableProperty]
    private MifareCardData _cardData = MifareCardData.CreateEmpty1K();

    [ObservableProperty]
    private bool _isEditingBackup;

    [ObservableProperty]
    private string _backupName = string.Empty;

    [ObservableProperty]
    private string _backupJsonPath = string.Empty;

    [ObservableProperty]
    private string _backupBinPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Open a backup from the card library.";

    public event Action<string>? RequestNavigation;

    [RelayCommand]
    public void Back() => RequestNavigation?.Invoke("back");

    public void LoadBackup(DumpEntry entry)
    {
        MifareDumpFile? dump = DumpLibrary.LoadDump(entry.Uid);
        if (dump == null)
        {
            StatusMessage = "Could not read the backup file.";
            return;
        }

        CardData = dump.ToCardData();
        IsEditingBackup = true;
        BackupName = $"{entry.DisplayName} · {entry.Uid}";
        BackupJsonPath = entry.DumpJsonPath;
        BackupBinPath = entry.DumpBinPath;
        StatusMessage = "Editing the saved backup. Changes are written to the dump files, not to a card.";
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
    public void SaveBackup()
    {
        if (!IsEditingBackup)
        {
            StatusMessage = "Open a backup first.";
            return;
        }

        try
        {
            SaveBackupFiles();
            foreach (MifareSector sector in CardData.Sectors)
            {
                foreach (MifareBlock block in sector.Blocks)
                {
                    block.MarkSaved();
                }
            }

            StatusMessage = "Backup saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Save error: " + ex.Message;
        }
    }

    [RelayCommand]
    public void SaveBlock(MifareBlock? block)
    {
        if (block == null)
        {
            return;
        }

        if (!block.IsValid)
        {
            StatusMessage = $"Cannot save block {block.BlockNumber:D2}: {block.ValidationTip}";
            return;
        }

        if (!IsEditingBackup)
        {
            StatusMessage = "Open a backup from the card library before editing blocks.";
            return;
        }

        try
        {
            SaveBackupFiles();
            block.MarkSaved();
            StatusMessage = $"Block {block.BlockNumber:D2} saved to the backup.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save error on block {block.BlockNumber:D2}: {ex.Message}";
        }
    }

    [RelayCommand]
    public void RevertBlock(MifareBlock? block)
    {
        if (block == null)
        {
            return;
        }

        block.Revert();
        StatusMessage = $"Reverted block {block.BlockNumber:D2} to the saved data.";
    }

    [RelayCommand]
    public void SaveDumpJson()
    {
        if (!IsEditingBackup)
        {
            return;
        }

        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string safeUid = string.IsNullOrEmpty(CardData.Uid) ? "card" : CardData.Uid;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(env.DumpsDirectory, $"mifare_1k_{safeUid}_{timestamp}.json");

        try
        {
            MifareDumpFile.FromCardData(CardData).Save(path);
            StatusMessage = $"Exported JSON to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Export error: " + ex.Message;
        }
    }

    [RelayCommand]
    public void SaveDumpEml()
    {
        if (!IsEditingBackup)
        {
            return;
        }

        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string safeUid = string.IsNullOrEmpty(CardData.Uid) ? "card" : CardData.Uid;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(env.DumpsDirectory, $"mifare_1k_{safeUid}_{timestamp}.eml");

        try
        {
            CardData.SaveAsEml(path);
            StatusMessage = $"Exported EML to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Export error: " + ex.Message;
        }
    }

    private void SaveBackupFiles()
    {
        MifareDumpFile dump = MifareDumpFile.FromCardData(CardData);
        dump.Save(BackupJsonPath);
        dump.SaveBinary(BackupBinPath);
    }
}
