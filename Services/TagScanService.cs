using System;
using System.IO;
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
        var proc = Pm3ProcessService.Instance;

        // In quick mode, try 'hf 14a reader' first (completes in ~1s)
        string output;
        if (quick)
        {
            output = await proc.ExecuteCommandAsync("hf 14a reader", ct, 10000);
            if (!output.Contains("UID:", StringComparison.OrdinalIgnoreCase))
            {
                // Fall back to thorough search
                output = await proc.ExecuteCommandAsync("hf search; lf search", ct, 30000);
            }
        }
        else
        {
            // Thorough search
            output = await proc.ExecuteCommandAsync("hf search; lf search", ct, 35000);
        }

        return ParseTagOutput(output);
    }

    public TagInfo ParseTagOutput(string rawOutput)
    {
        string clean = AnsiColorParser.StripAnsi(rawOutput);
        var info = new TagInfo { RawOutput = rawOutput };

        // 1. UID extraction
        var uidMatch = Regex.Match(clean, @"UID:\s*([0-9A-Fa-f\s]{8,})");
        if (uidMatch.Success)
        {
            string rawDigits = uidMatch.Groups[1].Value.Trim();
            // remove extra annotations like "( ONUID, re-used )"
            int parenIdx = rawDigits.IndexOf('(');
            if (parenIdx >= 0) rawDigits = rawDigits.Substring(0, parenIdx).Trim();

            string[] bytes = rawDigits.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            info.FormattedUid = string.Join(" ", bytes).ToUpperInvariant();
            info.Uid = string.Join("", bytes).ToUpperInvariant();
        }

        // 2. ATQA
        var atqaMatch = Regex.Match(clean, @"ATQA:\s*([0-9A-Fa-f\s]{4,})");
        if (atqaMatch.Success)
        {
            info.Atqa = atqaMatch.Groups[1].Value.Trim().ToUpperInvariant();
        }

        // 3. SAK
        var sakMatch = Regex.Match(clean, @"SAK:\s*([0-9A-Fa-f]{2})");
        if (sakMatch.Success)
        {
            info.Sak = sakMatch.Groups[1].Value.Trim().ToUpperInvariant();
        }

        // 4. Possible types / Tag Type
        var typeMatch = Regex.Match(clean, @"(?:Possible types:\s*|Valid\s+)([^\r\n]+)");
        if (typeMatch.Success)
        {
            info.TagType = typeMatch.Groups[1].Value.Trim();
        }
        else if (clean.Contains("MIFARE Classic", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "MIFARE Classic 1K";
        }
        else if (clean.Contains("EM410x", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "EM410x (125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }
        else if (clean.Contains("HID Prox", StringComparison.OrdinalIgnoreCase))
        {
            info.TagType = "HID Prox (125 kHz)";
            info.Frequency = "LF (125 kHz)";
        }

        // 5. Magic Capabilities
        var magicMatch = Regex.Match(clean, @"Magic capabilities\.\.\.\s*([^\r\n]+)");
        if (magicMatch.Success)
        {
            info.MagicType = magicMatch.Groups[1].Value.Trim();
        }

        // 6. PRNG detection
        var prngMatch = Regex.Match(clean, @"Prng detection\.\.\.\.\.\s*(\w+)");
        if (prngMatch.Success)
        {
            info.Prng = prngMatch.Groups[1].Value.Trim();
        }

        // 7. LF Facility Code & Card Number (for HID Prox / Indala)
        var fcMatch = Regex.Match(clean, @"(?:FC|Facility Code):\s*(\d+)");
        if (fcMatch.Success)
        {
            info.FacilityCode = fcMatch.Groups[1].Value.Trim();
        }

        var cnMatch = Regex.Match(clean, @"(?:CN|Card Number):\s*(\d+)");
        if (cnMatch.Success)
        {
            info.CardNumber = cnMatch.Groups[1].Value.Trim();
        }

        // If frequency wasn't detected from LF, but we found UID:
        if (!string.IsNullOrEmpty(info.Uid) && info.Frequency.StartsWith("HF") && string.IsNullOrEmpty(info.TagType))
        {
            info.TagType = "ISO14443-A Tag";
        }

        return info;
    }

    public async Task<string> SaveTagInfoToFileAsync(TagInfo info, string? customPath = null)
    {
        var env = Pm3EnvironmentResolver.Instance;
        if (!env.IsResolved) env.Resolve();

        string safeUid = string.IsNullOrEmpty(info.Uid) ? "unknown" : info.Uid;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = customPath ?? Path.Combine(env.DumpsDirectory, $"tag_{safeUid}_{timestamp}.json");

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(info, options);
        await File.WriteAllTextAsync(filePath, json);

        return filePath;
    }
}
