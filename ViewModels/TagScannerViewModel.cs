using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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

    public bool IsMifareCardDetected => CurrentTag != null && CurrentTag.IsMifare;
    public bool HasCurrentTag => CurrentTag != null && (!string.IsNullOrEmpty(CurrentTag.Uid) || !string.IsNullOrEmpty(CurrentTag.CardNumber));
    public bool HasNoTag => !HasCurrentTag;
    public bool CanWriteUid => CurrentTag != null && CurrentTag.CanChangeUid;
    public bool CannotWriteUid => HasCurrentTag && !CanWriteUid;

    public CardCapabilities Capabilities => CardCapabilities.For(CurrentTag);
    public bool CanBackup => CurrentTag != null && Capabilities.CanBackup && !IsBackingUp;
    public bool CannotBackup => HasCurrentTag && (!Capabilities.CanBackup || IsBackingUp);
    public string BackupHint => CurrentTag == null ? "Scan a card first." : Capabilities.Summary;

    public string DetectedMagicModeText
    {
        get
        {
            if (CurrentTag == null) return "Unknown";
            if (CurrentTag.IsGen1a) return "Gen 1a (Magic Backdoor Unlock)";
            if (CurrentTag.IsGen2Cuid) return "Gen 2 / CUID (Direct Block 0 Write)";
            return !string.IsNullOrWhiteSpace(CurrentTag.MagicType)
                ? $"{CurrentTag.MagicType} (Direct Block 0 Write)"
                : "Gen 2 / CUID (Direct Block 0 Write)";
        }
    }

    partial void OnCurrentTagChanged(TagInfo? value)
    {
        OnPropertyChanged(nameof(IsMifareCardDetected));
        OnPropertyChanged(nameof(HasCurrentTag));
        OnPropertyChanged(nameof(HasNoTag));
        OnPropertyChanged(nameof(CanWriteUid));
        OnPropertyChanged(nameof(CannotWriteUid));
        OnPropertyChanged(nameof(DetectedMagicModeText));
        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(CanBackup));
        OnPropertyChanged(nameof(CannotBackup));
        OnPropertyChanged(nameof(BackupHint));

        DumpEntry? existing = value == null ? null : DumpLibrary.Get(value.Uid);
        BackupAlias = value == null
            ? string.Empty
            : (existing?.Alias ?? DumpLibrary.SuggestAlias(value));
        BackupStatus = string.Empty;
        HasBackupStatus = false;
        HasBackupEntry = false;
    }

    [ObservableProperty]
    private bool _isWritingUidDialogOpen;

    [ObservableProperty]
    private string _writeUidInput = string.Empty;

    [ObservableProperty]
    private bool _isWritingUid;

    public bool IsNotWritingUid => !IsWritingUid;

    partial void OnIsWritingUidChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotWritingUid));
    }

    [ObservableProperty]
    private string _writeUidResult = string.Empty;

    [ObservableProperty]
    private bool _hasWriteUidResult;

    [ObservableProperty]
    private bool _isWriteUidSuccess;

    [ObservableProperty]
    private string _writeUidButtonText = "Write UID";

    public bool CanCancelWriteUid => !IsWriteUidSuccess;

    public Brush WriteUidResultBrush => IsWriteUidSuccess ? Brushes.LimeGreen : Brushes.LightCoral;

    partial void OnIsWriteUidSuccessChanged(bool value)
    {
        OnPropertyChanged(nameof(WriteUidResultBrush));
        OnPropertyChanged(nameof(CanCancelWriteUid));
        WriteUidButtonText = value ? "Finish" : "Write UID";
    }

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private string _backupAlias = string.Empty;

    [ObservableProperty]
    private string _backupStatus = string.Empty;

    [ObservableProperty]
    private bool _hasBackupStatus;

    [ObservableProperty]
    private bool _hasBackupEntry;

    partial void OnIsBackingUpChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBackup));
        OnPropertyChanged(nameof(CannotBackup));
    }

    [ObservableProperty]
    private string _lastSavedPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingScanner))]
    private bool _isShowingHistory;

    public bool IsShowingScanner => !IsShowingHistory;

    public ObservableCollection<TagInfo> ScanHistory { get; } = new();

    public event Action<string>? RequestNavigation;

    public TagScannerViewModel()
    {
        _ = LoadHistorySilentlyAsync();
    }

    [RelayCommand]
    public async Task ShowHistoryAsync()
    {
        await LoadHistoryAsync();
        IsShowingHistory = true;
    }

    [RelayCommand]
    public void BackToScanner()
    {
        IsShowingHistory = false;
    }

    [RelayCommand]
    public void SelectHistoryItem(TagInfo item)
    {
        if (item == null) return;
        CurrentTag = item;
        IsShowingHistory = false;
    }

    [RelayCommand]
    public async Task ScanCardAsync()
    {
        if (IsScanning) return;
        IsScanning = true;

        try
        {
            var tag = await TagScanService.Instance.ProgressiveScanAsync();
            if (!string.IsNullOrEmpty(tag.Uid) || !string.IsNullOrEmpty(tag.CardNumber))
            {
                CurrentTag = tag;
                var path = await TagScanService.Instance.AutoSaveScanAsync(tag);
                LastSavedPath = path;
                ScanHistory.Insert(0, tag);
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
    public void OpenWriteUidDialog()
    {
        if (CurrentTag == null || !CanWriteUid) return;

        WriteUidInput = CurrentTag.Uid;
        WriteUidResult = string.Empty;
        HasWriteUidResult = false;
        IsWriteUidSuccess = false;
        WriteUidButtonText = "Write UID";
        OnPropertyChanged(nameof(DetectedMagicModeText));
        IsWritingUidDialogOpen = true;
    }

    [RelayCommand]
    public void CloseWriteUidDialog()
    {
        IsWritingUidDialogOpen = false;
    }

    [RelayCommand]
    public void GenerateRandomUid()
    {
        byte[] bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        WriteUidInput = Convert.ToHexString(bytes);
    }

    [RelayCommand]
    public async Task ExecuteWriteUidAsync()
    {
        if (IsWriteUidSuccess)
        {
            CloseWriteUidDialog();
            return;
        }

        if (CurrentTag == null || !CanWriteUid) return;

        string cleanHex = WriteUidInput.Replace(" ", "").Replace(":", "").Trim().ToUpperInvariant();
        if (cleanHex.Length != 8 || !Regex.IsMatch(cleanHex, @"^[0-9A-Fa-f]{8}$"))
        {
            WriteUidResult = "UID must be exactly 4 bytes (8 hex characters), e.g. 11223344.";
            HasWriteUidResult = true;
            IsWriteUidSuccess = false;
            OnPropertyChanged(nameof(WriteUidResultBrush));
            return;
        }

        bool isGen1a = CurrentTag.IsGen1a;
        bool isGen2Cuid = !isGen1a;

        IsWritingUid = true;
        WriteUidResult = $"Writing UID [{cleanHex}] via {(isGen2Cuid ? "Gen 2 CUID direct write" : "Gen 1a magic backdoor")}...";
        HasWriteUidResult = true;
        IsWriteUidSuccess = false;
        OnPropertyChanged(nameof(WriteUidResultBrush));

        try
        {
            var (success, message) = await MifareService.Instance.ChangeUidAsync(cleanHex, isGen2Cuid: isGen2Cuid);
            WriteUidResult = message;
            IsWriteUidSuccess = success;
            OnPropertyChanged(nameof(WriteUidResultBrush));

            if (success)
            {
                CurrentTag.Uid = cleanHex;
                CurrentTag.FormattedUid = string.Join(" ", Enumerable.Range(0, 4).Select(i => cleanHex.Substring(i * 2, 2)));
                OnPropertyChanged(nameof(CurrentTag));

                await TagScanService.Instance.AutoSaveScanAsync(CurrentTag);
            }
        }
        catch (Exception ex)
        {
            WriteUidResult = $"Write error: {ex.Message}";
            IsWriteUidSuccess = false;
            OnPropertyChanged(nameof(WriteUidResultBrush));
        }
        finally
        {
            IsWritingUid = false;
        }
    }

    [RelayCommand]
    public async Task BackupCardAsync()
    {
        if (CurrentTag == null || !CanBackup)
        {
            return;
        }

        IsBackingUp = true;
        HasBackupEntry = false;
        HasBackupStatus = true;
        BackupStatus = "Finding keys and reading the card...";

        try
        {
            var progress = new Progress<string>(stage => BackupStatus = stage);
            var (result, entry) = await MifareService.Instance.BackupCardAsync(CurrentTag, BackupAlias, progress);

            if (!result.Success)
            {
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
                BackupStatus = "The backup finished, but no dump files were found. Check the client dump path.";
                return;
            }

            BackupStatus = $"Saved \"{entry.Alias}\" to your card library.";
            HasBackupEntry = true;
        }
        catch (Exception ex)
        {
            BackupStatus = "Backup error: " + ex.Message;
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    public void OpenLibrary() => RequestNavigation?.Invoke("library");

    [RelayCommand]
    public void OpenDumpsFolder()
    {
        try
        {
            var env = Pm3EnvironmentResolver.Instance;
            if (!env.IsResolved) env.Resolve();
            Directory.CreateDirectory(env.DumpsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{env.DumpsDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open dumps folder: {ex.Message}");
        }
    }

    public async Task LoadHistoryAsync()
    {
        try
        {
            var list = await TagScanService.Instance.LoadScanHistoryAsync();
            ScanHistory.Clear();
            foreach (var item in list)
            {
                ScanHistory.Add(item);
            }
        }
        catch { }
    }

    private async Task LoadHistorySilentlyAsync()
    {
        try
        {
            var list = await TagScanService.Instance.LoadScanHistoryAsync();
            ScanHistory.Clear();
            foreach (var item in list)
            {
                ScanHistory.Add(item);
            }
        }
        catch { }
    }
}
