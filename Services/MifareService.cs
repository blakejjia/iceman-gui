using System;
using System.IO;
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
        var proc = Pm3ProcessService.Instance;
        string output = await proc.ExecuteCommandAsync("hf mf chk --1k", ct, 30000);
        string clean = AnsiColorParser.StripAnsi(output);

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
    /// Reads all 64 blocks of a 1K card using the known keys.
    /// </summary>
    public async Task ReadAllBlocksAsync(MifareCardData card, IProgress<(int current, int total)>? progress = null, CancellationToken ct = default)
    {
        var proc = Pm3ProcessService.Instance;
        int totalBlocks = 64;

        for (int b = 0; b < totalBlocks; b++)
        {
            ct.ThrowIfCancellationRequested();
            int secNum = b / 4;
            int blkInSec = b % 4;
            var sector = card.Sectors[secNum];
            string key = sector.KeyA;

            string cmd = $"hf mf rdbl --blk {b} -a -k {key}";
            string outBlock = await proc.ExecuteCommandAsync(cmd, ct, 8000);
            string clean = AnsiColorParser.StripAnsi(outBlock);

            // [+]  data: 8B 12 31 BA 20 08 04 00 62 63 64 65 66 67 68 69
            var dataMatch = Regex.Match(clean, @"data:\s*([0-9A-Fa-f\s]{32,})");
            if (dataMatch.Success)
            {
                string rawHex = dataMatch.Groups[1].Value.Replace(" ", "").Trim().ToUpperInvariant();
                if (rawHex.Length == 32)
                {
                    sector.Blocks[blkInSec].DataHex = rawHex;
                    sector.Blocks[blkInSec].DataAscii = MifareBlock.HexToAscii(rawHex);

                    if (b == 0)
                    {
                        // Extract UID from block 0 (first 4 bytes / 8 hex chars)
                        card.Uid = rawHex.Substring(0, 8);
                    }
                }
            }

            progress?.Report((b + 1, totalBlocks));
        }
    }

    /// <summary>
    /// Reads a single block.
    /// </summary>
    public async Task<string> ReadSingleBlockAsync(int blockNum, string keyA, CancellationToken ct = default)
    {
        var proc = Pm3ProcessService.Instance;
        string cmd = $"hf mf rdbl --blk {blockNum} -a -k {keyA}";
        string outBlock = await proc.ExecuteCommandAsync(cmd, ct, 8000);
        string clean = AnsiColorParser.StripAnsi(outBlock);

        var dataMatch = Regex.Match(clean, @"data:\s*([0-9A-Fa-f\s]{32,})");
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
        var proc = Pm3ProcessService.Instance;
        dataHex = dataHex.Replace(" ", "").Trim().ToUpperInvariant();
        if (dataHex.Length != 32)
        {
            throw new ArgumentException("Data must be exactly 16 bytes (32 hex characters).");
        }

        string cmd = $"hf mf wrbl --blk {blockNum} -a -k {keyA} -d {dataHex}";
        string output = await proc.ExecuteCommandAsync(cmd, ct, 10000);
        string clean = AnsiColorParser.StripAnsi(output);

        return clean.Contains("isOk:01", StringComparison.OrdinalIgnoreCase) ||
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
            // bytes 0..3: UID
            // byte 4: BCC
            // bytes 5..15: SAK, ATQA, and original manufacturer data from block 0
            string restOfBlock0 = block0.Substring(10); // from byte 5 to 15 (22 hex chars)
            string newBlock0 = $"{newUidHex}{bcc:X2}{restOfBlock0}";

            // Write to block 0
            bool ok = await WriteBlockAsync(0, keyA, newBlock0, ct);
            if (!ok)
            {
                return (false, "Write command returned failure. Check card positioning.");
            }

            // Verify by reading back
            string verifyBlock0 = await ReadSingleBlockAsync(0, keyA, ct);
            if (verifyBlock0.StartsWith(newUidHex, StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Successfully changed UID to {newUidHex} and verified Block 0!");
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
            string output = await proc.ExecuteCommandAsync(cmd, ct, 10000);
            string clean = AnsiColorParser.StripAnsi(output);
            if (clean.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("UID set to", StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Successfully changed UID to {newUidHex} via Gen 1a magic command!");
            }
            return (false, "Gen 1a write returned: " + clean);
        }
    }
}
