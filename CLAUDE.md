# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`iceman-gui` is a WPF (.NET 10, Windows-only) desktop front-end for the Proxmark3 Iceman/RRG command-line client. It implements no RFID protocol logic itself: it launches the bundled `client/proxmark3.exe`, drives it either through a persistent interactive session or one-shot invocations, and parses the client's human-readable console output (after stripping ANSI codes) into typed models with regexes.

## Commands

```bash
dotnet run                    # needs Windows + .NET 10 SDK; resolver finds client/ under bin/Debug
dotnet build iceman-gui.csproj -c Release
```

```powershell
./package.ps1                 # self-contained win-x64 publish -> dist/iceman-gui-windows-win-x64.zip + SHA256SUMS.txt
```

There is no unit-test project and no lint configuration. `dotnet build` is the only offline check; CI (`.github/workflows/`) only runs on `v*` tags or manual dispatch and just builds + packages.

Hardware-in-the-loop verification: `iceman-gui.exe --test` (handled in `App.xaml.cs` before any window opens, implemented in `Core/HardwareVerificationRunner.cs`). It requires a physical Proxmark3 on a detected port and a magic card, runs five steps (connect/diagnostics, firmware images, scan, dump + save JSON/EML to `dumps/`, UID change + restore), and returns exit code 0/1. Step 5 temporarily rewrites the card's UID, so only run it with a tag you own.

## Architecture

**Layering:** `ViewModels` → singleton `Services` → `Core/Pm3ProcessService` → `proxmark3.exe`. `Models` are plain data objects (plus `MifareBlock`, an observable object with live hex validation).

**Entry and navigation.** `App.xaml.cs` creates `MainWindow`; `MainWindow.xaml` instantiates all six pages at once (`DashboardPage`, `TagScannerPage` shown as "Read Card", `WritePage`, `LibraryPage`, `KeysPage`, `MifareToolkitPage`) and toggles them with `Visibility` bindings on `MainViewModel.IsDashboardActive` / `IsScannerActive` / `IsWriteActive` / `IsLibraryActive` / `IsKeysActive` / `IsMifareActive`, derived from `SelectedPageTag`. Do not add WPF `Frame`/`NavigationService` navigation. In-page jumps use the `RequestNavigation` event raised by `TagScannerViewModel` ("library", "mifare") and `MifareToolkitViewModel` ("scanner"), plus `LibraryViewModel.OpenCardRequested` / `WriteToCardRequested`. The nav rail lists Device, Read Card, Write Card, Library and Keys. All are always visible; Read Card and Write Card are greyed out (`IsEnabled` bound to `IsConnected`) until a device is connected, while Library and Keys work offline. The Mifare page is reachable only from a library entry.

**Read and Library.** `TagScannerViewModel` exposes the scan result plus `Back up card`: it is enabled only when the scanned card supports it (`CardCapabilities.CanBackup`), calls `MifareService.BackupCardAsync` and shows autopwn progress in `BackupStatus`; the alias is prefilled from the existing library entry when the UID is already known. The Read page never edits the card. `LibraryViewModel` lists `DumpLibrary` entries, renames aliases, imports arbitrary dump files, opens the dump folder/JSON, and raises `OpenBackupRequested` so `MainViewModel` can load the backup into the editor.

**Backup editor.** `MifareToolkitViewModel.LoadBackup` loads a `DumpEntry`; `SaveBlock`/`SaveBackup` rewrite the PM3 JSON **and** BIN in the library folder, never the card. The page is reachable only from a library entry; `Back` returns to the page that opened it (`MainViewModel._pageBeforeMifare`). Card operations (checking keys, dumping, writing) live in the Read/Write wizard, not here.

**Write wizard.** `WriteViewModel` is a linear stepper: choose a `DumpLibrary` entry → read the target (`TagScanService.ReadCardInfoAsync`) → check the default keys (`MifareService.CheckDefaultKeysAsync`; if locked, `FindTargetKeysAsync` runs autopwn on the target and keeps its backup, or pick a saved `.bin` key file from `KeyLibrary`) → write → verify. Gen 2/CUID uses `hf mf restore` (plus an explicit block-0 write when the UID should be cloned), Gen 1a uses `hf mf cload`, and non-magic Classic cards get data blocks only (block 0 and trailers untouched). `hf mf restore`/`dump`/`cload` resolve `-f`/`-k` against the client dump folder, so `MifareService.StageFile` copies the library files into the dump root under a unique name and cleans them up afterwards. `hf mf dump` always loads a key file, so `VerifyAsync` synthesizes an all-FF key file when the target uses default keys; verification re-dumps the card and compares all blocks (including block 0 only when the UID was cloned).

