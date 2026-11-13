using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using iceman_gui.Core;
using iceman_gui.Models;

namespace iceman_gui.Services;

public class MifareService
{
    private static MifareService? _instance;
    public static MifareService Instance => _instance ??= new MifareService();

    /// <summary>
    /// Checks default keys on card and fills Key A / Key B for all 16 sectors.
    /// </summary>
    public async Task<MifareCardData> CheckKeysAsync(CancellationToken ct = default)
    {
        var result = await Pm3ProcessService.Instance.ExecuteAsync("hf mf chk --1k", 120_000, ct);
        if (!result.Success)
        {
            throw new Pm3CommandException("hf mf chk --1k", result);
        }

        string clean = AnsiColorParser.StripAnsi(result.Output);

        var card = MifareCardData.CreateEmpty1K();

        // Match table rows: [+]  000 | 003 | FFFFFFFFFFFF | 1 | FFFFFFFFFFFF | 1
        var matches = Regex.Matches(clean, @"\[\+\]\s+(\d{3})\s+\|\s+\d{3}\s+\|\s+([0-9A-Fa-f]{12})\s+\|\s+(\d)\s+\|\s+([0-9A-Fa-f]{12})\s+\|\s+(\d)");
        foreach (Match m in matches)
        {
            int sectorNum = int.Parse(m.Groups[1].Value);
            string keyA = m.Groups[2].Value.ToUpperInvariant();
            int resA = int.Parse(m.Groups[3].Value);
            string keyB = m.Groups[4].Value.ToUpperInvariant();
            int resB = int.Parse(m.Groups[5].Value);

            if (sectorNum < card.Sectors.Count)
            {
                if (resA == 1) card.Sectors[sectorNum].KeyA = keyA;
                if (resB == 1) card.Sectors[sectorNum].KeyB = keyB;
            }
        }

        return card;
    }

    /// <summary>
    /// Reads a single block.
    /// </summary>
    public async Task<string> ReadSingleBlockAsync(int blockNum, string keyA, CancellationToken ct = default)
    {
        string cmd = $"hf mf rdbl --blk {blockNum} -k {keyA}";
        var result = await Pm3ProcessService.Instance.ExecuteAsync(cmd, 15_000, ct);
        if (!result.Success)
        {
            return string.Empty;
        }

        string clean = AnsiColorParser.StripAnsi(result.Output);

        // [=]   0 | 8B 12 31 BA 12 08 04 00 62 63 64 65 66 67 68 69 | ..1.....bcdefghi
        var dataMatch = Regex.Match(clean, @"\s*\d+\s+\|\s+([0-9A-Fa-f\s]{40,})\s+\|");
        if (dataMatch.Success)
        {
            return dataMatch.Groups[1].Value.Replace(" ", "").Trim().ToUpperInvariant();
        }

        return string.Empty;
    }

