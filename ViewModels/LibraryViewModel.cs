using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;
using Microsoft.Win32;

namespace iceman_gui.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DumpEntry> _entries = new();

    [ObservableProperty]
    private DumpEntry? _selectedEntry;

    [ObservableProperty]
    private string _aliasDraft = string.Empty;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _cardSummary = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => !HasEntries;

    public event Action<DumpEntry>? WriteToCardRequested;

    partial void OnEntriesChanged(ObservableCollection<DumpEntry> value)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
    }

    partial void OnSelectedEntryChanged(DumpEntry? value)
    {
        HasSelection = value != null;
        AliasDraft = value?.Alias ?? string.Empty;

        if (value == null)
        {
            CardSummary = string.Empty;
            return;
        }

        MifareDumpFile? dump = DumpLibrary.LoadDump(value.Uid);
        CardSummary = dump == null
            ? "The dump file could not be read."
            : $"UID {dump.Uid} · {dump.Blocks.Count} blocks · {dump.SectorKeys.Count} sectors · ATQA {dump.Atqa} · SAK {dump.Sak}";
    }

    public void Refresh(string? selectUid = null)
    {
        string? uid = selectUid ?? SelectedEntry?.Uid;
        Entries = new ObservableCollection<DumpEntry>(DumpLibrary.List());
        SelectedEntry = Entries.FirstOrDefault(entry => entry.Uid == uid) ?? Entries.FirstOrDefault();
        SetStatus($"{Entries.Count} saved card(s).");
    }

    [RelayCommand]
    public void RefreshList() => Refresh();

    [RelayCommand]
    public async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Proxmark3 dump",
            Filter = "Proxmark3 JSON dump (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DumpEntry? entry = await Task.Run(() => DumpLibrary.Import(dialog.FileName));
        if (entry == null)
        {
            SetStatus("That file is not a readable Proxmark3 dump.");
            return;
        }

        Refresh(entry.Uid);
        SetStatus($"Imported \"{entry.Alias}\".");
    }

    [RelayCommand]
    public void SaveAlias()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        string alias = AliasDraft.Trim();
        if (alias.Length == 0)
        {
            SetStatus("An alias cannot be empty.");
            return;
        }

        DumpLibrary.SetAlias(SelectedEntry.Uid, alias);
        Refresh(SelectedEntry.Uid);
        SetStatus("Alias saved.");
    }

    [RelayCommand]
    public void WriteToCard()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        WriteToCardRequested?.Invoke(SelectedEntry);
    }

    [RelayCommand]
    public void OpenJson()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        OpenPath(SelectedEntry.DumpJsonPath);
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        OpenPath(SelectedEntry.Folder);
    }

    [RelayCommand]
    public void OpenDumpsFolder() => OpenPath(AppPaths.DumpsDirectory);

    [RelayCommand]
    public void Delete()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"Delete the backup \"{SelectedEntry.DisplayName}\" ({SelectedEntry.Uid})?\n\nThis removes its dump, key file and metadata from the card library.",
            "Delete backup",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Directory.Delete(SelectedEntry.Folder, recursive: true);
            Refresh();
            SetStatus("Backup deleted.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not delete the backup: " + ex.Message);
        }
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatus = !string.IsNullOrWhiteSpace(message);
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            else if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }
}
