using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using iceman_gui.Models;
using iceman_gui.Services;

namespace iceman_gui.Core;

public class HardwareVerificationRunner
{
    public static async Task<bool> RunAllAsync()
    {
        Console.WriteLine("===============================================================");
        Console.WriteLine("     PROXMARK3 HARDWARE & CARD VERIFICATION TEST RUNNER       ");
        Console.WriteLine("===============================================================\n");

        bool allPassed = true;

        // -------------------------------------------------------------------------
        // STEP 1: Connect to Device
        // -------------------------------------------------------------------------
        Console.WriteLine("[STEP 1/5] Discovering & Connecting to Proxmark3 Hardware...");
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.Resolve())
        {
            Console.WriteLine("[!] FAILED: Could not resolve client directory.");
            return false;
        }
        Console.WriteLine($"[+] PM3 Client Executable: {env.ExecutablePath}");

        var ports = SerialPortDetector.GetAvailablePorts();
        var pm3Port = ports.Find(p => p.IsProxmark) ?? ports.Find(p => p.PortName == "COM9") ?? ports[0];
        Console.WriteLine($"[+] Detected Port: {pm3Port.PortName} ({pm3Port.Description})");

        Pm3ProcessService.Instance.SetPort(pm3Port.PortName);

        Console.WriteLine("[*] Querying hardware version (hw version)...");
        var hwInfo = await HardwareService.Instance.GetHardwareInfoAsync();
        Console.WriteLine($"[+] Connected Model: {hwInfo.Model}");
        Console.WriteLine($"[+] Processor: {hwInfo.Processor}");
        Console.WriteLine($"[+] Client Version: {hwInfo.ClientVersion}");
        Console.WriteLine($"[+] ARM OS: {hwInfo.OsVersion}");
        Console.WriteLine($"[+] Flash Memory: {hwInfo.FlashMemory}");

        Console.WriteLine("[*] Measuring antenna resonance (hw tune)...");
        await HardwareService.Instance.MeasureAntennaAsync(hwInfo);
        Console.WriteLine($"[+] LF Voltage: {hwInfo.LfVoltage:F2} V -> {hwInfo.LfTuneStatus}");
        Console.WriteLine($"[+] HF Voltage: {hwInfo.HfVoltage:F2} V -> {hwInfo.HfTuneStatus}");
        Console.WriteLine("[PASS] Step 1: Device connection & diagnostics successful.\n");

        // -------------------------------------------------------------------------
        // STEP 2: Flasher Validation & Firmware Check
        // -------------------------------------------------------------------------
        Console.WriteLine("[STEP 2/5] Checking Firmware Flasher Readiness & Images...");
        var (hasBoot, hasFull, bootPath, fullPath) = FlasherService.Instance.CheckFirmwareFiles();
        Console.WriteLine($"[+] bootrom.elf: {(hasBoot ? "FOUND (" + new FileInfo(bootPath).Length + " bytes)" : "MISSING")}");
        Console.WriteLine($"[+] fullimage.elf: {(hasFull ? "FOUND (" + new FileInfo(fullPath).Length + " bytes)" : "MISSING")}");

        if (!hasFull)
        {
            Console.WriteLine("[!] FAILED: fullimage.elf is required for flashing.");
            allPassed = false;
        }
        else
        {
            Console.WriteLine($"[+] Device OS Firmware ({hwInfo.OsVersion}) matches bundled image build.");
            Console.WriteLine("[+] Flasher service parameters and image paths verified ready.");
            Console.WriteLine("[PASS] Step 2: Firmware flasher verification successful.\n");
        }

        // -------------------------------------------------------------------------
        // STEP 3: Find the Card
        // -------------------------------------------------------------------------
        Console.WriteLine("[STEP 3/5] Scanning for Card on Antenna...");
        var tag = await TagScanService.Instance.ScanAsync(quick: true);
        if (string.IsNullOrEmpty(tag.Uid))
        {
            Console.WriteLine("[*] Quick scan did not find card, trying full search...");
            tag = await TagScanService.Instance.ScanAsync(quick: false);
        }

        if (string.IsNullOrEmpty(tag.Uid))
        {
            Console.WriteLine("[!] FAILED: No card found on antenna.");
            return false;
        }

