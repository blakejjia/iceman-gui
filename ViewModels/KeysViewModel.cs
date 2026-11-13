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

public partial class KeyRow : ObservableObject
{
    public int Sector { get; }

    [ObservableProperty]
    private string _keyA;

    [ObservableProperty]
    private string _keyB;

    public KeyRow(int sector, string keyA, string keyB)
    {
        Sector = sector;
        _keyA = keyA;
        _keyB = keyB;
    }
}

public partial class DictionaryKeyEntry : ObservableObject
{
    [ObservableProperty]
    private string _value;

    public DictionaryKeyEntry(string value)
    {
        _value = value;
    }
}

public partial class KeysViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<KeyFileEntry> _keyFiles = new();

    [ObservableProperty]
    private KeyFileEntry? _selectedFile;

    [ObservableProperty]
    private ObservableCollection<KeyRow> _rows = new();

    [ObservableProperty]
    private ObservableCollection<DictionaryKeyEntry> _dictionaryKeys = new();

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isBin;

    [ObservableProperty]
    private bool _isDictionary;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    public bool HasFiles => KeyFiles.Count > 0;
    public bool HasNoFiles => !HasFiles;

    public KeysViewModel()
    {
        Refresh();
    }

    partial void OnKeyFilesChanged(ObservableCollection<KeyFileEntry> value)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasNoFiles));
    }

    partial void OnSelectedFileChanged(KeyFileEntry? value) => LoadFile(value);

    public void Refresh(KeyFileEntry? select = null)
    {
        string? path = select?.Path ?? SelectedFile?.Path;
        KeyFiles = new ObservableCollection<KeyFileEntry>(KeyLibrary.List());
        SelectedFile = KeyFiles.FirstOrDefault(entry => entry.Path == path) ?? KeyFiles.FirstOrDefault();
        SetStatus($"{KeyFiles.Count} key file(s).");
    }

    [RelayCommand]
    public void RefreshList() => Refresh();

    [RelayCommand]
    public void NewKeyFile()
    {
        KeyFileEntry entry = KeyLibrary.CreateNewBin();
        Refresh(entry);
        SetStatus($"Created {entry.Name}.");
    }

    [RelayCommand]
    public async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import key file",
            Filter = "Key files (*.bin;*.dic)|*.bin;*.dic|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        KeyFileEntry? entry = await Task.Run(() => KeyLibrary.Import(dialog.FileName));
        if (entry == null)
        {
            SetStatus("That file is not a .bin or .dic key file.");
            return;
        }

        Refresh(entry);
        SetStatus($"Imported {entry.Name}.");
    }

    [RelayCommand]
    public void Delete()
    {
        if (SelectedFile == null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"Delete the key file \"{SelectedFile.Name}\"?",
            "Delete key file",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            KeyLibrary.Delete(SelectedFile);
            Refresh();
            SetStatus("Key file deleted.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not delete the key file: " + ex.Message);
        }
    }

    [RelayCommand]
    public void Save()
    {
        if (SelectedFile == null)
        {
            return;
        }

        var file = new MifareKeyFile
        {
            Path = SelectedFile.Path,
            IsDictionary = IsDictionary
        };

        if (IsDictionary)
        {
            foreach (DictionaryKeyEntry entry in DictionaryKeys)
            {
                string value = entry.Value.Trim().ToUpperInvariant();
                if (value.Length > 0)
                {
                    file.DictionaryKeys.Add(value);
                }
            }
        }
        else
        {
            foreach (KeyRow row in Rows)
            {
                file.Sectors.Add(new MifareKeyPair(
                    row.KeyA.Trim().ToUpperInvariant(),
                    row.KeyB.Trim().ToUpperInvariant()));
            }
        }

        string? invalid = file.FirstInvalidKey();
        if (invalid != null)
        {
            SetStatus($"Invalid key \"{invalid}\": each key must be 12 hex characters.");
            return;
        }

        try
        {
            file.Save(SelectedFile.Path);
            Refresh(SelectedFile);
            SetStatus("Key file saved.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save the key file: " + ex.Message);
        }
    }

    [RelayCommand]
    public void AddDictionaryKey()
    {
        DictionaryKeys.Add(new DictionaryKeyEntry("FFFFFFFFFFFF"));
    }

    [RelayCommand]
    public void RemoveDictionaryKey(DictionaryKeyEntry? entry)
    {
        if (entry != null)
        {
            DictionaryKeys.Remove(entry);
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.KeysDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppPaths.KeysDirectory}\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void LoadFile(KeyFileEntry? entry)
    {
        Rows.Clear();
        DictionaryKeys.Clear();
        HasSelection = entry != null;
        IsBin = false;
        IsDictionary = false;
        FileName = entry?.Name ?? string.Empty;

        if (entry == null)
        {
            return;
        }

        try
        {
            MifareKeyFile file = MifareKeyFile.Load(entry.Path);
            IsBin = !file.IsDictionary;
            IsDictionary = file.IsDictionary;

            if (file.IsDictionary)
            {
                foreach (string key in file.DictionaryKeys)
                {
                    DictionaryKeys.Add(new DictionaryKeyEntry(key));
                }
            }
            else
            {
                for (int sector = 0; sector < file.Sectors.Count; sector++)
                {
                    Rows.Add(new KeyRow(sector, file.Sectors[sector].KeyA, file.Sectors[sector].KeyB));
                }
            }

            SetStatus($"Loaded {entry.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not load the key file: " + ex.Message);
        }
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatus = !string.IsNullOrWhiteSpace(message);
    }
}