**Keys.** `KeyLibrary` manages files in `%APPDATA%\iceman-gui\keys`; `MifareKeyFile` reads/writes 1K sector-key files (192 bytes: 16 sectors × KeyA/KeyB) and `.dic` dictionaries (one 12-hex key per line). `KeysViewModel`/`KeysPage` create, import, edit, validate and delete them; the write wizard can use any `.bin` key file for target authentication.

`MainViewModel` owns the three child view models, mirrors the dashboard's connection state and port into the status bar, seeds `MifareToolkit.CardData.Uid` when a MIFARE card is scanned, and aggregates every child's busy flag into `IsAppBusy` for the global progress bar.

**MVVM convention.** CommunityToolkit.Mvvm source generators throughout: `[ObservableProperty]` on fields (partial `OnXChanged` hooks), `[RelayCommand]` on methods (generated `XCommand` properties), `[NotifyPropertyChangedFor]` for computed props. Commands are `Task`-returning, never `async void`. Pages have empty code-behind; `MainWindow.xaml.cs` is the only code-behind with logic (nav sync + terminal launch).

**Bundled client resolution.** `Core/Pm3EnvironmentResolver` searches `<app dir>/client`, then every ancestor of the app directory, then the CWD, for a folder containing `proxmark3.exe`. This is why `dotnet run` works without setup, and why publishing must keep `client/` next to the exe. Always go through `Pm3EnvironmentResolver.Instance` (`ClientDirectory`, `ExecutablePath`, `LibsDirectory`, `RootDirectory`) instead of hardcoding paths.

**User data.** Nothing is written beside the executable (it may live under `Program Files`). `Core/AppPaths` owns the layout:

