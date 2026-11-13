using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace iceman_gui.Core;

/// <summary>
/// The Proxmark3 client stores history, logs and preferences under HOME.
/// Point that at %APPDATA% and keep the dump paths in sync with AppPaths.
/// </summary>
public static class ClientHome
{
    public static string PreferencesPath =>
        Path.Combine(AppPaths.ClientHome, ".proxmark3", "preferences.json");

    public static void EnsureSeeded(string? templatePath)
    {
        try
        {
            string directory = Path.GetDirectoryName(PreferencesPath)!;
            Directory.CreateDirectory(directory);

            JsonObject root;
            string source = File.Exists(PreferencesPath)
                ? PreferencesPath
                : (templatePath != null && File.Exists(templatePath) ? templatePath : string.Empty);

            if (source.Length > 0)
            {
                root = JsonNode.Parse(File.ReadAllText(source)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            root["Created"] ??= "proxmark3";
            root["FileType"] ??= "settings";

            // The client prefixes bare filenames with these paths, so they must
            // point at a writable, user-visible folder.
            string dumps = AppPaths.DumpsDirectory.Replace('\\', '/');
            root["file.default.savepath"] = dumps;
            root["file.default.dumppath"] = dumps;
            root["file.default.tracepath"] = dumps;

            File.WriteAllText(
                PreferencesPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // A client-side preference failure must not stop the app; the
            // client will fall back to its own defaults.
        }
    }
}
