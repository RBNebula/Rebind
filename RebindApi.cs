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

    private static KeyboardShortcut NormalizeDefaultShortcut(KeyboardShortcut value)
    {
        return value.MainKey == KeyCode.None ? KeyboardShortcut.Empty : value;
    }
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
