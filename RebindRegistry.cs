namespace Rebind;

internal static class RebindRegistry
{
    private const string DefaultSectionTitle = "Misc";
    private const string DefaultKeybindTitle = "Unnamed Keybind";
    private static readonly object Sync = new();
    private static readonly List<RebindEntry> Entries = new();
    private static bool _dirty = true;
    private static int _version;

    public static RebindHandle Register(
        string sectionTitle,
        string keybindTitle,
        Func<KeyCode> getter,
        Action<KeyCode> setter,
        KeyCode defaultKey,
        Action<KeyCode>? onChanged)
    {
        if (getter == null) throw new ArgumentNullException(nameof(getter));
        if (setter == null) throw new ArgumentNullException(nameof(setter));
        var entry = CreateRegisteredBaseEntry(ref sectionTitle, ref keybindTitle);
        entry.Getter = getter;
        entry.Setter = setter;
        entry.DefaultKey = defaultKey;
        entry.OnChanged = onChanged;
        return AddEntryAndCreateHandle(entry);
    }

    public static RebindHandle RegisterShortcut(
        string sectionTitle,
        string keybindTitle,
        Func<KeyboardShortcut> getter,
        Action<KeyboardShortcut> setter,
        KeyboardShortcut defaultShortcut,
        Action<KeyboardShortcut>? onChanged)
    {
        if (getter == null) throw new ArgumentNullException(nameof(getter));
        if (setter == null) throw new ArgumentNullException(nameof(setter));
        var entry = CreateRegisteredBaseEntry(ref sectionTitle, ref keybindTitle);
        entry.Mode = RebindEntryMode.KeyboardShortcut;
        entry.ShortcutGetter = getter;
        entry.ShortcutSetter = setter;
        entry.DefaultShortcut = defaultShortcut;
        entry.OnShortcutChanged = onChanged;
        return AddEntryAndCreateHandle(entry);
    }

    public static RebindHandle RegisterInputAction(
        string sectionTitle,
        string keybindTitle,
        InputActionReference actionRef,
        int bindingIndex,
        Action? onChanged)
    {
        return RegisterInputAction(sectionTitle, keybindTitle, actionRef, bindingIndex, defaultBindingPath: null, onChanged);
    }

    public static RebindHandle RegisterInputAction(
        string sectionTitle,
        string keybindTitle,
        InputActionReference actionRef,
        int bindingIndex,
        string? defaultBindingPath,
        Action? onChanged)
    {
        if (actionRef == null) throw new ArgumentNullException(nameof(actionRef));
        if (bindingIndex < 0) throw new ArgumentOutOfRangeException(nameof(bindingIndex));
        var entry = CreateRegisteredBaseEntry(ref sectionTitle, ref keybindTitle);
        entry.Mode = RebindEntryMode.InputAction;
        entry.ActionReference = actionRef;
        entry.BindingIndex = bindingIndex;
        entry.DefaultBindingPath = NormalizeBindingPath(defaultBindingPath);
        entry.OnChangedRaw = onChanged;
        return AddEntryAndCreateHandle(entry);
    }

    private static RebindEntry CreateRegisteredBaseEntry(ref string sectionTitle, ref string keybindTitle)
    {
        NormalizeTitles(ref sectionTitle, ref keybindTitle);
        return CreateBaseEntry(sectionTitle, keybindTitle);
    }

    private static RebindHandle AddEntryAndCreateHandle(RebindEntry entry)
    {
        lock (Sync)
        {
            entry.SortOrder = Entries.Count;
            Entries.Add(entry);
            MarkDirty();
        }

        return new RebindHandle(entry.Id);
    }

    public static void Unregister(Guid id)
    {
        lock (Sync)
        {
            if (Entries.RemoveAll(e => e.Id == id) > 0)
            {
                MarkDirty();
            }
        }
    }

    public static int GetVersion()
    {
        lock (Sync)
        {
            return _version;
        }
    }

    public static bool ConsumeDirty()
    {
        lock (Sync)
        {
            if (!_dirty) return false;
            _dirty = false;
            return true;
        }
    }

    public static List<RebindEntry> Snapshot()
    {
        lock (Sync)
        {
            return CloneEntriesSortedBySortOrder();
        }
    }

    private static RebindEntry CreateBaseEntry(string sectionTitle, string keybindTitle)
    {
        return new RebindEntry
        {
            Id = Guid.NewGuid(),
            SectionTitle = sectionTitle,
            KeybindTitle = keybindTitle
        };
    }

    private static string NormalizeBindingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path!.Trim();
    }

    private static List<RebindEntry> CloneEntriesSortedBySortOrder()
    {
        var clones = new List<RebindEntry>(Entries.Count);
        for (var i = 0; i < Entries.Count; i++)
        {
            clones.Add(Entries[i].Clone());
        }

        clones.Sort(CompareBySortOrder);
        return clones;
    }

    private static int CompareBySortOrder(RebindEntry left, RebindEntry right)
    {
        return left.SortOrder.CompareTo(right.SortOrder);
    }

    private static void NormalizeTitles(ref string sectionTitle, ref string keybindTitle)
    {
        sectionTitle = string.IsNullOrWhiteSpace(sectionTitle)
            ? DefaultSectionTitle
            : sectionTitle.Trim();
        keybindTitle = string.IsNullOrWhiteSpace(keybindTitle)
            ? DefaultKeybindTitle
            : keybindTitle.Trim();
    }

    private static void MarkDirty()
    {
        _dirty = true;
        _version++;
    }
}

internal sealed class RebindEntry
{
    public Guid Id { get; set; }
    public int SortOrder { get; set; }
    public string SectionTitle { get; set; } = string.Empty;
    public string KeybindTitle { get; set; } = string.Empty;
    public RebindEntryMode Mode { get; set; } = RebindEntryMode.KeyCode;
    public Func<KeyCode> Getter { get; set; } = () => KeyCode.None;
    public Action<KeyCode> Setter { get; set; } = _ => { };
    public KeyCode DefaultKey { get; set; } = KeyCode.None;
    public Action<KeyCode>? OnChanged { get; set; }
    public Func<KeyboardShortcut> ShortcutGetter { get; set; } = () => KeyboardShortcut.Empty;
    public Action<KeyboardShortcut> ShortcutSetter { get; set; } = _ => { };
    public KeyboardShortcut DefaultShortcut { get; set; } = KeyboardShortcut.Empty;
    public Action<KeyboardShortcut>? OnShortcutChanged { get; set; }
    public Action? OnChangedRaw { get; set; }
    public InputActionReference? ActionReference { get; set; }
    public int BindingIndex { get; set; } = -1;
    public string DefaultBindingPath { get; set; } = string.Empty;

    public RebindEntry Clone()
    {
        return new RebindEntry
        {
            Id = Id,
            SortOrder = SortOrder,
            SectionTitle = SectionTitle,
            KeybindTitle = KeybindTitle,
            Mode = Mode,
            Getter = Getter,
            Setter = Setter,
            DefaultKey = DefaultKey,
            OnChanged = OnChanged,
            ShortcutGetter = ShortcutGetter,
            ShortcutSetter = ShortcutSetter,
            DefaultShortcut = DefaultShortcut,
            OnShortcutChanged = OnShortcutChanged,
            OnChangedRaw = OnChangedRaw,
            ActionReference = ActionReference,
            BindingIndex = BindingIndex,
            DefaultBindingPath = DefaultBindingPath
        };
    }
}

internal enum RebindEntryMode
{
    KeyCode = 0,
    InputAction = 1,
    KeyboardShortcut = 2
}
