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
    private ObservableCollection<KeyFileEntry> _keyFiles = new();

    [ObservableProperty]
    private KeyFileEntry? _selectedKeyFile;

    partial void OnSelectedKeyFileChanged(KeyFileEntry? value)
    {
        if (value == null)
        {
            return;
        }

        TargetKeyPath = value.Path;
        DefaultKeysOk = false;
        KeyStatus = $"Using key file {value.Name}.";
        NotifyGateStates();
    }

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

    public Brush VerifyBrush => VerifyOk ? Brushes.LimeGreen : Brushes.LightCoral;

    partial void OnSelectedSourceChanged(DumpEntry? value) => NotifyGateStates();
    partial void OnTargetTagChanged(TagInfo? value) => NotifyGateStates();
    partial void OnIsReadingTargetChanged(bool value) => NotifyGateStates();
    partial void OnIsCheckingKeysChanged(bool value) => NotifyGateStates();
    partial void OnDefaultKeysOkChanged(bool value) => NotifyGateStates();
    partial void OnTargetKeyPathChanged(string value) => NotifyGateStates();
    partial void OnIsWritingChanged(bool value) => NotifyGateStates();
    partial void OnIsVerifyingChanged(bool value) => NotifyGateStates();
    partial void OnVerifyOkChanged(bool value) => OnPropertyChanged(nameof(VerifyBrush));

    public void RefreshSources(DumpEntry? select = null)
    {
        string? uid = select?.Uid ?? SelectedSource?.Uid;
        Sources = new ObservableCollection<DumpEntry>(DumpLibrary.List());
        SelectedSource = Sources.FirstOrDefault(entry => entry.Uid == uid) ?? Sources.FirstOrDefault();
    }

    public void RefreshKeyFiles()
    {
        string? path = SelectedKeyFile?.Path;
        KeyFiles = new ObservableCollection<KeyFileEntry>(
            KeyLibrary.List().Where(entry => entry.Kind == KeyFileKind.Bin));
        SelectedKeyFile = KeyFiles.FirstOrDefault(entry => entry.Path == path);
    }

    public void SelectSource(DumpEntry entry)
    {
        RefreshSources(entry);
    }

    [RelayCommand]
    public void RefreshList() => RefreshSources();

    [RelayCommand]
    public async Task ReadTargetAsync()
    {
        if (IsReadingTarget)
        {
            return;
        }

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

            if (SelectedKeyFile != null && TargetCapabilities.Magic != MagicWriteMode.Gen1a)
            {
                TargetKeyPath = SelectedKeyFile.Path;
                DefaultKeysOk = false;
                KeyStatus = $"Using key file {SelectedKeyFile.Name}.";
            }
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

        IsCheckingKeys = true;
        DefaultKeysOk = false;
        TargetKeyPath = string.Empty;
        SelectedKeyFile = null;
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

        IsCheckingKeys = true;
        SelectedKeyFile = null;
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

        IsWriting = true;
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
                WriteStatus = "Write failed: " + (writeResult.Error ?? writeResult.Failure.ToString());
                AppendLog(writeResult.Output);
                return;
            }

            WriteStatus = "Write finished. Verifying the card...";
            IsVerifying = true;

            WriteVerification verification = await MifareService.Instance.VerifyAsync(
                SelectedSource.DumpJsonPath,
                keyPath,
                checkUid: fullClone);

            VerifyOk = verification.Success;
            VerifyStatus = verification.Message;
            HasVerifyResult = true;
            WriteStatus = verification.Success ? "Done." : "Write finished, but verification found differences.";
        }
        catch (Exception ex)
        {
            WriteStatus = "Write error: " + ex.Message;
        }
        finally
        {
            IsVerifying = false;
            IsWriting = false;
        }
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
    }
}
