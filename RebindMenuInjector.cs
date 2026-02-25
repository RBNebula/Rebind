namespace Rebind;

internal static partial class RebindMenuInjector
{
    private readonly struct ConflictPromptUiContext
    {
        public ConflictPromptUiContext(Canvas canvas, Button? buttonTemplate, TMP_Text? textTemplate)
        {
            Canvas = canvas;
            ButtonTemplate = buttonTemplate;
            TextTemplate = textTemplate;
        }

        public Canvas Canvas { get; }
        public Button? ButtonTemplate { get; }
        public TMP_Text? TextTemplate { get; }
    }

    private static readonly Dictionary<int, InjectedMenuState> States = new();
    private static readonly HashSet<int> OpenSettingsMenuIds = new();
    private static readonly Type? SettingsMenuType = AccessTools.TypeByName("SettingsMenu");
    private static readonly Type? SettingKeybindType = AccessTools.TypeByName("SettingKeybind");
    private static readonly Type? BaseSettingOptionType = AccessTools.TypeByName("BaseSettingOption");
    private static readonly FieldInfo? KeybindPageField = AccessTools.Field(SettingsMenuType, "_keybindsPage");
    private static readonly FieldInfo? BaseLabelField = AccessTools.Field(BaseSettingOptionType, "_label");
    private static readonly FieldInfo? KeybindValueLabelField = AccessTools.Field(SettingKeybindType, "_keybindLabel");
    private static readonly FieldInfo? HideDefaultBindField = AccessTools.Field(SettingKeybindType, "_hideWhenUsingDefaultBind");
    private static readonly FieldInfo? SettingKeybindActionField = AccessTools.Field(SettingKeybindType, "Action");

    public static void DrawConflictPrompt()
    {
        RebindConflictPrompt.Draw();
    }

    public static void EnsureInjected(object settingsMenuInstance)
    {
        if (settingsMenuInstance == null) return;
        if (SettingKeybindType == null || BaseSettingOptionType == null || KeybindPageField == null) return;

        var keybindPage = KeybindPageField.GetValue(settingsMenuInstance) as GameObject;
        if (keybindPage == null) return;

        var menuId = keybindPage.GetInstanceID();
        if (!States.TryGetValue(menuId, out var state))
        {
            state = new InjectedMenuState(keybindPage);
            States[menuId] = state;
            MarkSearchFilterDirty();
        }

        state.EnsureUpToDate(RebindRegistry.GetVersion());
    }

    public static void NotifySettingsMenuEnabled(object settingsMenuInstance)
    {
        UpdateSettingsMenuState(settingsMenuInstance, isOpen: true);
    }

    public static void NotifySettingsMenuDisabled(object settingsMenuInstance)
    {
        UpdateSettingsMenuState(settingsMenuInstance, isOpen: false);
    }

    public static bool ShouldSuppressExternalHotkeys()
    {
        if (OpenSettingsMenuIds.Count > 0) return true;
        return RebindConflictPrompt.IsVisible;
    }

    private static void UpdateSettingsMenuState(object settingsMenuInstance, bool isOpen)
    {
        if (settingsMenuInstance is not UnityEngine.Object settingsMenuObject) return;
        if (settingsMenuObject == null) return;

        var id = settingsMenuObject.GetInstanceID();
        if (isOpen)
        {
            OpenSettingsMenuIds.Add(id);
            return;
        }

        OpenSettingsMenuIds.Remove(id);
    }

    public static void RebuildAll()
    {
        var registryVersion = RebindRegistry.GetVersion();
        VisitAliveStates(state => state.RebuildIfStale(registryVersion));
        MarkSearchFilterDirty();
    }

    public static void RefreshNativeConflictVisualsAll()
    {
        VisitAliveStates(state => state.RefreshNativeConflictVisuals());
    }

    private static bool TryGetConflictPromptUiContext(out ConflictPromptUiContext context)
    {
        var deadIds = new List<int>();
        foreach (var pair in States)
        {
            var state = pair.Value;
            if (!state.IsAlive)
            {
                deadIds.Add(pair.Key);
                continue;
            }

            if (state.TryGetConflictPromptUiContext(out context))
            {
                RemoveStates(deadIds);
                return true;
            }
        }

        RemoveStates(deadIds);
        context = default;
        return false;
    }

    private static void VisitAliveStates(Action<InjectedMenuState> visitAction)
    {
        var deadIds = new List<int>();
        foreach (var pair in States)
        {
            var state = pair.Value;
            if (!state.IsAlive)
            {
                deadIds.Add(pair.Key);
                continue;
            }

            visitAction(state);
        }

        RemoveStates(deadIds);
    }

    private static void RemoveStates(List<int> deadIds)
    {
        for (var i = 0; i < deadIds.Count; i++)
        {
            States.Remove(deadIds[i]);
        }
    }
}
