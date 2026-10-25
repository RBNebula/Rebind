namespace Rebind;

internal static class RebindManagedKeybindStore
{
    private const string ConfigFileName = "com.rebind.managed.cfg";
    private const string SectionPrefix = "Managed";
    private static readonly object Sync = new();
    private static ConfigFile? _config;

    public static KeyboardShortcut Load(string pluginGuid, string bindingId, KeyboardShortcut defaultValue)
    {
        lock (Sync)
        {
            var config = GetConfig();
            var entry = config.Bind(
                BuildSection(pluginGuid),
                BuildKey(bindingId),
                Serialize(defaultValue),
                "Rebind managed keybind.");

            if (TryDeserialize(entry.Value, out var parsed))
            {
                return parsed;
            }

            entry.Value = Serialize(defaultValue);
            TrySave(config);
            return defaultValue;
        }
    }

    public static void Save(string pluginGuid, string bindingId, KeyboardShortcut value)
    {
        lock (Sync)
        {
            var config = GetConfig();
            var entry = config.Bind(
                BuildSection(pluginGuid),
                BuildKey(bindingId),
                Serialize(value),
                "Rebind managed keybind.");
            entry.Value = Serialize(value);
            TrySave(config);
        }
    }

    private static ConfigFile GetConfig()
    {
        _config ??= new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, ConfigFileName), true);
        return _config;
    }

    private static string BuildSection(string pluginGuid)
    {
        return $"{SectionPrefix}.{Sanitize(pluginGuid, "plugin")}";
    }

    private static string BuildKey(string bindingId)
    {
        return Sanitize(bindingId, "binding");
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        trimmed = trimmed.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("=", "_");
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string Serialize(KeyboardShortcut value)
    {
        if (value.MainKey == KeyCode.None) return KeyCode.None.ToString();

        var parts = new List<string> { value.MainKey.ToString() };
        foreach (var modifier in value.Modifiers.Where(m => m != KeyCode.None))
        {
            parts.Add(modifier.ToString());
        }

        return string.Join("+", parts);
    }

    private static bool TryDeserialize(string? raw, out KeyboardShortcut result)
    {
        result = KeyboardShortcut.Empty;
        var text = raw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        var tokens = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
        {
            return false;
        }

        if (!Enum.TryParse(tokens[0], ignoreCase: true, out KeyCode mainKey))
        {
            return false;
        }

        if (mainKey == KeyCode.None)
        {
            result = KeyboardShortcut.Empty;
            return true;
        }

        var modifiers = new List<KeyCode>();
        for (var i = 1; i < tokens.Length; i++)
        {
            if (!Enum.TryParse(tokens[i], ignoreCase: true, out KeyCode key)) continue;
            if (key == KeyCode.None) continue;
            modifiers.Add(key);
        }

        result = new KeyboardShortcut(mainKey, modifiers.ToArray());
        return true;
    }

    private static void TrySave(ConfigFile config)
    {
        try
        {
            config.Save();
        }
        catch
        {
            // Keep runtime state even if save fails.
        }
    }
}
