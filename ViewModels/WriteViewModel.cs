using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.ViewModels;

public partial class WriteViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DumpEntry> _sources = new();

    [ObservableProperty]
    private DumpEntry? _selectedSource;

    [ObservableProperty]
    private TagInfo? _targetTag;

    [ObservableProperty]
    private bool _isReadingTarget;

    [ObservableProperty]
    private string _targetStatus = "Connect the device, then read the target card.";

    [ObservableProperty]
    private bool _isCheckingKeys;

    [ObservableProperty]
    private string _keyStatus = "Read the target card first.";

    [ObservableProperty]
    private bool _defaultKeysOk;

    [ObservableProperty]
    private string _targetKeyPath = string.Empty;

    [ObservableProperty]
    private bool _writeUid = true;

    [ObservableProperty]
    private bool _isWriting;

    [ObservableProperty]
    private string _writeStatus = string.Empty;

    [ObservableProperty]
    private string _writeLog = string.Empty;

    [ObservableProperty]
    private bool _hasWriteLog;

    [ObservableProperty]
    private bool _isVerifying;

    [ObservableProperty]
    private bool _hasVerifyResult;

    [ObservableProperty]
    private bool _verifyOk;

    [ObservableProperty]
    private string _verifyStatus = string.Empty;

    [ObservableProperty]
    private bool _writeFailed;

    public ObservableCollection<WizardStep> Steps { get; } = new();

    [ObservableProperty]
    private int _currentStep = 1;

    public WriteViewModel()
    {
        Steps.Add(new WizardStep(1, "Choose backup"));
        Steps.Add(new WizardStep(2, "Read target"));
        Steps.Add(new WizardStep(3, "Keys"));
        Steps.Add(new WizardStep(4, "Write"));
        Steps[^1].IsNotLast = false;
        UpdateSteps();
    }

    public bool IsStep1Visible => CurrentStep == 1;
    public bool IsStep2Visible => CurrentStep == 2;
    public bool IsStep3Visible => CurrentStep == 3;
    public bool IsStep4Visible => CurrentStep == 4;
    public bool ShowContinue => CurrentStep < Steps.Count;
    public bool CanGoBack => CurrentStep > 1;
    public bool KeysReady => TargetTag != null
                             && (TargetCapabilities.Magic == MagicWriteMode.Gen1a
                                 || DefaultKeysOk
                                 || TargetKeyPath.Length > 0);
    public bool CanGoNext => CurrentStep switch
    {
        1 => SelectedSource != null,
        2 => TargetTag != null && TargetCapabilities.IsClassic,
        3 => KeysReady,
        _ => false
    };

    partial void OnCurrentStepChanged(int value) => UpdateSteps();

    public bool HasNoSources => Sources.Count == 0;

    partial void OnSourcesChanged(ObservableCollection<DumpEntry> value) =>
        OnPropertyChanged(nameof(HasNoSources));

    public CardCapabilities TargetCapabilities => CardCapabilities.For(TargetTag);

    public bool CanReadTarget => !IsReadingTarget;
    public bool CanCheckKeys => TargetTag != null
                                && TargetCapabilities.IsClassic
                                && TargetCapabilities.Magic != MagicWriteMode.Gen1a
                                && !IsCheckingKeys;
    public bool CanFindKeys => CanCheckKeys;
    public bool CanWriteUid => TargetTag != null && TargetCapabilities.CanChangeUid;
    public bool CanWrite => SelectedSource != null
                            && TargetTag != null
                            && TargetCapabilities.IsClassic
                            && (TargetCapabilities.Magic == MagicWriteMode.Gen1a || DefaultKeysOk || TargetKeyPath.Length > 0)
                            && !IsWriting
                            && !IsVerifying;

    public bool HasTarget => TargetTag != null;
    public bool CardSupported => TargetTag != null && TargetCapabilities.IsClassic;
    public bool CardUnsupported => TargetTag != null && !TargetCapabilities.IsClassic;
    public bool ReadyToWrite => CanWrite;
    public bool ShowCheckIcon => CardSupported && !WriteFailed;
    public bool ShowDismissIcon => CardUnsupported && !WriteFailed;
    public bool ShowErrorIcon => WriteFailed;
    public bool ShowWriteAnother => VerifyOk;
    public bool ShowWriteButton => IsStep4Visible && !VerifyOk;
    public string ReadinessText => VerifyOk ? "Verified"
        : WriteFailed ? "Write failed"
        : ReadyToWrite ? "Ready to write"
        : CardSupported ? "Card supported"
        : "Not supported";

    public Brush VerifyBrush => VerifyOk ? Brushes.LimeGreen : Brushes.LightCoral;

    partial void OnSelectedSourceChanged(DumpEntry? value) => NotifyGateStates();
    partial void OnTargetTagChanged(TagInfo? value) => NotifyGateStates();
    partial void OnIsReadingTargetChanged(bool value) => NotifyGateStates();
    partial void OnIsCheckingKeysChanged(bool value) => NotifyGateStates();
    partial void OnDefaultKeysOkChanged(bool value) => NotifyGateStates();
    partial void OnTargetKeyPathChanged(string value) => NotifyGateStates();
    partial void OnIsWritingChanged(bool value) => NotifyGateStates();
    partial void OnIsVerifyingChanged(bool value) => NotifyGateStates();
    partial void OnVerifyOkChanged(bool value)
    {
        OnPropertyChanged(nameof(VerifyBrush));
        NotifyReadiness();
    }

    partial void OnWriteFailedChanged(bool value) => NotifyReadiness();

    public void RefreshSources(DumpEntry? select = null)
    {
        string? uid = select?.Uid ?? SelectedSource?.Uid;
        Sources = new ObservableCollection<DumpEntry>(DumpLibrary.List());
        SelectedSource = Sources.FirstOrDefault(entry => entry.Uid == uid) ?? Sources.FirstOrDefault();
    }

    public void SelectSource(DumpEntry entry)
    {
        RefreshSources(entry);
    }

    [RelayCommand]
    public void RefreshList() => RefreshSources();

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
    public async Task ReadTargetAsync()
    {
        if (IsReadingTarget)
        {
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Reading target card");
        IsReadingTarget = true;
        TargetStatus = "Reading the target card...";
        DefaultKeysOk = false;
        TargetKeyPath = string.Empty;
        HasVerifyResult = false;
        WriteStatus = string.Empty;
        WriteLog = string.Empty;
        HasWriteLog = false;

        try
        {
            TagInfo tag = await TagScanService.Instance.ReadCardInfoAsync();
            if (string.IsNullOrWhiteSpace(tag.Uid))
            {
                TargetTag = null;
                TargetStatus = "No card was found on the antenna.";
                KeyStatus = "Place the target card on the antenna and read it again.";
                return;
            }

            TargetTag = tag;
            TargetStatus = $"{tag.FormattedUid} · {tag.TagType} · {tag.DisplayMagicType}";

            if (TargetCapabilities.Magic == MagicWriteMode.Gen1a)
            {
                KeyStatus = "Gen 1a card: it is written through the magic backdoor, so no keys are needed.";
                DefaultKeysOk = true;
            }
            else if (!TargetCapabilities.IsClassic)
            {
                KeyStatus = "This card type cannot be written by the wizard.";
            }
            else
            {
                KeyStatus = "Check the target keys before writing.";
            }

            WriteUid = TargetCapabilities.CanChangeUid;
        }
        catch (Exception ex)
        {
            TargetTag = null;
            TargetStatus = "Could not read the target: " + ex.Message;
            KeyStatus = "Read the target card first.";
        }
        finally
        {
            IsReadingTarget = false;
        }
    }

    [RelayCommand]
    public async Task CheckKeysAsync()
    {
        if (TargetTag == null || IsCheckingKeys)
        {
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Checking keys");
        IsCheckingKeys = true;
        DefaultKeysOk = false;
        TargetKeyPath = string.Empty;
        KeyStatus = "Checking the default key on all sectors...";

        try
        {
            KeyCheckResult check = await MifareService.Instance.CheckDefaultKeysAsync();
            DefaultKeysOk = check.AllOpen;
            KeyStatus = check.AllOpen
                ? "All sectors open with the default key FFFFFFFFFFFF."
                : $"{check.LockedSectors.Count} sector(s) do not open with the default key: {string.Join(", ", check.LockedSectors)}.";
        }
        catch (Exception ex)
        {
            KeyStatus = "Key check failed: " + ex.Message;
        }
        finally
        {
            IsCheckingKeys = false;
        }
    }

    [RelayCommand]
    public async Task FindKeysAsync()
    {
        if (TargetTag == null || IsCheckingKeys)
        {
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Finding keys (autopwn)");
        IsCheckingKeys = true;
        KeyStatus = "Finding the target keys (autopwn)...";

        try
        {
            var progress = new Progress<string>(stage => KeyStatus = stage);
            var (result, entry, allKeys) = await MifareService.Instance.FindTargetKeysAsync(TargetTag, progress);

            if (!result.Success || entry == null)
            {
                KeyStatus = "Could not find the target keys: " + (result.Error ?? "no dump was produced.");
                return;
            }

            TargetKeyPath = entry.KeyBinPath;
            DefaultKeysOk = allKeys;
            KeyStatus = allKeys
                ? "Target keys found and saved to the library."
                : "Some target keys could not be recovered; writing may fail on those sectors.";

            RefreshSources(SelectedSource);
        }
        catch (Exception ex)
        {
            KeyStatus = "Finding keys failed: " + ex.Message;
        }
        finally
        {
            IsCheckingKeys = false;
        }
    }

    [RelayCommand]
    public async Task WriteAsync()
    {
        if (SelectedSource == null || TargetTag == null || IsWriting)
        {
            return;
        }

        MifareDumpFile? dump = DumpLibrary.LoadDump(SelectedSource.Uid);
        if (dump == null || !dump.Is1K)
        {
            WriteStatus = "Only a readable MIFARE Classic 1K backup can be written right now.";
            return;
        }

        string sourceBin = SelectedSource.DumpBinPath;
        if (!File.Exists(sourceBin))
        {
            WriteStatus = "The source .bin file is missing next to the backup JSON.";
            return;
        }

        using var activity = Pm3Activity.Instance.Begin("Writing card");
        IsWriting = true;
        WriteFailed = false;
        HasVerifyResult = false;
        VerifyStatus = string.Empty;
        WriteLog = string.Empty;
        HasWriteLog = false;
        WriteStatus = "Writing the card...";

        CardCapabilities capabilities = TargetCapabilities;
        string? keyPath = TargetKeyPath.Length > 0 ? TargetKeyPath : null;
        bool fullClone = capabilities.CanChangeUid && WriteUid;

        try
        {
            Pm3Result writeResult;

            if (capabilities.Magic == MagicWriteMode.Gen1a)
            {
                WriteStatus = "Loading the dump through the Gen 1a backdoor...";
                writeResult = await MifareService.Instance.CloadAsync(sourceBin);
            }
            else if (capabilities.Magic == MagicWriteMode.Gen2)
            {
                WriteStatus = "Restoring the dump to the Gen 2/CUID card...";
                writeResult = await MifareService.Instance.RestoreAsync(
                    sourceBin,
                    DefaultKeysOk && keyPath == null ? null : keyPath,
                    TargetTag.Uid);

                if (writeResult.Success && fullClone)
                {
                    WriteStatus = "Writing the source UID...";
                    string block0 = dump.Blocks.TryGetValue(0, out string? value) ? value : string.Empty;
                    string keyA = keyPath == null ? "FFFFFFFFFFFF" : MifareService.ReadKeyA(keyPath, 0);
                    Pm3Result blockZero = await MifareService.Instance.WriteBlock0Async(block0, keyA);
                    if (!blockZero.Success)
                    {
                        AppendLog("Block 0 write failed: " + blockZero.Error);
                    }
                }
            }
            else
            {
                WriteStatus = "Writing data blocks only (the UID cannot be changed on this card)...";
                var progress = new Progress<(int current, int total)>(p => WriteStatus = $"Writing block {p.current} / 63...");
                writeResult = await MifareService.Instance.WriteDataOnlyAsync(
                    dump,
                    "FFFFFFFFFFFF",
                    keyPath,
                    progress);
            }

            if (!writeResult.Success)
            {
                WriteFailed = true;
                WriteStatus = "Write failed: " + (writeResult.Error ?? writeResult.Failure.ToString());
                AppendLog(writeResult.Output);
                return;
            }

            WriteStatus = "Write finished. Verifying the card...";
            IsVerifying = true;

            using var verifying = Pm3Activity.Instance.Begin("Verifying card");
            WriteVerification verification = await MifareService.Instance.VerifyAsync(
                SelectedSource.DumpJsonPath,
                keyPath,
                checkUid: fullClone);

            VerifyOk = verification.Success;
            WriteFailed = !verification.Success;
            VerifyStatus = verification.Message;
            HasVerifyResult = true;
            WriteStatus = verification.Success ? "Done." : "Write finished, but verification found differences.";
        }
        catch (Exception ex)
        {
            WriteFailed = true;
            WriteStatus = "Write error: " + ex.Message;
        }
        finally
        {
            IsVerifying = false;
            IsWriting = false;
        }
    }

    [RelayCommand]
    public void StartNewWrite()
    {
        CurrentStep = 1;
        TargetTag = null;
        TargetStatus = "Connect the device, then read the target card.";
        DefaultKeysOk = false;
        TargetKeyPath = string.Empty;
        KeyStatus = "Read the target card first.";
        WriteStatus = string.Empty;
        WriteLog = string.Empty;
        HasWriteLog = false;
        HasVerifyResult = false;
        VerifyOk = false;
        VerifyStatus = string.Empty;
        WriteFailed = false;
        UpdateSteps();
    }

    private void AppendLog(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        WriteLog = text;
        HasWriteLog = true;
    }

    private void NotifyGateStates()
    {
        OnPropertyChanged(nameof(TargetCapabilities));
        OnPropertyChanged(nameof(CanReadTarget));
        OnPropertyChanged(nameof(CanCheckKeys));
        OnPropertyChanged(nameof(CanFindKeys));
        OnPropertyChanged(nameof(CanWriteUid));
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

        bool keysReady = KeysReady;
        foreach (WizardStep step in Steps)
        {
            step.IsActive = step.Number == CurrentStep;
            step.IsDone = step.Number < CurrentStep;
            step.CanGo = step.Number switch
            {
                1 => true,
                2 => SelectedSource != null,
                3 => TargetTag != null && TargetCapabilities.IsClassic,
                4 => keysReady,
                _ => false
            };
        }

        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
        OnPropertyChanged(nameof(IsStep4Visible));
        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(KeysReady));
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
