using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using iceman_gui.Core;
using iceman_gui.Models;

namespace iceman_gui.Services;

public sealed class DumpEntry
{
    public string Uid { get; init; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string Magic { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Folder { get; init; } = string.Empty;
    public bool HasKeys { get; set; }

    public string DumpJsonPath => Path.Combine(Folder, $"hf-mf-{Uid}-dump.json");
    public string DumpBinPath => Path.Combine(Folder, $"hf-mf-{Uid}-dump.bin");
    public string KeyBinPath => Path.Combine(Folder, $"hf-mf-{Uid}-key.bin");

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Uid : Alias;
}

/// <summary>
/// Card backups saved by the app. Each card lives in its own folder so the
/// PM3-compatible filenames stay intact while the user gets an editable alias.
/// </summary>
public static class DumpLibrary
{
    private const string MetaFileName = "meta.json";

    public static string Root => AppPaths.DumpsDirectory;

    public static List<DumpEntry> List()
    {
        AppPaths.EnsureCreated();
        var entries = new List<DumpEntry>();

        foreach (string folder in Directory.GetDirectories(Root))
        {
            string uid = Path.GetFileName(folder);
            if (!File.Exists(Path.Combine(folder, $"hf-mf-{uid}-dump.json")))
            {
                continue;
            }

            DumpEntry entry = ReadMeta(folder, uid);
            entry.HasKeys = File.Exists(entry.KeyBinPath);
            entries.Add(entry);
        }

        return entries.OrderByDescending(entry => entry.CreatedAt).ToList();
    }

    public static DumpEntry? Get(string uid)
    {
        string folder = Path.Combine(Root, uid);
        if (!File.Exists(Path.Combine(folder, $"hf-mf-{uid}-dump.json")))
        {
            return null;
        }

        DumpEntry entry = ReadMeta(folder, uid);
        entry.HasKeys = File.Exists(entry.KeyBinPath);
        return entry;
    }

    /// <summary>
    /// Moves the files written by `hf mf autopwn` (flat, in the client dump
    /// folder) into the card's own folder and records its metadata.
    /// </summary>
    public static DumpEntry? SaveAutopwnResult(TagInfo tag, string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(tag.Uid))
        {
            return null;
        }

        string uid = tag.Uid;
        string folder = Path.Combine(Root, uid);
        Directory.CreateDirectory(folder);

        bool movedAny = false;
        foreach (string suffix in new[] { "dump.json", "dump.bin", "key.bin" })
        {
            string name = $"hf-mf-{uid}-{suffix}";
            string source = Path.Combine(Root, name);
            if (!File.Exists(source))
            {
                continue;
            }

            string destination = Path.Combine(folder, name);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
            movedAny = true;
        }

        if (!movedAny)
        {
            return null;
        }

        DumpEntry entry = ReadMeta(folder, uid);
        entry.Alias = string.IsNullOrWhiteSpace(alias)
            ? (string.IsNullOrWhiteSpace(entry.Alias) ? SuggestAlias(tag) : entry.Alias)
            : alias!;
        entry.CardType = string.IsNullOrWhiteSpace(tag.TagType) ? entry.CardType : tag.TagType;
        entry.Magic = string.IsNullOrWhiteSpace(tag.MagicType) ? entry.Magic : tag.MagicType;
        entry.CreatedAt = DateTime.Now;
        entry.HasKeys = File.Exists(entry.KeyBinPath);
        WriteMeta(entry);
        return entry;
    }

    /// <summary>
    /// Copies an existing PM3 dump (and its sibling .bin/key files, if any)
    /// into the library. Returns null when the file cannot be read.
    /// </summary>
    public static DumpEntry? Import(string jsonPath, string? alias = null)
    {
        if (!File.Exists(jsonPath))
        {
            return null;
        }

        MifareDumpFile dump;
        try
        {
            dump = MifareDumpFile.Load(jsonPath);
        }
        catch
        {
            return null;
        }

        string uid = string.IsNullOrWhiteSpace(dump.Uid)
            ? SanitizeFileName(Path.GetFileNameWithoutExtension(jsonPath))
            : dump.Uid;

        string folder = Path.Combine(Root, uid);
        Directory.CreateDirectory(folder);

        string baseName = Path.GetFileNameWithoutExtension(jsonPath);
        if (baseName.EndsWith("-dump", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^"-dump".Length];
        }

        string targetJson = Path.Combine(folder, $"hf-mf-{uid}-dump.json");
        if (!string.Equals(Path.GetFullPath(jsonPath), Path.GetFullPath(targetJson), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(jsonPath, targetJson, overwrite: true);
        }

        string sourceDirectory = Path.GetDirectoryName(jsonPath)!;
        foreach (string suffix in new[] { "dump.bin", "key.bin" })
        {
            string sibling = Path.Combine(sourceDirectory, $"{baseName}-{suffix}");
            if (File.Exists(sibling))
            {
                File.Copy(sibling, Path.Combine(folder, $"hf-mf-{uid}-{suffix}"), overwrite: true);
            }
        }

        DumpEntry entry = ReadMeta(folder, uid);
        entry.Alias = string.IsNullOrWhiteSpace(alias)
            ? (string.IsNullOrWhiteSpace(entry.Alias) ? $"MIFARE {uid}" : entry.Alias)
            : alias!;
        entry.CardType = string.IsNullOrWhiteSpace(entry.CardType)
            ? InferCardType(dump.Sak)
            : entry.CardType;
        if (entry.CreatedAt == default)
        {
            entry.CreatedAt = File.GetLastWriteTime(targetJson);
        }
        entry.HasKeys = File.Exists(entry.KeyBinPath);
        WriteMeta(entry);
        return entry;
    }

    public static void SetAlias(string uid, string alias)
    {
        DumpEntry? entry = Get(uid);
        if (entry == null)
        {
            return;
        }

        entry.Alias = alias.Trim();
        WriteMeta(entry);
    }

    public static MifareDumpFile? LoadDump(string uid)
    {
        string path = Path.Combine(Root, uid, $"hf-mf-{uid}-dump.json");
        return File.Exists(path) ? MifareDumpFile.Load(path) : null;
    }

    private static DumpEntry ReadMeta(string folder, string uid)
    {
        var entry = new DumpEntry { Uid = uid, Folder = folder, Alias = $"MIFARE {uid}" };
        string metaPath = Path.Combine(folder, MetaFileName);

        if (File.Exists(metaPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
                JsonElement root = document.RootElement;
                entry.Alias = GetString(root, "alias", entry.Alias);
                entry.CardType = GetString(root, "cardType", string.Empty);
                entry.Magic = GetString(root, "magic", string.Empty);
                if (DateTime.TryParse(GetString(root, "createdAt", string.Empty), out DateTime created))
                {
                    entry.CreatedAt = created;
                }
            }
            catch
            {
            }
        }

        if (entry.CreatedAt == default)
        {
            string dumpPath = Path.Combine(folder, $"hf-mf-{uid}-dump.json");
            entry.CreatedAt = File.Exists(dumpPath) ? File.GetLastWriteTime(dumpPath) : DateTime.Now;
        }

        if (string.IsNullOrWhiteSpace(entry.CardType))
        {
            entry.CardType = "MIFARE Classic";
        }

        return entry;
    }

    private static void WriteMeta(DumpEntry entry)
    {
        Directory.CreateDirectory(entry.Folder);
        var root = new JsonObject
        {
            ["uid"] = entry.Uid,
            ["alias"] = entry.Alias,
            ["cardType"] = entry.CardType,
            ["magic"] = entry.Magic,
            ["createdAt"] = entry.CreatedAt.ToString("O")
        };

        File.WriteAllText(
            Path.Combine(entry.Folder, MetaFileName),
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string SuggestAlias(TagInfo tag)
    {
        string type = string.IsNullOrWhiteSpace(tag.TagType) ? "MIFARE card" : tag.TagType;
        string suffix = tag.Uid.Length > 4 ? tag.Uid[^4..] : tag.Uid;
        return $"{type} {suffix}";
    }

    private static string InferCardType(string sak) => sak switch
    {
        "08" => "MIFARE Classic 1K",
        "09" => "MIFARE Mini",
        "18" => "MIFARE Classic 4K",
        _ => "MIFARE Classic"
    };

    private static string GetString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? fallback)
            : fallback;

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name.Length == 0 ? "dump" : name;
    }
}
