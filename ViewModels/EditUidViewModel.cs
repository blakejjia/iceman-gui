using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

/// <summary>
/// Changes only the UID (block 0) of a magic card. Data sectors and sector
/// trailers are left untouched.
/// </summary>
public partial class EditUidViewModel : ObservableObject
{
    private static readonly Regex UidRegex = new("^[0-9A-Fa-f]{8}$", RegexOptions.Compiled);
    private static readonly Regex KeyRegex = new("^[0-9A-Fa-f]{12}$", RegexOptions.Compiled);

    [ObservableProperty]
    private TagInfo? _targetTag;

    [ObservableProperty]
    private string _targetStatus = "Read a target card first.";

    [ObservableProperty]
    private bool _isReadingTarget;

    [ObservableProperty]
    private string _newUid = string.Empty;

    [ObservableProperty]
    private string _keyA = "FFFFFFFFFFFF";

    [ObservableProperty]
    private bool _isWriting;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _writeSucceeded;

    [ObservableProperty]
    private bool _writeFailed;

    public ObservableCollection<WizardStep> Steps { get; } = new();

    [ObservableProperty]
    private int _currentStep = 1;

    public EditUidViewModel()
    {
        Steps.Add(new WizardStep(1, "Read target"));
        Steps.Add(new WizardStep(2, "Set UID"));
        Steps.Add(new WizardStep(3, "Write"));
        Steps[^1].IsNotLast = false;
        UpdateSteps();
    }

    public bool IsStep1Visible => CurrentStep == 1;
    public bool IsStep2Visible => CurrentStep == 2;
    public bool IsStep3Visible => CurrentStep == 3;
    public bool ShowContinue => CurrentStep < Steps.Count;
    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep switch
    {
        1 => TargetTag != null && TargetCapabilities.CanChangeUid,
        2 => TargetCapabilities.CanChangeUid && IsValidUid && (!IsGen2 || IsValidKeyA),
        _ => false
    };

    partial void OnCurrentStepChanged(int value) => UpdateSteps();

    public CardCapabilities TargetCapabilities => CardCapabilities.For(TargetTag);
    public bool IsGen1a => TargetCapabilities.Magic == MagicWriteMode.Gen1a;
    public bool IsGen2 => TargetCapabilities.Magic == MagicWriteMode.Gen2;
    public bool ShowKeyA => IsGen2;
    public bool IsValidUid => UidRegex.IsMatch(NewUid.Trim());
    public bool IsValidKeyA => KeyRegex.IsMatch(KeyA.Trim());

    public bool CanReadTarget => !IsReadingTarget && !IsWriting;

    public bool CanWrite => TargetTag != null
                            && TargetCapabilities.CanChangeUid
                            && IsValidUid
                            && (!IsGen2 || IsValidKeyA)
                            && !IsWriting
                            && !IsReadingTarget;

    public bool HasTarget => TargetTag != null;
    public bool CardSupported => TargetTag != null && TargetCapabilities.CanChangeUid;
    public bool CardUnsupported => TargetTag != null && !TargetCapabilities.CanChangeUid;
    public bool ReadyToWrite => CanWrite;
    public bool ShowCheckIcon => CardSupported && !WriteFailed;
    public bool ShowDismissIcon => CardUnsupported && !WriteFailed;
    public bool ShowErrorIcon => WriteFailed;
    public bool ShowWriteAnother => WriteSucceeded;
    public bool ShowWriteButton => IsStep3Visible && !WriteSucceeded;
    public string ReadinessText => WriteSucceeded ? "Verified"
        : WriteFailed ? "Write failed"
        : ReadyToWrite ? "Ready to write"
        : CardSupported ? "UID writable"
        : "UID not writable";

    partial void OnTargetTagChanged(TagInfo? value) => NotifyGateStates();
    partial void OnIsReadingTargetChanged(bool value) => NotifyGateStates();
    partial void OnIsWritingChanged(bool value) => NotifyGateStates();
    partial void OnNewUidChanged(string value)
    {
        OnPropertyChanged(nameof(IsValidUid));
        NotifyGateStates();
    }

    partial void OnKeyAChanged(string value)
    {
        OnPropertyChanged(nameof(IsValidKeyA));
        NotifyGateStates();
    }

    partial void OnWriteSucceededChanged(bool value) => NotifyReadiness();

    partial void OnWriteFailedChanged(bool value) => NotifyReadiness();

