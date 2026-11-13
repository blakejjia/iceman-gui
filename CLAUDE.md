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

**Entry and navigation.** `App.xaml.cs` creates `MainWindow`; `MainWindow.xaml` instantiates all three pages at once (`DashboardPage`, `TagScannerPage`, `MifareToolkitPage`) and toggles them with `Visibility` bindings on `MainViewModel.IsDashboardActive` / `IsScannerActive` / `IsMifareActive`, derived from `SelectedPageTag`. Do not add WPF `Frame`/`NavigationService` navigation. In-page jumps use the `RequestNavigation` event raised by `TagScannerViewModel` ("mifare") and `MifareToolkitViewModel` ("scanner"). The nav rail only lists Dashboard and Tag Scanner — the Mifare page is reachable only from the scanner.

`MainViewModel` owns the three child view models, mirrors the dashboard's connection state and port into the status bar, seeds `MifareToolkit.CardData.Uid` when a MIFARE card is scanned, and aggregates every child's busy flag into `IsAppBusy` for the global progress bar.

**MVVM convention.** CommunityToolkit.Mvvm source generators throughout: `[ObservableProperty]` on fields (partial `OnXChanged` hooks), `[RelayCommand]` on methods (generated `XCommand` properties), `[NotifyPropertyChangedFor]` for computed props. Commands are `Task`-returning, never `async void`. Pages have empty code-behind; `MainWindow.xaml.cs` is the only code-behind with logic (nav sync + terminal launch).

**Bundled client resolution.** `Core/Pm3EnvironmentResolver` searches `<app dir>/client`, then every ancestor of the app directory, then the CWD, for a folder containing `proxmark3.exe`. This is why `dotnet run` works without setup, and why publishing must keep `client/` next to the exe. Always go through `Pm3EnvironmentResolver.Instance` (`ClientDirectory`, `ExecutablePath`, `LibsDirectory`, `DumpsDirectory`, `RootDirectory`) instead of hardcoding paths. `dumps/` is created at runtime beside the resolved root; scan history is `dumps/scans_YYYY-MM-DD.json`.

**Process control (`Core/Pm3ProcessService`).** A singleton that serializes every command through a `SemaphoreSlim`, because the Proxmark3 serial link cannot have two commands in flight. Primary path: one long-lived `proxmark3.exe <port> -w -f` session. Each command writes the command plus a `rem END_<guid>` sentinel to stdin, and the stdout reader completes the pending `TaskCompletionSource` when it sees `remark: END_<guid>`; output lines in between become the command's return value. Cancellation writes a blank newline (PM3 aborts its reader/search on Enter). If no session exists but a port is set, `ExecuteCommandAsync` starts one; otherwise it falls back to a one-shot `proxmark3.exe <port> -f -w -c "<command>"`. Connection success is inferred from specific client lines (`pm3 -->`, `Communicating with PM3 over`, `remark: READY`), failure from `Can't open serial port` / `OFFLINE mode`.

`CreateBaseStartInfo` sets the environment the Qt/MinGW client needs: `PATH` prepended with `client/libs` and `client/libs/shell`, `QT_PLUGIN_PATH` / `QT_QPA_PLATFORM_PLUGIN_PATH` with a trailing slash, `HOME` = client dir, `MSYSTEM=MINGW64`. `Core/ChildProcessTracker` puts every spawned process in a Windows job object with kill-on-close so no orphan `proxmark3.exe` survives the GUI.

**Output parsing.** `Core/AnsiColorParser.StripAnsi` runs before all regexes; `ParseToRuns` is only for rendering ANSI in the UI. Parsers (`TagScanService.ParseTagOutput`, `HardwareService`, `MifareService`) are tightly coupled to the client's printable output format, so when editing one, compare against real captured output (local `dumps/` and `client/.proxmark3/logs/*.txt` are useful samples) and keep patterns tolerant of both `[+]`/`[=]`/`[-]` prefixes and column-table formatting.

Notable behaviors:
- `TagScanService.ProgressiveScanAsync` tries `hf 14a info` (5 s) → `hf search` (8 s) → `lf search` (10 s) → `auto` (25 s), returning the first tier that yields a UID or LF card number. The `quick` flag on `ScanAsync` is currently ignored.
- `MifareService` assumes a MIFARE Classic 1K layout (16 sectors × 4 blocks, 32 hex chars per block). Key recovery is `hf mf chk --1k`, reads are `hf mf rdbl`, writes are `hf mf wrbl` (`--force` only for block 0). Gen 2/CUID writes recompute the BCC byte and rewrite block 0 from a read-modify-write; Gen 1a uses `hf mf csetuid`.
- Flashing stops the interactive session first to release the COM port, then runs `proxmark3.exe <port> --flash --unlock-bootloader --image bootrom.elf --image fullimage.elf`; the user reconnects afterwards.
- Port discovery reads `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM` and marks USB/ICEMAN/PROXMARK descriptions as Proxmark; falls back to `COM9` when the registry yields nothing.

## Bundled binaries

`client/` is a full Iceman distribution (exe, Qt/MinGW DLLs, dictionaries, lualibs, cmdscripts, `bootrom.elf`, `fullimage.elf`); `recovery/` holds recovery bins. The csproj copies `assets/`, `client/`, `recovery/` and `*.bat` to the output and fails the `Publish` target if `client\proxmark3.exe` or `client\libs\Qt6Core.dll` is missing — `package.ps1` and the workflow repeat that check. Keep those three places in sync when changing packaging. `client/.proxmark3/` logs/history/preferences and `dumps/*.json|*.eml|*.bin` are gitignored.

## UI

Dark theme only (`ui:ThemesDictionary Theme="Dark"` in `App.xaml`), Mica window backdrop, `FluentEntranceStyle` applied to each page container for the fade/slide transition. Use `DynamicResource` theme brushes for text/surfaces; the views intentionally use fixed hex accents for status colors (e.g. `#4ADE80` green for valid/modified, `#F87171` red for illegal, cyan for untouched).

Unhandled dispatcher exceptions are written to `crash.log` in the working directory and shown in a MessageBox (`App.xaml.cs`).