- `%APPDATA%\iceman-gui\` — settings, `library.json`, `keys/`, and `client-home/` (the Proxmark3 client's `HOME`: history, logs, preferences).
- `%USERPROFILE%\Documents\iceman-gui\dumps\` — `scans_YYYY-MM-DD.json` plus one folder per backed-up card: `<UID>/hf-mf-<UID>-dump.json|.bin|-key.bin` and `meta.json` (alias, card type, magic, createdAt).

`Core/ClientHome` seeds `client-home/.proxmark3/preferences.json` from the bundled template and rewrites `file.default.savepath|dumppath|tracepath` to the Documents dumps folder on every launch. This is required: the bundled preferences contain the packager's absolute paths, which make `hf mf autopwn`/`hf mf dump` fail with "can't save the file". `Services/DumpLibrary` lists, imports, aliases and loads those dumps; `Models/MifareDumpFile` reads/writes the PM3 `mfc v2` JSON (`Card`, `blocks`, `SectorKeys`) and maps it to `MifareCardData`; `Models/CardCapabilities` decides what the scanned card supports (MIFARE Classic only for backup; Gen1a vs Gen2 UID write).

**Process control (`Core/Pm3ProcessService`).** The client is exposed to the rest of the app as a command API: `ConnectAsync(port)`, `DisconnectAsync()`, `ExecuteAsync(command, timeoutMs)` returning `Pm3Result { Success, Output, Failure }`, and `RunExclusiveAsync(...)` for work that needs the device to itself (flashing). One long-lived `proxmark3.exe <port> -w -f` session; a `SemaphoreSlim` gate serializes every operation.

Completion framing: while idle the client prints no prompt, and while a command runs it does not read stdin at all (a newline cannot cancel it). Every command is therefore written as `command\nrem END_<guid>\n`; the `remark: END_<guid>` line closes the response, and the echoed `[usb|script] pm3 --> ...` prompt lines and `\r` progress frames are filtered out. On timeout the command is abandoned and the session is killed (`Failure = TimedOut`); the next command reconnects on the same port. Killing the client mid-command can make the USB CDC port disappear (`invalid serial port COMx`) until the device is unplugged and replugged, so timeouts must stay well above the slowest command. An explicit `DisconnectAsync` clears the port so later commands return `NotConnected` instead of silently reconnecting.

`CreateBaseStartInfo` sets the environment the Qt/MinGW client needs: `PATH` prepended with `client/libs` and `client/libs/shell`, `QT_PLUGIN_PATH` / `QT_QPA_PLATFORM_PLUGIN_PATH` with a trailing slash, `HOME` = `%APPDATA%\iceman-gui\client-home` (so the client never writes into the install folder), `MSYSTEM=MINGW64`. `Core/ChildProcessTracker` puts every spawned process in a Windows job object with kill-on-close so no orphan `proxmark3.exe` survives the GUI.

**Output parsing.** `Core/AnsiColorParser.StripAnsi` runs before all regexes; `ParseToRuns` is only for rendering ANSI in the UI. Parsers (`TagScanService.ParseTagOutput`, `HardwareService`, `MifareService`) are tightly coupled to the client's printable output format, so when editing one, compare against real captured output (local `dumps/` and `client/.proxmark3/logs/*.txt` are useful samples) and keep patterns tolerant of both `[+]`/`[=]`/`[-]` prefixes and column-table formatting.

Notable behaviors:
- `TagScanService.ProgressiveScanAsync` tries `hf 14a info` (10 s) → `hf search` (45 s) → `lf search` (45 s) → `auto` (90 s), returning the first tier that yields a UID or LF card number. Measured on a real device: 0.3 s / 10.7 s / 8.6 s / 19.4 s. The `quick` flag on `ScanAsync` is currently ignored.
- `MifareService` assumes a MIFARE Classic 1K layout (16 sectors × 4 blocks, 32 hex chars per block). Key recovery is `hf mf chk --1k` (120 s timeout), reads are `hf mf rdbl` (15 s), writes are `hf mf wrbl` (30 s, `--force` only for block 0). Gen 2/CUID writes recompute the BCC byte and rewrite block 0 from a read-modify-write; Gen 1a uses `hf mf csetuid`. On a non-Classic 14443-A card (e.g. DESFire) `hf mf chk` can run for minutes; guard the toolkit by card type before running it.
- `MifareService.AutopwnAsync` runs `hf mf autopwn --1k|--2k|--4k|--mini` (600 s timeout) and `BackupCardAsync` moves the produced `hf-mf-<UID>-dump.json|.bin|-key.bin` into `Documents\iceman-gui\dumps\<UID>\`. Default keys finish in ~2 s; unknown keys can take minutes and report no percentage. Only MIFARE Classic is supported by autopwn.
- Flashing goes through `Pm3ProcessService.RunExclusiveAsync`, which stops the session, runs `proxmark3.exe <port> --flash --unlock-bootloader --image bootrom.elf --image fullimage.elf` while holding the gate, and leaves the device to be reconnected afterwards.
- Port discovery reads `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM` and marks USB/ICEMAN/PROXMARK descriptions as Proxmark; falls back to `COM9` when the registry yields nothing.

## Bundled binaries

`client/` is a full Iceman distribution (exe, Qt/MinGW DLLs, dictionaries, lualibs, cmdscripts, `bootrom.elf`, `fullimage.elf`); `recovery/` holds recovery bins. The csproj copies `assets/`, `client/`, `recovery/` and `*.bat` to the output and fails the `Publish` target if `client\proxmark3.exe` or `client\libs\Qt6Core.dll` is missing — `package.ps1` and the workflow repeat that check. Keep those three places in sync when changing packaging. `client/.proxmark3/` logs/history/preferences are gitignored; app-created dumps live outside the repo (see User data).

## Upstream reference docs

`docs/proxmark3/` contains the upstream Iceman client documentation (`commands.md`, `commands.json`, `cheatsheet.md`, MIFARE/magic-card notes, standalone mode) at the commit recorded in `docs/proxmark3/ATTRIBUTION.md`. Consult it for command syntax and expected console output before touching a parser. It tracks upstream `main`, while the bundled client is a snapshot — captured client logs in `client/.proxmark3/logs/` and `dumps/` remain the ground truth for what this build actually prints.

## UI

Dark theme only (`ui:ThemesDictionary Theme="Dark"` in `App.xaml`), Mica window backdrop, `FluentEntranceStyle` applied to each page container for the fade/slide transition. Use `DynamicResource` theme brushes for text/surfaces; the views intentionally use fixed hex accents for status colors (e.g. `#4ADE80` green for valid/modified, `#F87171` red for illegal, cyan for untouched).

Unhandled dispatcher exceptions are written to `crash.log` in the working directory and shown in a MessageBox (`App.xaml.cs`).
