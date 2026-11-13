using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace iceman_gui.Models;

public sealed record MifareKeyPair(string KeyA, string KeyB);

/// <summary>
/// A MIFARE Classic 1K key file (.bin, 16 sectors x KeyA/KeyB) or a key
/// dictionary (.dic, one 12-hex key per line).
/// </summary>
public sealed class MifareKeyFile
{
    public const int SectorCount = 16;
    public const int KeyLength = 12;

    private static readonly Regex KeyRegex = new("^[0-9A-Fa-f]{12}$", RegexOptions.Compiled);

    public string Path { get; set; } = string.Empty;
    public bool IsDictionary { get; set; }
    public List<MifareKeyPair> Sectors { get; } = new();
    public List<string> DictionaryKeys { get; } = new();

    public static bool IsValidKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && KeyRegex.IsMatch(value.Trim());

    public static MifareKeyFile CreateEmpty1K(string path)
    {
        var file = new MifareKeyFile { Path = path };
        for (int sector = 0; sector < SectorCount; sector++)
        {
            file.Sectors.Add(new MifareKeyPair("FFFFFFFFFFFF", "FFFFFFFFFFFF"));
        }
        return file;
    }

    public static MifareKeyFile Load(string path)
    {
        var file = new MifareKeyFile { Path = path };
        bool dictionary = System.IO.Path.GetExtension(path).Equals(".dic", StringComparison.OrdinalIgnoreCase);
        file.IsDictionary = dictionary;

        if (dictionary)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string value = line.Trim();
                if (value.Length == 0 || value.StartsWith('#'))
                {
                    continue;
                }

                if (IsValidKey(value))
                {
                    file.DictionaryKeys.Add(value.ToUpperInvariant());
                }
            }
            return file;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < SectorCount * 12)
        {
            throw new InvalidDataException(
                $"A 1K key file must contain at least {SectorCount * 12} bytes (found {bytes.Length}).");
        }

        for (int sector = 0; sector < SectorCount; sector++)
        {
            int offset = sector * 12;
            file.Sectors.Add(new MifareKeyPair(
                Convert.ToHexString(bytes, offset, 6),
                Convert.ToHexString(bytes, offset + 6, 6)));
        }

        return file;
    }

    public void Save(string path)
    {
        Path = path;
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (IsDictionary)
        {
            File.WriteAllText(path, string.Join(Environment.NewLine, DictionaryKeys) + Environment.NewLine, Encoding.ASCII);
            return;
        }

        var bytes = new byte[SectorCount * 12];
        for (int sector = 0; sector < Math.Min(SectorCount, Sectors.Count); sector++)
        {
            WriteKey(bytes, sector * 12, Sectors[sector].KeyA);
            WriteKey(bytes, sector * 12 + 6, Sectors[sector].KeyB);
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteKey(byte[] destination, int offset, string key)
    {
        string normalized = (key ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsValidKey(normalized))
        {
            throw new InvalidDataException($"Invalid key '{key}': expected 12 hex characters.");
        }

        for (int i = 0; i < 6; i++)
        {
            destination[offset + i] = Convert.ToByte(normalized.Substring(i * 2, 2), 16);
        }
    }

    public string? FirstInvalidKey()
    {
        if (IsDictionary)
        {
            return DictionaryKeys.FirstOrDefault(key => !IsValidKey(key));
        }

        return Sectors
            .SelectMany(sector => new[] { sector.KeyA, sector.KeyB })
            .FirstOrDefault(key => !IsValidKey(key));
    }
}
