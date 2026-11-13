using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace iceman_gui.Core;

public enum AppTheme
{
    Dark,
    Light,
    System
}

/// <summary>
/// Applies and persists the application theme. Dark is the default; the
/// choice is stored in settings.json under %APPDATA%\iceman-gui.
/// </summary>
public static class ThemeService
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Initialize()
    {
        Current = LoadTheme();
        Apply(Current, persist: false);
    }

    public static void SetTheme(AppTheme theme)
    {
        Current = theme;
        Apply(theme, persist: true);
    }

    private static void Apply(AppTheme theme, bool persist)
    {
        ApplicationTheme effective = theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.System => GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
            _ => ApplicationTheme.Dark
        };

        try
        {
            ApplicationThemeManager.Apply(effective, WindowBackdropType.Mica, updateAccent: true);
        }
        catch
        {
            // A theme failure must not prevent startup.
        }

        if (persist)
        {
            SaveTheme(theme);
        }
    }

    private static SystemTheme GetSystemTheme()
    {
        try
        {
            return ApplicationThemeManager.GetSystemTheme();
        }
        catch
        {
            return SystemTheme.Dark;
        }
    }

    private static AppTheme LoadTheme()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return AppTheme.Dark;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (document.RootElement.TryGetProperty("theme", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                Enum.TryParse(value.GetString(), ignoreCase: true, out AppTheme parsed))
            {
                return parsed;
            }
        }
        catch
        {
        }

        return AppTheme.Dark;
    }

    private static void SaveTheme(AppTheme theme)
    {
        try
        {
            JsonObject root = new();
            if (File.Exists(AppPaths.SettingsFile))
            {
                root = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile)) as JsonObject ?? new JsonObject();
            }

            root["theme"] = theme.ToString();
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            File.WriteAllText(
                AppPaths.SettingsFile,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persistence is best-effort.
        }
    }
}
