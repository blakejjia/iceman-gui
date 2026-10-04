using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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

    public bool IsMifareCardDetected => CurrentTag != null && CurrentTag.IsMifare;
    public bool HasCurrentTag => CurrentTag != null && (!string.IsNullOrEmpty(CurrentTag.Uid) || !string.IsNullOrEmpty(CurrentTag.CardNumber));

    partial void OnCurrentTagChanged(TagInfo? value)
    {
        OnPropertyChanged(nameof(IsMifareCardDetected));
        OnPropertyChanged(nameof(HasCurrentTag));
    }

    [ObservableProperty]
    private bool _isAutoDetectEnabled = true;

    private CancellationTokenSource? _autoDetectCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "Ready to scan tag. Place card on antenna.";

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
        StartAutoDetectLoop();
    }

    private void StartAutoDetectLoop()
    {
        _autoDetectCts?.Cancel();
        _autoDetectCts = new CancellationTokenSource();
        var token = _autoDetectCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1200, token);

                    // Skip auto-detect probing if:
                    // 1. Feature switch is toggled off
                    // 2. Currently performing a scan
                    // 3. A card is already detected and displayed (Rule: "on there is already a card, no more auto detect anymore")
                    if (!IsAutoDetectEnabled || IsScanning || HasCurrentTag)
                    {
                        continue;
                    }

                    // Check if COM port is active
                    if (!Pm3ProcessService.Instance.IsConnected)
                    {
                        continue;
                    }

                    // Perform lightweight presence check (~200ms)
                    bool detected = await TagScanService.Instance.FastProbePresenceAsync(token);
                    if (detected && !token.IsCancellationRequested && !HasCurrentTag && !IsScanning)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            if (!HasCurrentTag && !IsScanning)
                            {
                                await ScanCardAsync();
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(2000, token); } catch { break; }
                }
            }
        }, token);
    }

    [RelayCommand]
    public void GoToMifare() => RequestNavigation?.Invoke("mifare");

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
        StatusMessage = $"Loaded profile from history: [{item.FormattedUid}] ({item.TagType})";
    }

    [RelayCommand]
    public async Task ScanCardAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusMessage = "Progressive scan in progress (Universal auto sweep across LF & HF)...";

        try
        {
            var tag = await TagScanService.Instance.ProgressiveScanAsync();
            CurrentTag = tag;
            if (!string.IsNullOrEmpty(tag.Uid) || !string.IsNullOrEmpty(tag.CardNumber))
            {
                var path = await TagScanService.Instance.AutoSaveScanAsync(tag);
                LastSavedPath = path;
                ScanHistory.Insert(0, tag);
                StatusMessage = $"Tag Found & Auto-saved: [{tag.FormattedUid}] ({tag.TagType})";
            }
            else
            {
                StatusMessage = "No tag detected. Ensure card is centered on antenna.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
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
        StatusMessage = IsAutoDetectEnabled
            ? "Card cleared. Auto-detect active — place a card on the antenna."
            : "Card cleared. Ready to scan.";
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
            StatusMessage = $"Copied UID [{CurrentTag.Uid}] to clipboard!";
        }
    }

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
            StatusMessage = $"Could not open dumps folder: {ex.Message}";
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