    [RelayCommand]
    public async Task ReadTargetAsync()
    {
        if (IsReadingTarget || IsWriting)
        {
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Reading target card");
        IsReadingTarget = true;
        TargetStatus = "Reading the target card...";
        HasStatus = false;

        try
        {
            TagInfo tag = await TagScanService.Instance.ReadCardInfoAsync();
            if (string.IsNullOrWhiteSpace(tag.Uid))
            {
                TargetTag = null;
                TargetStatus = "No card was found on the antenna.";
                return;
            }

            TargetTag = tag;
            NewUid = tag.Uid;
            TargetStatus = $"{tag.FormattedUid} · {tag.TagType} · {tag.DisplayMagicType}";
            WriteSucceeded = false;

            Status = TargetCapabilities.CanChangeUid
                ? "Ready. Enter the new 4-byte UID."
                : "This card is not UID-writable (no Gen 1a / Gen 2 magic capability).";
            HasStatus = true;
        }
        catch (Exception ex)
        {
            TargetTag = null;
            TargetStatus = "Could not read the target: " + ex.Message;
        }
        finally
        {
            IsReadingTarget = false;
        }
    }

    [RelayCommand]
    public void GoToStep(WizardStep? step)
    {
        if (step == null)
        {
            return;
        }

        if (step.Number <= CurrentStep || step.CanGo)
        {
            CurrentStep = step.Number;
        }
    }

    [RelayCommand]
    public void NextStep()
    {
        if (CanGoNext && CurrentStep < Steps.Count)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    public void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    public void RandomizeUid()
    {
        byte[] bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        NewUid = Convert.ToHexString(bytes);
    }

    [RelayCommand]
    public async Task WriteUidAsync()
    {
        if (!CanWrite || TargetTag == null)
        {
            return;
        }

        string uid = NewUid.Trim().ToUpperInvariant();
        using var activity = Pm3Activity.Instance.Begin("Writing UID");
        IsWriting = true;
        HasStatus = true;
        WriteSucceeded = false;
        WriteFailed = false;
        Status = IsGen1a
            ? "Writing the UID through the Gen 1a backdoor..."
            : "Writing block 0 (UID + BCC)...";

        try
        {
            var (success, message) = await MifareService.Instance.ChangeUidAsync(
                uid,
                isGen2Cuid: IsGen2,
                keyA: KeyA.Trim().ToUpperInvariant());

            if (!success)
            {
                WriteFailed = true;
                Status = message;
                return;
            }

            Status = message;

            try
            {
                TagInfo verify = await TagScanService.Instance.ReadCardInfoAsync();
                TargetTag = verify;
                if (string.Equals(verify.Uid, uid, StringComparison.OrdinalIgnoreCase))
                {
                    WriteSucceeded = true;
                    Status = $"UID changed to {verify.FormattedUid} and verified.";
                }
                else
                {
                    WriteFailed = true;
                    Status = $"Write reported success, but the card now reads {verify.FormattedUid}.";
                }
            }
            catch (Exception ex)
            {
                WriteFailed = true;
                Status = message + " (Could not re-read the card: " + ex.Message + ")";
            }
        }
        catch (Exception ex)
        {
            WriteFailed = true;
            Status = "UID write error: " + ex.Message;
        }
        finally
        {
            IsWriting = false;
        }
    }

    [RelayCommand]
    public void StartNewUid()
    {
        CurrentStep = 1;
        TargetTag = null;
        TargetStatus = "Read a target card first.";
        NewUid = string.Empty;
        KeyA = "FFFFFFFFFFFF";
        Status = string.Empty;
        HasStatus = false;
        WriteSucceeded = false;
        WriteFailed = false;
        UpdateSteps();
    }

    private void NotifyGateStates()
    {
        OnPropertyChanged(nameof(TargetCapabilities));
        OnPropertyChanged(nameof(IsGen1a));
        OnPropertyChanged(nameof(IsGen2));
        OnPropertyChanged(nameof(ShowKeyA));
        OnPropertyChanged(nameof(CanReadTarget));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(HasTarget));
        OnPropertyChanged(nameof(CardSupported));
        OnPropertyChanged(nameof(CardUnsupported));
        OnPropertyChanged(nameof(ReadyToWrite));
        NotifyReadiness();
        UpdateSteps();
    }

    private void UpdateSteps()
    {
        if (Steps.Count == 0)
        {
            return;
        }

        bool uidWritable = TargetTag != null && TargetCapabilities.CanChangeUid;
        bool formReady = IsValidUid && (!IsGen2 || IsValidKeyA);

        foreach (WizardStep step in Steps)
        {
            step.IsActive = step.Number == CurrentStep;
            step.IsDone = step.Number < CurrentStep;
            step.CanGo = step.Number switch
            {
                1 => true,
                2 => uidWritable,
                3 => uidWritable && formReady,
                _ => false
            };
        }

        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(ShowWriteButton));
        OnPropertyChanged(nameof(ShowWriteAnother));
    }

    private void NotifyReadiness()
    {
        OnPropertyChanged(nameof(ReadinessText));
        OnPropertyChanged(nameof(ShowCheckIcon));
        OnPropertyChanged(nameof(ShowDismissIcon));
        OnPropertyChanged(nameof(ShowErrorIcon));
        OnPropertyChanged(nameof(ShowWriteAnother));
        OnPropertyChanged(nameof(ShowWriteButton));
    }
}
