namespace Rebind;

/// <summary>
/// Public registration surface for third-party mods.
/// </summary>
public static class RebindApi
{
    /// <summary>
    /// Registers a keybind (single key or modifier combo).
    /// </summary>
    public static RebindHandle RegisterKeybind(
        string sectionTitle,
        string keybindTitle,
        Func<KeyboardShortcut> getter,
        Action<KeyboardShortcut> setter,
        KeyboardShortcut defaultKeybind = default,
        Action<KeyboardShortcut>? onChanged = null)
    {
        return RebindRegistry.RegisterShortcut(
            sectionTitle,
            keybindTitle,
            getter,
            setter,
            NormalizeDefaultShortcut(defaultKeybind),
            onChanged);
    }

    /// <summary>
    /// Registers an InputAction binding by action reference and binding index.
    /// </summary>
    public static RebindHandle RegisterInputActionBinding(
        string sectionTitle,
        string keybindTitle,
        InputActionReference actionRef,
        int bindingIndex,
        Action? onChanged = null)
    {
        return RegisterInputActionBinding(sectionTitle, keybindTitle, actionRef, bindingIndex, defaultBindingPath: string.Empty, onChanged);
    }

    /// <summary>
    /// Registers an InputAction binding with an explicit default binding path.
    /// </summary>
    public static RebindHandle RegisterInputActionBinding(
        string sectionTitle,
        string keybindTitle,
        InputActionReference actionRef,
        int bindingIndex,
        string defaultBindingPath,
        Action? onChanged = null)
    {
        return RebindRegistry.RegisterInputAction(sectionTitle, keybindTitle, actionRef, bindingIndex, defaultBindingPath, onChanged);
    }

    /// <summary>
    /// Registers a keybind from a metadata packet and persists values in Rebind-managed config.
    /// </summary>
    public static RebindHandle RegisterManagedKeybind(ManagedKeybindPacket packet)
    {
        if (packet == null) throw new ArgumentNullException(nameof(packet));
        if (packet.Apply == null) throw new ArgumentNullException(nameof(packet.Apply));

        var pluginGuid = NormalizeRequired(packet.PluginGuid, nameof(packet.PluginGuid));
        var bindingId = NormalizeOptional(packet.BindingId);
        if (string.IsNullOrEmpty(bindingId))
        {
            bindingId = BuildDefaultBindingId(packet.SectionTitle, packet.KeybindTitle);
        }

        var defaultKeybind = NormalizeDefaultShortcut(packet.DefaultKeybind);
        var current = RebindManagedKeybindStore.Load(pluginGuid, bindingId, defaultKeybind);
        ApplyManagedShortcut(packet, current, pluginGuid, bindingId);

        return RegisterKeybind(
            packet.SectionTitle,
            packet.KeybindTitle,
            getter: () => current,
            setter: bind => current = bind,
            defaultKeybind: defaultKeybind,
            onChanged: bind =>
            {
                var normalized = NormalizeDefaultShortcut(bind);
                current = normalized;
                RebindManagedKeybindStore.Save(pluginGuid, bindingId, normalized);
                ApplyManagedShortcut(packet, normalized, pluginGuid, bindingId);
                packet.OnChanged?.Invoke(normalized);
            });
    }

    private static KeyboardShortcut NormalizeDefaultShortcut(KeyboardShortcut value)
    {
        return value.MainKey == KeyCode.None ? KeyboardShortcut.Empty : value;
    }

    private static string NormalizeRequired(string? value, string paramName)
    {
        var normalized = NormalizeOptional(value);
        if (!string.IsNullOrEmpty(normalized)) return normalized;
        throw new ArgumentException("Value is required.", paramName);
    }

    private static string NormalizeOptional(string? value)
    {
        if (value == null) return string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Trim();
    }

    private static string BuildDefaultBindingId(string sectionTitle, string keybindTitle)
    {
        var section = NormalizeOptional(sectionTitle);
        var keybind = NormalizeOptional(keybindTitle);
        if (section.Length == 0 && keybind.Length == 0)
        {
            return "binding";
        }

        if (section.Length == 0) return keybind;
        if (keybind.Length == 0) return section;
        return section + "." + keybind;
    }

    private static void ApplyManagedShortcut(ManagedKeybindPacket packet, KeyboardShortcut value, string pluginGuid, string bindingId)
    {
        try
        {
            packet.Apply(value);
        }
        catch (Exception ex)
        {
            Rebind.Log.LogError($"Managed keybind apply failed for '{pluginGuid}:{bindingId}': {ex}");
        }
    }
}

/// <summary>
/// Metadata packet for a Rebind-managed persistent keybind.
/// </summary>
public sealed class ManagedKeybindPacket
{
    /// <summary>Plugin identifier used as persistence scope (example: com.author.modname).</summary>
    public string PluginGuid { get; set; } = string.Empty;

    /// <summary>Stable identifier for this keybind inside the plugin scope.</summary>
    public string BindingId { get; set; } = string.Empty;

    /// <summary>Section title shown in Rebind menu.</summary>
    public string SectionTitle { get; set; } = string.Empty;

    /// <summary>Keybind title shown in Rebind menu.</summary>
    public string KeybindTitle { get; set; } = string.Empty;

    /// <summary>Default binding used for reset and first-run fallback.</summary>
    public KeyboardShortcut DefaultKeybind { get; set; } = KeyboardShortcut.Empty;

    /// <summary>Required callback invoked to apply the effective keybind to runtime state.</summary>
    public Action<KeyboardShortcut> Apply { get; set; } = _ => { };

    /// <summary>Optional callback invoked after Rebind applied and persisted a change.</summary>
    public Action<KeyboardShortcut>? OnChanged { get; set; }
}

/// <summary>
/// Disposable handle for a registered row.
/// </summary>
public sealed class RebindHandle : IDisposable
{
    private readonly Guid _id;
    private bool _disposed;

    internal RebindHandle(Guid id)
    {
        _id = id;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RebindRegistry.Unregister(_id);
    }
}
