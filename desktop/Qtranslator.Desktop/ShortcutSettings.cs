using System.IO;
using System.Text.Json;

namespace Qtranslator.Desktop;

public enum ShortcutModifiers
{
    ControlShift,
    ControlAlt,
    AltShift
}

public sealed record ShortcutSettings(ShortcutModifiers Modifiers, string Key)
{
    public static ShortcutSettings Default { get; } =
        new(ShortcutModifiers.ControlAlt, "X");

    public string DisplayName => $"{Modifiers switch
    {
        ShortcutModifiers.ControlAlt => "Ctrl+Alt",
        ShortcutModifiers.AltShift => "Alt+Shift",
        _ => "Ctrl+Shift"
    }}+{Key.ToUpperInvariant()}";
}

public static class ShortcutSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Qtranslator");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "desktop-settings.json");

    public static ShortcutSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ShortcutSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<ShortcutSettings>(File.ReadAllText(SettingsPath));
            return settings is not null && IsAllowed(settings)
                ? settings
                : ShortcutSettings.Default;
        }
        catch
        {
            return ShortcutSettings.Default;
        }
    }

    public static void Save(ShortcutSettings settings)
    {
        if (!IsAllowed(settings)) throw new InvalidOperationException("该快捷键保留给旧 Translator 或 AI 截图，请选择其他组合。");
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsValidKey(string key) =>
        key.Length == 1 && key[0] is >= 'A' and <= 'Z';

    public static bool IsAllowed(ShortcutSettings settings) =>
        Enum.IsDefined(settings.Modifiers) && IsValidKey(settings.Key) &&
        !(settings.Modifiers == ShortcutModifiers.ControlShift && settings.Key is "X" or "D");
}