    /// <summary>
    /// Writes a single block with 16 bytes (32 hex characters).
    /// </summary>
    public async Task<bool> WriteBlockAsync(int blockNum, string keyA, string dataHex, CancellationToken ct = default)
    {
        dataHex = dataHex.Replace(" ", "").Trim().ToUpperInvariant();
        if (dataHex.Length != 32)
        {
            throw new ArgumentException("Data must be exactly 16 bytes (32 hex characters).");
        }

        string force = (blockNum == 0) ? "--force " : "";
        string cmd = $"hf mf wrbl --blk {blockNum} {force}-k {keyA} -d {dataHex}";
        var result = await Pm3ProcessService.Instance.ExecuteAsync(cmd, 30_000, ct);
        if (!result.Success)
        {
            return false;
        }

        string clean = AnsiColorParser.StripAnsi(result.Output);

        return clean.Contains("Write ( ok )", StringComparison.OrdinalIgnoreCase) ||
               clean.Contains("Write block ok", StringComparison.OrdinalIgnoreCase) ||
               clean.Contains("success", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Changes UID on Gen 2 (CUID) or Gen 1a magic card.
    /// </summary>
    public async Task<(bool success, string message)> ChangeUidAsync(string newUidHex, bool isGen2Cuid = true, string keyA = "FFFFFFFFFFFF", CancellationToken ct = default)
    {
        var proc = Pm3ProcessService.Instance;
        newUidHex = newUidHex.Replace(" ", "").Replace(":", "").Trim().ToUpperInvariant();

        if (newUidHex.Length != 8)
        {
            return (false, "UID must be 4 bytes (8 hex characters), e.g. 11223344.");
        }

        if (isGen2Cuid)
        {
            // Gen 2 / CUID: Read existing block 0, replace UID and compute BCC
            string block0 = await ReadSingleBlockAsync(0, keyA, ct);
            if (string.IsNullOrEmpty(block0) || block0.Length != 32)
            {
                return (false, "Failed to read block 0 using key: " + keyA);
            }

            // Calculate BCC for 4-byte UID: bcc = b0 ^ b1 ^ b2 ^ b3
            byte b0 = Convert.ToByte(newUidHex.Substring(0, 2), 16);
            byte b1 = Convert.ToByte(newUidHex.Substring(2, 2), 16);
            byte b2 = Convert.ToByte(newUidHex.Substring(4, 2), 16);
            byte b3 = Convert.ToByte(newUidHex.Substring(6, 2), 16);
            byte bcc = (byte)(b0 ^ b1 ^ b2 ^ b3);

            // Construct new block 0
            // bytes 0..3: UID (8 hex chars)
            // byte 4: BCC (2 hex chars)
            // bytes 5..15: SAK, ATQA, and original manufacturer data (22 hex chars)
            string restOfBlock0 = block0.Substring(10);
            string newBlock0 = $"{newUidHex}{bcc:X2}{restOfBlock0}";

            // Write to block 0 with --force
            bool ok = await WriteBlockAsync(0, keyA, newBlock0, ct);
            if (!ok)
            {
                return (false, "Write command returned failure. Check card positioning.");
            }

            // Verify by reading back
            string verifyBlock0 = await ReadSingleBlockAsync(0, keyA, ct);
            if (verifyBlock0.StartsWith(newUidHex, StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Successfully changed UID to {newUidHex} and verified Block 0 ({verifyBlock0})!");
            }
            else
            {
                return (false, $"Write seemed to succeed but verification read returned: {verifyBlock0.Substring(0, Math.Min(8, verifyBlock0.Length))}");
            }
        }
        else
        {
            // Gen 1a Magic card
            string cmd = $"hf mf csetuid -u {newUidHex}";
            var result = await Pm3ProcessService.Instance.ExecuteAsync(cmd, 30_000, ct);
            if (!result.Success)
            {
                return (false, $"Gen 1a write failed ({result.Failure}): {result.Error}");
            }

            string clean = AnsiColorParser.StripAnsi(result.Output);
            if (clean.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("UID set to", StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Successfully changed UID to {newUidHex} via Gen 1a magic command!");
            }
            return (false, "Gen 1a write returned: " + clean);
        }
    }

    /// <summary>
    /// Finds the card keys and dumps its contents to the client's dump folder.
    /// Only MIFARE Classic cards are supported by `hf mf autopwn`.
    /// </summary>
    public async Task<Pm3Result> AutopwnAsync(TagInfo tag, CancellationToken ct = default)
    {
        CardCapabilities capabilities = CardCapabilities.For(tag);
        if (!capabilities.CanBackup)
        {
            return Pm3Result.Fail(Pm3Failure.Unsupported, capabilities.Summary);
        }

        string command = $"hf mf autopwn {capabilities.SizeArgument}".Trim();
        return await Pm3ProcessService.Instance.ExecuteAsync(command, 600_000, ct);
    }

    /// <summary>
    /// Runs autopwn and moves the produced files into the card library.
    /// </summary>
    public async Task<(Pm3Result Result, DumpEntry? Entry)> BackupCardAsync(
        TagInfo tag, string? alias = null, IProgress<string>? stage = null, CancellationToken ct = default)
    {
        stage?.Report("Finding keys and reading the card...");

        Pm3Result result = await AutopwnAsync(tag, ct);
        if (!result.Success)
        {
            return (result, null);
        }

        stage?.Report("Saving the backup...");
        DumpEntry? entry = DumpLibrary.SaveAutopwnResult(tag, alias);
        return (result, entry);
    }

    /// <summary>
    /// Checks whether the default key FFFFFFFFFFFF opens every sector of the
    /// target card (the common case for blank magic cards).
    /// </summary>
    public async Task<KeyCheckResult> CheckDefaultKeysAsync(CancellationToken ct = default)
    {
        Pm3Result result = await Pm3ProcessService.Instance.ExecuteAsync(
            "hf mf chk --1k -k FFFFFFFFFFFF", 90_000, ct);
        if (!result.Success)
        {
            throw new Pm3CommandException("hf mf chk --1k -k FFFFFFFFFFFF", result);
        }

        string clean = AnsiColorParser.StripAnsi(result.Output);
        var open = new bool[16];
        int openCount = 0;

        foreach (Match match in Regex.Matches(
                     clean,
                     @"\[\+\]\s+(\d{3})\s+\|\s+\d{3}\s+\|\s+([0-9A-Fa-f]{12})\s+\|\s+(\d)"))
        {
            if (!int.TryParse(match.Groups[1].Value, out int sector) || sector >= 16)
            {
                continue;
            }

            if (match.Groups[3].Value == "1" && !open[sector])
            {
                open[sector] = true;
                openCount++;
            }
        }

        var locked = Enumerable.Range(0, 16).Where(sector => !open[sector]).ToList();
        return new KeyCheckResult(openCount == 16, openCount, 16, locked);
    }

    /// <summary>
    /// Finds the target card keys with autopwn and keeps its dump/key files in
    /// the library. Returns whether every key was recovered.
    /// </summary>
    public async Task<(Pm3Result Result, DumpEntry? Entry, bool AllKeysFound)> FindTargetKeysAsync(
        TagInfo tag, IProgress<string>? stage = null, CancellationToken ct = default)
    {
        stage?.Report("Finding the target card keys...");

        Pm3Result result = await AutopwnAsync(tag, ct);
        if (!result.Success)
        {
            return (result, null, false);
        }

        DumpEntry? entry = DumpLibrary.SaveAutopwnResult(tag);

        // The printed key table is the reliable signal: a sector whose Key A
        // and Key B results are both 0 is missing from the key file.
        string clean = AnsiColorParser.StripAnsi(result.Output);
        int sectorsChecked = 0;
        int sectorsMissing = 0;
        foreach (Match match in Regex.Matches(
                     clean,
                     @"\[\+\]\s+(\d{3})\s+\|\s+\d{3}\s+\|\s+([0-9A-Fa-f]{12})\s+\|\s+(\d)\s+\|\s+([0-9A-Fa-f]{12})\s+\|\s+(\d)"))
        {
            sectorsChecked++;
            if (match.Groups[3].Value == "0" && match.Groups[5].Value == "0")
            {
                sectorsMissing++;
            }
        }

        bool allKeys = sectorsChecked == 0 || sectorsMissing == 0;
        return (result, entry, allKeys);
    }

    /// <summary>
    /// Full clone for Gen 2/CUID targets (or MIFARE Classic targets whose keys
    /// are known). Pass null for <paramref name="keyPath"/> to authenticate with
    /// the default key.
    /// </summary>
    public async Task<Pm3Result> RestoreAsync(
        string dumpPath, string? keyPath, string targetUid, CancellationToken ct = default)
    {
        string dumpName = StageFile(dumpPath, "write");
        string? keyName = keyPath == null ? null : StageFile(keyPath, "write");

        try
        {
            string command = $"hf mf restore --1k -u {targetUid} -f {dumpName}";
            if (keyName != null)
            {
                command += $" -k {keyName} --ka";
            }

            return await Pm3ProcessService.Instance.ExecuteAsync(command, 180_000, ct);
        }
        finally
        {
            DeleteStaged(dumpName);
            if (keyName != null)
            {
                DeleteStaged(keyName);
            }
        }
    }

    /// <summary>
    /// Loads a complete dump onto a Gen 1a magic card through its backdoor.
    /// </summary>
    public async Task<Pm3Result> CloadAsync(string dumpPath, CancellationToken ct = default)
    {
        string dumpName = StageFile(dumpPath, "write");
        try
        {
            return await Pm3ProcessService.Instance.ExecuteAsync($"hf mf cload -f {dumpName}", 180_000, ct);
        }
        finally
        {
            DeleteStaged(dumpName);
        }
    }

    public async Task<Pm3Result> WriteBlock0Async(string block0Hex, string keyA, CancellationToken ct = default)
    {
        if (block0Hex.Length != 32)
        {
            return Pm3Result.Fail(Pm3Failure.Unsupported, "Block 0 must be 16 bytes (32 hex characters).");
        }

        return await Pm3ProcessService.Instance.ExecuteAsync(
            $"hf mf wrbl --blk 0 --force -k {keyA} -d {block0Hex}", 30_000, ct);
    }

    /// <summary>
    /// Data-only clone for cards whose UID cannot be changed: writes data
    /// blocks 1..62 while leaving block 0 and the sector trailers untouched.
    /// </summary>
    public async Task<Pm3Result> WriteDataOnlyAsync(
        MifareDumpFile dump, string defaultKeyA, string? keyPath,
        IProgress<(int current, int total)>? progress = null, CancellationToken ct = default)
    {
        for (int block = 1; block < 64; block++)
        {
            if (block % 4 == 3)
            {
                continue;
            }

            if (!dump.Blocks.TryGetValue(block, out string? hex) || hex.Length != 32)
            {
                continue;
            }

            string keyA = keyPath == null ? defaultKeyA : ReadKeyA(keyPath, block / 4);
            Pm3Result result = await Pm3ProcessService.Instance.ExecuteAsync(
                $"hf mf wrbl --blk {block} -k {keyA} -d {hex}", 30_000, ct);

            if (!result.Success)
            {
                return result;
            }

            progress?.Report((block, 64));
        }

        return Pm3Result.Ok(string.Empty);
    }

    /// <summary>
    /// Dumps the target again and compares it with the source backup.
    /// </summary>
    public async Task<WriteVerification> VerifyAsync(
        string expectedDumpPath, string? keyPath, bool checkUid, CancellationToken ct = default)
    {
        string baseName = $"_verify_{Guid.NewGuid():N}";
        // `hf mf dump` always loads a key file from the dump folder; when the
        // target uses the default key, synthesize one instead of omitting -k.
        string keyName = keyPath == null ? CreateDefaultKeyFile() : StageFile(keyPath, "verify");

        try
        {
            string command = $"hf mf dump --1k -f {baseName} -k {keyName}";
            Pm3Result result = await Pm3ProcessService.Instance.ExecuteAsync(command, 90_000, ct);
            if (!result.Success)
            {
                return new WriteVerification(false, 0, string.Empty, $"Verification dump failed: {result.Error}");
            }

            string jsonPath = Path.Combine(AppPaths.DumpsDirectory, baseName + ".json");
            if (!File.Exists(jsonPath))
            {
                return new WriteVerification(false, 0, string.Empty, "Verification dump was not written.");
            }

            MifareDumpFile actual = MifareDumpFile.Load(jsonPath);
            MifareDumpFile expected = MifareDumpFile.Load(expectedDumpPath);

            int mismatches = 0;
            for (int block = checkUid ? 0 : 1; block < 64; block++)
            {
                expected.Blocks.TryGetValue(block, out string? expectedHex);
                actual.Blocks.TryGetValue(block, out string? actualHex);
                if (!string.Equals(expectedHex ?? string.Empty, actualHex ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches++;
                }
            }

            string message = mismatches == 0
                ? "Verified: the card matches the backup."
                : $"{mismatches} block(s) differ from the backup.";

            return new WriteVerification(mismatches == 0, mismatches, actual.Uid, message);
        }
        finally
        {
            DeleteStaged(baseName + ".json");
            DeleteStaged(baseName + ".bin");
            DeleteStaged(keyName);
        }
    }

    private static string CreateDefaultKeyFile()
    {
        string name = $"_verifykeys_{Guid.NewGuid():N}.bin";
        byte[] keys = Enumerable.Repeat((byte)0xFF, 192).ToArray();
        File.WriteAllBytes(Path.Combine(AppPaths.DumpsDirectory, name), keys);
        return name;
    }

    public static string ReadKeyA(string keyPath, int sector)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(keyPath);
            int offset = sector * 12;
            return offset + 6 <= bytes.Length
                ? Convert.ToHexString(bytes, offset, 6)
                : "FFFFFFFFFFFF";
        }
        catch
        {
            return "FFFFFFFFFFFF";
        }
    }

    private static string StageFile(string sourcePath, string prefix)
    {
        string name = $"_{prefix}_{Guid.NewGuid():N}_{Path.GetFileName(sourcePath)}";
        File.Copy(sourcePath, Path.Combine(AppPaths.DumpsDirectory, name), overwrite: true);
        return name;
    }

    private static void DeleteStaged(string name)
    {
        try
        {
            File.Delete(Path.Combine(AppPaths.DumpsDirectory, name));
        }
        catch
        {
        }
    }
}
