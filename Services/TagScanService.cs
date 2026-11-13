using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using iceman_gui.Core;
using iceman_gui.Models;

namespace iceman_gui.Services;

public class TagScanService
{
    private static TagScanService? _instance;
    public static TagScanService Instance => _instance ??= new TagScanService();

    public async Task<TagInfo> ScanAsync(bool quick = false, CancellationToken ct = default)
    {
        return await ProgressiveScanAsync(ct);
    }

    /// <summary>
    /// Fast single-step read of the card on the antenna (ISO 14443-A only).
    /// Used by the write wizard before touching a card.
    /// </summary>
    public async Task<TagInfo> ReadCardInfoAsync(CancellationToken ct = default)
    {
        Pm3Result result = await Pm3ProcessService.Instance.ExecuteAsync("hf 14a info", 15_000, ct);
        if (!result.Success)
        {
            throw new Pm3CommandException("hf 14a info", result);
        }

        return ParseTagOutput(result.Output);
    }

    public async Task<TagInfo> ProgressiveScanAsync(CancellationToken ct = default)
    {
        // Timeouts measured against a real device: hf 14a info ~0.3s, hf search
        // ~10.7s, lf search ~8.6s, auto ~19.4s. They cannot be interrupted once
        // started, so every timeout is sized well above the observed worst case.
        try
        {
            var tag14a = await RunTierAsync("hf 14a info", 10_000, ct);
            if (!string.IsNullOrEmpty(tag14a.Uid))
            {
                return tag14a;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Pm3CommandException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return new TagInfo();
        }

        try
        {
            var tagHf = await RunTierAsync("hf search", 45_000, ct);
            if (!string.IsNullOrEmpty(tagHf.Uid))
            {
                return tagHf;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Pm3CommandException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return new TagInfo();
        }

        try
        {
            var tagLf = await RunTierAsync("lf search", 45_000, ct);
            if (!string.IsNullOrEmpty(tagLf.Uid) || !string.IsNullOrEmpty(tagLf.CardNumber))
            {
                return tagLf;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Pm3CommandException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return new TagInfo();
        }

        try
        {
            return await RunTierAsync("auto", 90_000, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Pm3CommandException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return new TagInfo();
        }
    }

    private static async Task<TagInfo> RunTierAsync(string command, int timeoutMs, CancellationToken ct)
    {
        var result = await Pm3ProcessService.Instance.ExecuteAsync(command, timeoutMs, ct);
        ct.ThrowIfCancellationRequested();

        if (!result.Success)
        {
            throw new Pm3CommandException(command, result);
        }

        return Instance.ParseTagOutput(result.Output);
    }

    public TagInfo ParseTagOutput(string rawOutput)
    {
        string clean = AnsiColorParser.StripAnsi(rawOutput);
        var info = new TagInfo
        {
            RawOutput = rawOutput,
            ScanTime = DateTime.Now
        };

        // 1. UID extraction (HF UID or LF TAG ID / IDm)
        var uidMatch = Regex.Match(clean, @"(?:UID|TAG ID|EM 410x ID|CSN|IDm):\s*([0-9A-Fa-f\s]{8,})", RegexOptions.IgnoreCase);
        if (uidMatch.Success)
        {
            string rawDigits = uidMatch.Groups[1].Value.Trim();
            int parenIdx = rawDigits.IndexOf('(');
            if (parenIdx >= 0) rawDigits = rawDigits.Substring(0, parenIdx).Trim();

            string[] bytes = rawDigits.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            info.FormattedUid = string.Join(" ", bytes).ToUpperInvariant();
            info.Uid = string.Join("", bytes).ToUpperInvariant();

            // UID Length classification
            if (bytes.Length == 4) info.UidLength = "4 bytes (Single UID)";
            else if (bytes.Length == 5) info.UidLength = "5 bytes (LF ID)";
            else if (bytes.Length == 7) info.UidLength = "7 bytes (Double UID)";
            else if (bytes.Length == 8) info.UidLength = "8 bytes (FeliCa IDm / 64-bit UID)";
            else if (bytes.Length == 10) info.UidLength = "10 bytes (Triple UID)";
            else info.UidLength = $"{bytes.Length} bytes";
        }

        // 2. ATQA
        var atqaMatch = Regex.Match(clean, @"ATQA:\s*([0-9A-Fa-f\s]{4,})");
        if (atqaMatch.Success)
        {
            info.Atqa = atqaMatch.Groups[1].Value.Trim().ToUpperInvariant();
        }

        // 3. SAK
        var sakMatch = Regex.Match(clean, @"SAK:\s*([0-9A-Fa-f]{2}(?:\s*\[\d+\])?)");
        if (sakMatch.Success)
        {
            info.Sak = sakMatch.Groups[1].Value.Trim().ToUpperInvariant();
        }

        // 4. Standard / Make classification (LF & HF)
        if (clean.Contains("ISO14443-A", StringComparison.OrdinalIgnoreCase) || clean.Contains("ISO 14443-A", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "ISO/IEC 14443-A (Type A)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("ISO14443-B", StringComparison.OrdinalIgnoreCase) || clean.Contains("ISO 14443-B", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "ISO/IEC 14443-B (Type B)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("ISO15693", StringComparison.OrdinalIgnoreCase) || clean.Contains("Vicinity", StringComparison.OrdinalIgnoreCase) || clean.Contains("ICODE", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "ISO/IEC 15693 (Vicinity)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("FeliCa", StringComparison.OrdinalIgnoreCase) || clean.Contains("JIS X 6319", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "FeliCa (JIS X 6319-4)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("EM410x", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "EM Microelectronic EM410x (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("HID Prox", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "HID Prox (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("Indala", StringComparison.OrdinalIgnoreCase) || clean.Contains("MOTOROLA", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "Motorola / Indala (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("AWID", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "AWID (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("IoProx", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "Kantech IoProx (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("T55xx", StringComparison.OrdinalIgnoreCase) || clean.Contains("T5577", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "Atmel T55xx / T5577 (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("Hitag", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "NXP Hitag (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("iCLASS", StringComparison.OrdinalIgnoreCase) || clean.Contains("PicoPass", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "HID iCLASS / PicoPass (HF 13.56 MHz)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("LEGIC", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "LEGIC Prime / Advant (HF 13.56 MHz)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("Topaz", StringComparison.OrdinalIgnoreCase) || clean.Contains("Jewel", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "Innovision Topaz / Jewel (HF 13.56 MHz)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("ISO14443-B", StringComparison.OrdinalIgnoreCase) || clean.Contains("ISO 14443-B", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "ISO/IEC 14443-B (Type B)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("ISO15693", StringComparison.OrdinalIgnoreCase) || clean.Contains("Vicinity", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "ISO/IEC 15693 (Vicinity)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("FeliCa", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "FeliCa (JIS X 6319-4)";
            info.Frequency = "HF (13.56 MHz)";
        }
        else if (clean.Contains("EM410x", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "EM410x (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("HID Prox", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "HID Prox (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("Indala", StringComparison.OrdinalIgnoreCase) || clean.Contains("MOTOROLA", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "Motorola / Indala (LF 125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("iCLASS", StringComparison.OrdinalIgnoreCase) || clean.Contains("PicoPass", StringComparison.OrdinalIgnoreCase))
        {
            info.Standard = "HID iCLASS / PicoPass (HF)";
            info.Frequency = "HF (13.56 MHz)";
        }

        // 5. Manufacturer Detection
        var mfgMatch = Regex.Match(clean, @"(?:\n|\r)\s*\[\+\]\s{3,}([A-Z][A-Za-z0-9\s,\.\-]+(?:Germany|Netherlands|France|Japan|USA|Semiconductor|Technologies|Corp|Inc|PLC)?)");
        if (mfgMatch.Success && !mfgMatch.Groups[1].Value.Contains("Possible", StringComparison.OrdinalIgnoreCase))
        {
            info.Manufacturer = mfgMatch.Groups[1].Value.Trim();
        }
        else if (clean.Contains("NXP Semiconductors", StringComparison.OrdinalIgnoreCase))
        {
            info.Manufacturer = "NXP Semiconductors Germany";
        }
        else if (clean.Contains("Infineon", StringComparison.OrdinalIgnoreCase))
        {
            info.Manufacturer = "Infineon Technologies";
        }
        else if (clean.Contains("STMicroelectronics", StringComparison.OrdinalIgnoreCase))
        {
            info.Manufacturer = "STMicroelectronics";
        }
        else if (clean.Contains("Sony", StringComparison.OrdinalIgnoreCase))
        {
            info.Manufacturer = "Sony Corporation";
        }
        else if (info.FormattedUid.StartsWith("04 "))
        {
            info.Manufacturer = "NXP Semiconductors";
        }
        else if (info.FormattedUid.StartsWith("05 "))
        {
            info.Manufacturer = "Infineon Technologies";
        }
        else if (info.FormattedUid.StartsWith("02 "))
        {
            info.Manufacturer = "STMicroelectronics";
        }

        // 6. Tag Type / Chip Model
        var typeMatch = Regex.Match(clean, @"Possible types:\s*(?:\r?\n\s*(?:\[[+=\-]\]\s*)?)?([^\r\n]+)");
        if (typeMatch.Success)
        {
            string t = typeMatch.Groups[1].Value.Trim();
            if (t.StartsWith("[+]") || t.StartsWith("[=]") || t.StartsWith("[-]"))
            {
                t = t.Substring(3).Trim();
            }
            if (!string.IsNullOrEmpty(t))
            {
                info.TagType = t;
            }
        }
        else if (clean.Contains("MIFARE DESFire EV2", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "MIFARE DESFire EV2";
        }
        else if (clean.Contains("MIFARE DESFire", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "MIFARE DESFire";
        }
        else if (clean.Contains("MIFARE Classic", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "MIFARE Classic 1K";
        }
        else if (clean.Contains("MIFARE Plus", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "MIFARE Plus";
        }
        else if (clean.Contains("MIFARE Ultralight", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "MIFARE Ultralight";
        }
        else if (clean.Contains("NTAG", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "NXP NTAG";
        }
        else if (clean.Contains("EM410x", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "EM410x (125 kHz)";
        }
        else if (clean.Contains("HID Prox", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "HID Prox (125 kHz)";
        }
        else if (!string.IsNullOrEmpty(info.Uid))
        {
            info.TagType = info.Standard;
        }

        // 7. ATS (Answer To Select)
        var atsMatch = Regex.Match(clean, @"ATS:\s*([0-9A-Fa-f\s]+(?:\s*\[\s*[0-9A-Fa-f\s]+\s*\])?)");
        if (atsMatch.Success)
        {
            info.Ats = atsMatch.Groups[1].Value.Trim();
        }

        // ATS Details (TL, FSC, Divisors, FWI, etc.)
        var atsDetailsList = new List<string>();
        if (clean.Contains("FSC = "))
        {
            var fscMatch = Regex.Match(clean, @"FSC\s*=\s*\d+");
            if (fscMatch.Success) atsDetailsList.Add(fscMatch.Value);
        }
        if (clean.Contains("divisors are supported"))
        {
            atsDetailsList.Add("Divisors: [2, 4, 8]");
        }
        if (clean.Contains("CID is supported"))
        {
            atsDetailsList.Add("CID supported");
        }
        if (atsDetailsList.Count > 0)
        {
            info.AtsDetails = string.Join(" • ", atsDetailsList);
        }

        // 8. Historical Bytes
        var histMatch = Regex.Match(clean, @"Historical bytes[^\r\n]*\r?\n\s*\[\+\]\s*([^\r\n]+)");
        if (histMatch.Success)
        {
            info.HistoricalBytes = histMatch.Groups[1].Value.Trim();
        }

        // 9. Magic Capabilities
        var magicMatch = Regex.Match(clean, @"Magic capabilities\.\.\.\s*([^\r\n]+)");
        if (magicMatch.Success)
        {
            info.MagicType = magicMatch.Groups[1].Value.Trim();
        }

        // 10. PRNG detection
        var prngMatch = Regex.Match(clean, @"Prng detection\.\.\.\.\.\s*(\w+)");
        if (prngMatch.Success)
        {
            info.Prng = prngMatch.Groups[1].Value.Trim();
        }

        // 11. LF Facility Code & Card Number
        var fcMatch = Regex.Match(clean, @"(?:FC|Facility Code):\s*(\d+)");
        if (fcMatch.Success) info.FacilityCode = fcMatch.Groups[1].Value.Trim();

        var cnMatch = Regex.Match(clean, @"(?:CN|Card Number):\s*(\d+)");
        if (cnMatch.Success) info.CardNumber = cnMatch.Groups[1].Value.Trim();

        return info;
    }

    public async Task<string> AutoSaveScanAsync(TagInfo info)
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        Directory.CreateDirectory(env.DumpsDirectory);
        string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
        string filePath = Path.Combine(env.DumpsDirectory, $"scans_{dateStr}.json");

        var list = new List<TagInfo>();
        if (File.Exists(filePath))
        {
            try
            {
                string existing = await File.ReadAllTextAsync(filePath);
                var loaded = JsonSerializer.Deserialize<List<TagInfo>>(existing);
                if (loaded != null) list.AddRange(loaded);
            }
            catch { }
        }

        // Add newest scan at top
        list.Insert(0, info);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(list, options);
        await File.WriteAllTextAsync(filePath, json);

        return filePath;
    }

    public async Task<List<TagInfo>> LoadScanHistoryAsync()
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        var result = new List<TagInfo>();
        if (!Directory.Exists(env.DumpsDirectory)) return result;

        var files = Directory.GetFiles(env.DumpsDirectory, "scans_*.json")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                string content = await File.ReadAllTextAsync(file);
                var items = JsonSerializer.Deserialize<List<TagInfo>>(content);
                if (items != null) result.AddRange(items);
            }
            catch { }
        }

        return result.OrderByDescending(t => t.ScanTime).ToList();
    }

    public async Task<string> SaveTagInfoToFileAsync(TagInfo info, string? customPath = null)
    {
        if (customPath == null)
        {
            return await AutoSaveScanAsync(info);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(info, options);
        await File.WriteAllTextAsync(customPath, json);
        return customPath;
    }
}
