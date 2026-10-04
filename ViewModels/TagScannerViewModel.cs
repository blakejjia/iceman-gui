using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class TagScannerViewModel : ObservableObject
{
    [ObservableProperty]
    private TagInfo? _currentTag;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "Ready to scan tag. Place card on antenna.";

    [ObservableProperty]
    private string _lastSavedPath = string.Empty;

    public event Action<string>? RequestNavigation;

    [RelayCommand]
    public void GoToMifare() => RequestNavigation?.Invoke("mifare");

    [RelayCommand]
    public async Task QuickScanAsync()
    {
        IsScanning = true;
        StatusMessage = "Quick scanning ISO14443-A card...";

        try
        {
            var tag = await TagScanService.Instance.ScanAsync(quick: true);
            CurrentTag = tag;
            if (!string.IsNullOrEmpty(tag.Uid))
            {
                StatusMessage = $"Tag Found: UID [{tag.FormattedUid}] ({tag.TagType})";
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
    public async Task FullScanAsync()
    {
        IsScanning = true;
        StatusMessage = "Performing full HF & LF search (this may take ~20 seconds)...";

        try
        {
            var tag = await TagScanService.Instance.ScanAsync(quick: false);
            CurrentTag = tag;
            if (!string.IsNullOrEmpty(tag.Uid) || !string.IsNullOrEmpty(tag.CardNumber))
            {
                StatusMessage = $"Tag Found: [{tag.FormattedUid}] Type: {tag.TagType}";
            }
            else
            {
                StatusMessage = "No HF or LF tag detected.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

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
    public async Task SaveTagInfoAsync()
    {
        if (CurrentTag == null || string.IsNullOrEmpty(CurrentTag.Uid))
        {
            StatusMessage = "No tag information to save.";
            return;
        }

        try
        {
            string path = await TagScanService.Instance.SaveTagInfoToFileAsync(CurrentTag);
            LastSavedPath = path;
            StatusMessage = $"Saved tag details to: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }
}