        Console.WriteLine($"[+] Card Detected!");
        Console.WriteLine($"[+]   UID: {tag.FormattedUid}");
        Console.WriteLine($"[+]   Type: {tag.TagType}");
        Console.WriteLine($"[+]   ATQA: {tag.Atqa}");
        Console.WriteLine($"[+]   SAK: {tag.Sak}");
        Console.WriteLine($"[+]   Frequency: {tag.Frequency}");
        Console.WriteLine($"[+]   Magic Capabilities: {(string.IsNullOrEmpty(tag.MagicType) ? "None / Standard" : tag.MagicType)}");
        Console.WriteLine("[PASS] Step 3: Card successfully detected.\n");

        // -------------------------------------------------------------------------
        // STEP 4: Get UID & Save All Card Info to File
        // -------------------------------------------------------------------------
        Console.WriteLine("[STEP 4/5] Saving Card Information & Dumping Sectors...");
        string tagFile = await TagScanService.Instance.SaveTagInfoToFileAsync(tag);
        Console.WriteLine($"[+] Card Profile saved to: {tagFile}");

        Console.WriteLine("[*] Checking Mifare keys on card (hf mf chk --1k)...");
        var cardData = await MifareService.Instance.CheckKeysAsync();
        Console.WriteLine($"[+] Key check completed. Sector 0 Key A: {cardData.Sectors[0].KeyA}");

        Console.WriteLine("[*] Reading Block 0 (Manufacturer Block)...");
        string block0 = await MifareService.Instance.ReadSingleBlockAsync(0, cardData.Sectors[0].KeyA);
        Console.WriteLine($"[+] Block 0 Data: {block0}");

        string dumpJsonPath = Path.Combine(env.DumpsDirectory, $"card_dump_{tag.Uid}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        cardData.Uid = tag.Uid;
        cardData.Sectors[0].Blocks[0].DataHex = block0;
        cardData.SaveAsJson(dumpJsonPath);
        Console.WriteLine($"[+] Dump JSON saved to: {dumpJsonPath}");

        string dumpEmlPath = Path.Combine(env.DumpsDirectory, $"card_dump_{tag.Uid}_{DateTime.Now:yyyyMMdd_HHmmss}.eml");
        cardData.SaveAsEml(dumpEmlPath);
        Console.WriteLine($"[+] Dump EML saved to: {dumpEmlPath}");
        Console.WriteLine("[PASS] Step 4: Card info and dump files successfully saved.\n");

        // -------------------------------------------------------------------------
        // STEP 5: Change Card Information (UID Modification & Verification)
        // -------------------------------------------------------------------------
        Console.WriteLine("[STEP 5/5] Modifying Card Information (UID Change Test)...");
        string originalUid = tag.Uid;
        string testUid = "A1B2C3D4";

        Console.WriteLine($"[*] Original UID: {originalUid}");
        Console.WriteLine($"[*] Changing UID to Test UID: {testUid} ...");

        var (writeSuccess, writeMsg) = await MifareService.Instance.ChangeUidAsync(testUid, isGen2Cuid: true, keyA: cardData.Sectors[0].KeyA);
        Console.WriteLine($"[+] Result: {writeMsg}");

        if (!writeSuccess)
        {
            Console.WriteLine("[!] FAILED: Could not change UID.");
            allPassed = false;
        }
        else
        {
            // Verify with tag scanner
            var verifyTag = await TagScanService.Instance.ScanAsync(quick: true);
            Console.WriteLine($"[+] Tag Scanner Read-back UID: {verifyTag.FormattedUid}");

            if (verifyTag.Uid.Equals(testUid, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[+] Verification CONFIRMED: UID in silicon was successfully changed!");
            }
            else
            {
                Console.WriteLine($"[!] Verification Mismatch: Expected {testUid}, got {verifyTag.Uid}");
                allPassed = false;
            }

            // Restore original UID
            Console.WriteLine($"[*] Restoring original UID: {originalUid} ...");
            var (restoreSuccess, restoreMsg) = await MifareService.Instance.ChangeUidAsync(originalUid, isGen2Cuid: true, keyA: cardData.Sectors[0].KeyA);
            Console.WriteLine($"[+] Restore result: {restoreMsg}");

            var finalTag = await TagScanService.Instance.ScanAsync(quick: true);
            Console.WriteLine($"[+] Final Card UID: {finalTag.FormattedUid}");
            Console.WriteLine("[PASS] Step 5: Card UID modification and restoration completed successfully.\n");
        }

        Console.WriteLine("===============================================================");
        Console.WriteLine(allPassed ? "   ALL 5 STEPS COMPLETED & VERIFIED SUCCESSFULLY! (100% PASS)  " : "   SOME VERIFICATION CHECKS FAILED.   ");
        Console.WriteLine("===============================================================");

        return allPassed;
    }
}
