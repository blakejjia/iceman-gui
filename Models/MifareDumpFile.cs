using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace iceman_gui.Models;

public sealed class MifareSectorKeys
{
    public string KeyA { get; set; } = "FFFFFFFFFFFF";
    public string KeyB { get; set; } = "FFFFFFFFFFFF";
    public string AccessConditions { get; set; } = "FF078069";
    public Dictionary<string, string> AccessConditionsText { get; } = new();
}

/// <summary>
/// Reads and writes the Proxmark3 MIFARE Classic dump format ("mfc v2"),
/// which is what `hf mf autopwn` and `hf mf dump` produce.
/// </summary>
public sealed class MifareDumpFile
{
    public string Uid { get; set; } = string.Empty;
    public string Atqa { get; set; } = string.Empty;
    public string Sak { get; set; } = string.Empty;
    public Dictionary<int, string> Blocks { get; } = new();
    public Dictionary<int, MifareSectorKeys> SectorKeys { get; } = new();
    public string? SourcePath { get; set; }

    public bool Is1K => Blocks.Count == 64;

    public static MifareDumpFile Load(string path)
    {
        var dump = new MifareDumpFile { SourcePath = path };
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

        JsonElement root = document.RootElement;

        if (TryGetProperty(root, "Card", out JsonElement card))
        {
            dump.Uid = GetString(card, "UID");
            dump.Atqa = GetString(card, "ATQA");
            dump.Sak = GetString(card, "SAK");
        }

        if (TryGetProperty(root, "blocks", out JsonElement blocks) && blocks.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in blocks.EnumerateObject())
            {
                if (int.TryParse(property.Name, out int index))
                {
                    dump.Blocks[index] = (property.Value.GetString() ?? string.Empty).Trim().ToUpperInvariant();
                }
            }
        }

        if (TryGetProperty(root, "SectorKeys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in keys.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out int sector) || property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var sectorKeys = new MifareSectorKeys
                {
                    KeyA = GetString(property.Value, "KeyA"),
                    KeyB = GetString(property.Value, "KeyB"),
                    AccessConditions = GetString(property.Value, "AccessConditions")
                };

                if (TryGetProperty(property.Value, "AccessConditionsText", out JsonElement text) &&
                    text.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty item in text.EnumerateObject())
                    {
                        sectorKeys.AccessConditionsText[item.Name] = item.Value.GetString() ?? string.Empty;
                    }
                }

                dump.SectorKeys[sector] = sectorKeys;
            }
        }

        return dump;
    }

    public void Save(string path)
    {
        var root = new JsonObject
        {
            ["Created"] = "proxmark3",
            ["FileType"] = "mfc v2",
            ["Card"] = new JsonObject
            {
                ["UID"] = Uid,
                ["ATQA"] = Atqa,
                ["SAK"] = Sak
            }
        };

        var blocks = new JsonObject();
        foreach (var block in Blocks.OrderBy(pair => pair.Key))
        {
            blocks[block.Key.ToString()] = block.Value;
        }
        root["blocks"] = blocks;

        var keys = new JsonObject();
        foreach (var entry in SectorKeys.OrderBy(pair => pair.Key))
        {
            var text = new JsonObject();
            foreach (var item in entry.Value.AccessConditionsText)
            {
                text[item.Key] = item.Value;
            }

            keys[entry.Key.ToString()] = new JsonObject
            {
                ["KeyA"] = entry.Value.KeyA,
                ["KeyB"] = entry.Value.KeyB,
                ["AccessConditions"] = entry.Value.AccessConditions,
                ["AccessConditionsText"] = text
            };
        }
        root["SectorKeys"] = keys;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        SourcePath = path;
    }

    public void SaveBinary(string path)
    {
        var bytes = new byte[64 * 16];
        for (int block = 0; block < 64; block++)
        {
            if (!Blocks.TryGetValue(block, out string? hex) || hex.Length != 32)
            {
                continue;
            }

            for (int index = 0; index < 16; index++)
            {
                bytes[block * 16 + index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }
        }

        File.WriteAllBytes(path, bytes);
    }

    public MifareCardData ToCardData()
    {
        var card = MifareCardData.CreateEmpty1K(string.IsNullOrWhiteSpace(Uid) ? "00000000" : Uid);

        for (int sectorIndex = 0; sectorIndex < card.Sectors.Count; sectorIndex++)
        {
            MifareSector sector = card.Sectors[sectorIndex];

            if (SectorKeys.TryGetValue(sectorIndex, out MifareSectorKeys? keys))
            {
                sector.KeyA = keys.KeyA;
                sector.KeyB = keys.KeyB;
                sector.AccessBits = keys.AccessConditions;
            }

            for (int blockInSector = 0; blockInSector < sector.Blocks.Count; blockInSector++)
            {
                int blockNumber = sectorIndex * 4 + blockInSector;
                sector.Blocks[blockInSector].DataHex =
                    Blocks.TryGetValue(blockNumber, out string? hex) && hex.Length == 32
                        ? hex
                        : new string('0', 32);
            }
        }

        return card;
    }

    public static MifareDumpFile FromCardData(MifareCardData card)
    {
        var dump = new MifareDumpFile
        {
            Uid = card.Uid,
            Atqa = string.Empty,
            Sak = string.Empty
        };

        foreach (MifareSector sector in card.Sectors)
        {
            var keys = new MifareSectorKeys
            {
                KeyA = sector.KeyA,
                KeyB = sector.KeyB,
                AccessConditions = sector.AccessBits
            };

            dump.SectorKeys[sector.SectorNumber] = keys;

            foreach (MifareBlock block in sector.Blocks)
            {
                dump.Blocks[block.BlockNumber] = block.DataHex;
            }
        }

        if (dump.Blocks.TryGetValue(0, out string? blockZero) && blockZero.Length == 32)
        {
            dump.Sak = blockZero.Substring(10, 2);
            dump.Atqa = blockZero.Substring(12, 4);
        }

        return dump;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value) ||
        element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);

    private static string GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim().ToUpperInvariant()
            : string.Empty;
}
