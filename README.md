<div align="center">
  <img src="assets/logo_full.jpg" alt="PM3 Iceman GUI Logo" width="720" />

  <br /><br />

  [![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
  [![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-0078D6.svg)](https://microsoft.com/windows)
  [![Proxmark3](https://img.shields.io/badge/Proxmark3-Iceman%2FRRG-success.svg)](https://github.com/RfidResearchGroup/proxmark3)

  <p><strong>A modern, high-performance Windows 11 Fluent UI desktop client designed for the Proxmark3 (Iceman / RfidResearchGroup) distribution.</strong></p>
</div>

`iceman-gui` simplifies everyday RFID and NFC workflows—device connection, hardware diagnostics, antenna tuning, tag identification, firmware flashing, and MIFARE Classic sector editing—while preserving raw command-line speed through an unbuffered one-click external terminal.

---

## Feature Tour

| Feature | Description |
| :--- | :--- |
| **Serial Connection** | Instant Windows registry detection (`HARDWARE\DEVICEMAP\SERIALCOMM`) identifying Proxmark3 USB CDC ports automatically. |
| **Hardware Inspection** | Displays ARM OS version, bootloader build, microcontroller model, and flash memory status. |
| **Antenna Resonance** | Real-time voltage measurement (`hw tune`) for both LF (125 kHz) and HF (13.56 MHz) antennas. |
| **Tag Identification** | Extracts UID, ATQA, SAK, PRNG behavior, and detects magic card capabilities (Gen 1a, Gen 2 CUID). |
| **Dump & Export** | Inspect full 64-block dumps and export card profiles to structured JSON or standard `.eml` files. |
| **Firmware Flasher** | Validates `bootrom.elf` and `fullimage.elf`, manages bootloader unlocking, and provides an emergency recovery guide. |

# Installation

1. Download the latest `iceman-gui-windows-x64.zip` from [Releases](https://github.com/your-username/iceman-gui/releases).
2. Extract the archive.
3. Place `iceman-gui.exe` (and its DLLs) into your Proxmark3 root directory (the folder containing `pm3.bat` and `client/`), or anywhere on your system.
4. Run `iceman-gui.exe`.

---

## License & Attribution

- **`iceman-gui`** is released under the **[GNU General Public License v3.0 (GPL-3.0)](LICENSE)**.
- Proxmark3 client binaries and firmware are copyright [RfidResearchGroup](https://github.com/RfidResearchGroup/proxmark3) and contributors under GPL-3.0.
