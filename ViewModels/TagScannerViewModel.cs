using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class TagScannerViewModel : ObservableObject
{
    [ObservableProperty]
    private TagInfo? _currentTag;

    public bool HasCurrentTag => CurrentTag != null && (!string.IsNullOrEmpty(CurrentTag.Uid) || !string.IsNullOrEmpty(CurrentTag.CardNumber));
    public bool HasNoTag => !HasCurrentTag;

    public CardCapabilities Capabilities => CardCapabilities.For(CurrentTag);
    public bool CanBackup => CurrentTag != null && Capabilities.CanBackup && !IsBackingUp;
    public bool CannotBackup => HasCurrentTag && (!Capabilities.CanBackup || IsBackingUp);
    public string BackupHint => CurrentTag == null ? "Scan a card first." : Capabilities.Summary;

    partial void OnCurrentTagChanged(TagInfo? value)
    {
        OnPropertyChanged(nameof(HasCurrentTag));
        OnPropertyChanged(nameof(HasNoTag));
        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(CanBackup));
        OnPropertyChanged(nameof(CannotBackup));
        OnPropertyChanged(nameof(BackupHint));
        OnPropertyChanged(nameof(BackupUnsupported));

        BackupStatus = string.Empty;
        HasBackupStatus = false;
        HasBackupEntry = false;
        IsNamingBackup = false;
        BackupNameInput = string.Empty;
        _pendingBackupEntry = null;
    }

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private string _backupStatus = string.Empty;

    [ObservableProperty]
    private bool _hasBackupStatus;

    [ObservableProperty]
    private bool _hasBackupEntry;

    [ObservableProperty]
    private bool _backupFailed;

    public bool BackupUnsupported => HasCurrentTag && !Capabilities.CanBackup;

    [ObservableProperty]
    private bool _isNamingBackup;

    [ObservableProperty]
    private string _backupNameInput = string.Empty;

    private DumpEntry? _pendingBackupEntry;

    public bool CanSaveBackupName => !string.IsNullOrWhiteSpace(BackupNameInput);

    partial void OnBackupNameInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveBackupName));
    }

    partial void OnIsBackingUpChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBackup));
        OnPropertyChanged(nameof(CannotBackup));
    }

    public event Action<string>? RequestNavigation;

    [RelayCommand]
    public async Task ScanCardAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        using var activity = Pm3Activity.Instance.Begin("Scanning card");

        try
        {
            var tag = await TagScanService.Instance.ProgressiveScanAsync();
            if (!string.IsNullOrEmpty(tag.Uid) || !string.IsNullOrEmpty(tag.CardNumber))
            {
                CurrentTag = tag;
            }
            else
            {
                CurrentTag = null;
            }
        }
        catch (Exception ex)
        {
            CurrentTag = null;
            Debug.WriteLine($"Scan error: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public void ClearCard()
    {
        CurrentTag = null;
    }

    [RelayCommand]
    public Task QuickScanAsync() => ScanCardAsync();

    [RelayCommand]
    public Task FullScanAsync() => ScanCardAsync();

    [RelayCommand]
    public void CopyUid()
    {
        if (CurrentTag != null && !string.IsNullOrEmpty(CurrentTag.Uid))
        {
            Clipboard.SetText(CurrentTag.Uid);
        }
    }

    [RelayCommand]
    public async Task BackupCardAsync()
    {
        if (CurrentTag == null || !CanBackup)
        {
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Backing up card");
        IsBackingUp = true;
        BackupFailed = false;
        HasBackupEntry = false;
        HasBackupStatus = true;
        BackupStatus = "Finding keys and reading the card...";

        try
        {
            var progress = new Progress<string>(stage => BackupStatus = stage);
            var (result, entry) = await MifareService.Instance.BackupCardAsync(CurrentTag, null, progress);

            if (!result.Success)
            {
                BackupFailed = true;
                BackupStatus = result.Failure switch
                {
                    Pm3Failure.Unsupported => result.Error ?? "This card does not support automatic backup.",
                    Pm3Failure.TimedOut => "The card did not finish in time. The session was reset; reconnect the device if it stays unresponsive.",
                    Pm3Failure.StartFailed => result.Error ?? "Could not start the Proxmark3 session.",
                    Pm3Failure.SessionLost => "The Proxmark3 session ended unexpectedly.",
                    _ => result.Error ?? "Backup failed."
                };
                return;
            }

            if (entry == null)
            {
                BackupFailed = true;
                BackupStatus = "The backup finished, but no dump files were found. Check the client dump path.";
                return;
            }

            _pendingBackupEntry = entry;
            BackupNameInput = entry.Alias;
            IsNamingBackup = true;
            BackupStatus = "Backup saved. Give it a name.";
            HasBackupEntry = true;
        }
        catch (Exception ex)
        {
            BackupFailed = true;
            BackupStatus = "Backup error: " + ex.Message;
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    public void SaveBackupName()
    {
        if (_pendingBackupEntry != null)
        {
            string alias = BackupNameInput.Trim();
            if (alias.Length > 0)
            {
                DumpLibrary.SetAlias(_pendingBackupEntry.Uid, alias);
                BackupStatus = $"Saved \"{alias}\" to your card library.";
            }
            else
            {
                BackupStatus = $"Saved \"{_pendingBackupEntry.Alias}\" to your card library.";
            }
        }

        CloseNamingDialog();
    }

    [RelayCommand]
    public void SkipBackupName()
    {
        if (_pendingBackupEntry != null)
        {
            BackupStatus = $"Saved \"{_pendingBackupEntry.Alias}\" to your card library.";
        }

        CloseNamingDialog();
    }

    private void CloseNamingDialog()
    {
        IsNamingBackup = false;
        _pendingBackupEntry = null;
    }

    [RelayCommand]
    public void OpenLibrary() => RequestNavigation?.Invoke("library");

}
