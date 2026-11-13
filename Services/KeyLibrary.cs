using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iceman_gui.Core;
using iceman_gui.Models;

namespace iceman_gui.Services;

public enum KeyFileKind
{
    Bin,
    Dic
}

public sealed class KeyFileEntry
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public KeyFileKind Kind { get; init; }
    public int Count { get; set; }
    public DateTime Modified { get; set; }

    public string KindLabel => Kind == KeyFileKind.Bin ? "sector keys" : "dictionary";
}

public static class KeyLibrary
{
    public static string Root => AppPaths.KeysDirectory;

    public static List<KeyFileEntry> List()
    {
        AppPaths.EnsureCreated();
        var entries = new List<KeyFileEntry>();

        foreach (string path in Directory.EnumerateFiles(Root))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".bin" && extension != ".dic")
            {
                continue;
            }

            entries.Add(CreateEntry(path));
        }

        return entries.OrderByDescending(entry => entry.Modified).ToList();
    }

    public static KeyFileEntry CreateNewBin()
    {
        AppPaths.EnsureCreated();
        string name = $"keys-{DateTime.Now:yyyyMMdd-HHmmss}.bin";
        string path = UniquePath(name);
        MifareKeyFile.CreateEmpty1K(path).Save(path);
        return CreateEntry(path);
    }

    public static KeyFileEntry? Import(string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension != ".bin" && extension != ".dic" || !File.Exists(sourcePath))
        {
            return null;
        }

        AppPaths.EnsureCreated();
        string destination = UniquePath(Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destination, overwrite: false);
        return CreateEntry(destination);
    }

    public static void Delete(KeyFileEntry entry)
    {
        File.Delete(entry.Path);
    }

    private static KeyFileEntry CreateEntry(string path)
    {
        var kind = Path.GetExtension(path).Equals(".dic", StringComparison.OrdinalIgnoreCase)
            ? KeyFileKind.Dic
            : KeyFileKind.Bin;

        int count = 0;
        try
        {
            if (kind == KeyFileKind.Bin)
            {
                count = (int)(new FileInfo(path).Length / 12);
            }
            else
            {
                count = File.ReadLines(path)
                    .Select(line => line.Trim())
                    .Count(line => line.Length > 0 && !line.StartsWith('#'));
            }
        }
        catch
        {
        }

        return new KeyFileEntry
        {
            Name = Path.GetFileName(path),
            Path = path,
            Kind = kind,
            Count = count,
            Modified = File.GetLastWriteTime(path)
        };
    }

    private static string UniquePath(string name)
    {
        string path = Path.Combine(Root, name);
        if (!File.Exists(path))
        {
            return path;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int index = 2; index < 1000; index++)
        {
            path = Path.Combine(Root, $"{stem}-{index}{extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(Root, $"{stem}-{Guid.NewGuid():N}{extension}");
    }
}
