using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value) ||
        element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);

    private static string GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim().ToUpperInvariant()
            : string.Empty;
}
