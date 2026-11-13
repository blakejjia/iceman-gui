using System;
using System.IO;

namespace iceman_gui.Core;

/// <summary>
/// User-writable locations. The application must never write into the
/// installation folder: it may be read-only (e.g. Program Files).
/// </summary>
public static class AppPaths
{
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iceman-gui");

    public static string DocumentsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iceman-gui");

    public static string DumpsDirectory => Path.Combine(DocumentsDirectory, "dumps");
    public static string KeysDirectory => Path.Combine(AppDataDirectory, "keys");
    public static string ClientHome => Path.Combine(AppDataDirectory, "client-home");
    public static string LibraryFile => Path.Combine(AppDataDirectory, "library.json");
    public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(DocumentsDirectory);
        Directory.CreateDirectory(DumpsDirectory);
        Directory.CreateDirectory(KeysDirectory);
        Directory.CreateDirectory(ClientHome);
    }

    /// <summary>
    /// One-time copy of dumps saved by older builds next to the executable.
    /// </summary>
    public static void MigrateLegacyDumps(string? legacyDumpsDirectory)
    {
        if (string.IsNullOrWhiteSpace(legacyDumpsDirectory) || !Directory.Exists(legacyDumpsDirectory))
        {
            return;
        }

        string marker = Path.Combine(AppDataDirectory, ".dumps-migrated");
        if (File.Exists(marker))
        {
            return;
        }

        try
        {
            foreach (string file in Directory.GetFiles(legacyDumpsDirectory))
            {
                string destination = Path.Combine(DumpsDirectory, Path.GetFileName(file));
                if (!File.Exists(destination))
                {
                    File.Copy(file, destination);
                }
            }

            File.WriteAllText(marker, DateTime.Now.ToString("O"));
        }
        catch
        {
            // Migration is best-effort; the app still works with an empty folder.
        }
    }
}
