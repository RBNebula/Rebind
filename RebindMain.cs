namespace Rebind;

[BepInPlugin(ModInfo.PLUGIN_GUID, ModInfo.PLUGIN_NAME, ModInfo.PLUGIN_VERSION)]
public sealed class Rebind : BaseUnityPlugin
{
    private Harmony? _harmony;
    private static ManualLogSource? _log;
    private static ConfigEntry<bool>? _enableTimingLogs;
    private static ConfigEntry<int>? _timingWarnThresholdMs;

    internal static ManualLogSource Log => _log ?? BepInEx.Logging.Logger.CreateLogSource(ModInfo.PLUGIN_NAME);
    internal static bool TimingLogsEnabled => _enableTimingLogs?.Value ?? false;
    internal static int TimingWarnThresholdMs => Math.Max(1, _timingWarnThresholdMs?.Value ?? 250);

    private void Awake()
    {
        InitializeLoggingAndConfig();
        InitializeHarmony();
        PatchRuntimeBridges();
    }

    private void InitializeLoggingAndConfig()
    {
        _log = Logger;
        ConfigureDebugOptions();
    }

    private void ConfigureDebugOptions()
    {
        _enableTimingLogs = Config.Bind(ModInfo.CONFIG_SECTION_DEBUG, "EnableTimingLogs", false, "Log detailed keybind menu rebuild timings.");
        _timingWarnThresholdMs = Config.Bind(ModInfo.CONFIG_SECTION_DEBUG, "TimingWarnThresholdMs", 250, "Always log a warning when a timing block exceeds this many ms.");
    }

    private void InitializeHarmony()
    {
        _harmony = new Harmony(ModInfo.HARMONY_ID);
    }

    private void PatchRuntimeBridges()
    {
        TryPatchSettingsMenu();
        TryPatchHotkeyGate();
    }

    private void TryPatchSettingsMenu()
    {
        try
        {
            if (!TryResolveSettingsMenuPatchMethods(
                out var onKeybindsMethod,
                out var keybindsPostfix,
                out var onEnableMethod,
                out var onDisableMethod,
                out var onEnablePostfix,
                out var onDisablePostfix))
            {
                return;
            }

            var harmony = GetHarmonyOrWarn("keybind menu injection");
            if (harmony == null) return;

            PatchSettingsMenuLifecycle(
                harmony,
                onKeybindsMethod,
                keybindsPostfix,
                onEnableMethod,
                onDisableMethod,
                onEnablePostfix,
                onDisablePostfix);

            TryPatchSettingKeybindConflictBridge(harmony);
            Logger.LogInfo("Patched keybind menu injector.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize Rebind patches: {ex}");
        }
    }

    private bool TryResolveSettingsMenuPatchMethods(
        out MethodInfo? onKeybindsMethod,
        out MethodInfo? keybindsPostfix,
        out MethodInfo? onEnableMethod,
        out MethodInfo? onDisableMethod,
        out MethodInfo? onEnablePostfix,
        out MethodInfo? onDisablePostfix)
    {
        onKeybindsMethod = null;
        keybindsPostfix = null;
        onEnableMethod = null;
        onDisableMethod = null;
        onEnablePostfix = null;
        onDisablePostfix = null;

        var settingsMenuType = AccessTools.TypeByName("SettingsMenu");
        if (settingsMenuType == null)
        {
            Logger.LogWarning("Could not locate SettingsMenu type; keybind menu injection is unavailable.");
            return false;
        }

        onKeybindsMethod = AccessTools.Method(settingsMenuType, "OnKeybindsPagePressed");
        keybindsPostfix = AccessTools.Method(typeof(Rebind), nameof(OnKeybindsPagePressedPostfix));
        if (onKeybindsMethod == null || keybindsPostfix == null)
        {
            Logger.LogWarning("Could not patch SettingsMenu.OnKeybindsPagePressed; keybind menu injection is unavailable.");
            return false;
        }

        onEnableMethod = AccessTools.Method(settingsMenuType, "OnEnable");
        onDisableMethod = AccessTools.Method(settingsMenuType, "OnDisable");
        onEnablePostfix = AccessTools.Method(typeof(Rebind), nameof(OnSettingsMenuEnabledPostfix));
        onDisablePostfix = AccessTools.Method(typeof(Rebind), nameof(OnSettingsMenuDisabledPostfix));
        return true;
    }

    private Harmony? GetHarmonyOrWarn(string featureName)
    {
        var harmony = _harmony;
        if (harmony != null) return harmony;
        Logger.LogWarning($"Harmony instance is unavailable; {featureName} is unavailable.");
        return null;
    }

    private static void PatchSettingsMenuLifecycle(
        Harmony harmony,
        MethodInfo onKeybindsMethod,
        MethodInfo keybindsPostfix,
        MethodInfo? onEnableMethod,
        MethodInfo? onDisableMethod,
        MethodInfo? onEnablePostfix,
        MethodInfo? onDisablePostfix)
    {
        harmony.Patch(onKeybindsMethod, postfix: new HarmonyMethod(keybindsPostfix));
        if (onEnableMethod != null && onEnablePostfix != null)
        {
            harmony.Patch(onEnableMethod, postfix: new HarmonyMethod(onEnablePostfix));
        }

        if (onDisableMethod != null && onDisablePostfix != null)
        {
            harmony.Patch(onDisableMethod, postfix: new HarmonyMethod(onDisablePostfix));
        }
    }

    private void TryPatchSettingKeybindConflictBridge(Harmony harmony)
    {
        try
        {
            var settingKeybindType = AccessTools.TypeByName("SettingKeybind");
            if (settingKeybindType == null) return;

            var startRebindMethod = AccessTools.Method(settingKeybindType, "StartRebind");
            var updateUiMethod = AccessTools.Method(settingKeybindType, "UpdateUI");
            var onDisableMethod = AccessTools.Method(settingKeybindType, "OnDisable");

            var startRebindPrefix = AccessTools.Method(typeof(Rebind), nameof(OnSettingKeybindStartRebindPrefix));
            var updateUiPostfix = AccessTools.Method(typeof(Rebind), nameof(OnSettingKeybindUpdateUiPostfix));
            var onDisablePostfix = AccessTools.Method(typeof(Rebind), nameof(OnSettingKeybindOnDisablePostfix));

            if (startRebindMethod != null && startRebindPrefix != null)
            {
                harmony.Patch(startRebindMethod, prefix: new HarmonyMethod(startRebindPrefix));
            }

            if (updateUiMethod != null && updateUiPostfix != null)
            {
                harmony.Patch(updateUiMethod, postfix: new HarmonyMethod(updateUiPostfix));
            }

            if (onDisableMethod != null && onDisablePostfix != null)
            {
                harmony.Patch(onDisableMethod, postfix: new HarmonyMethod(onDisablePostfix));
            }

            Logger.LogInfo("Patched native keybind conflict bridge.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to patch native keybind conflict bridge: {ex.Message}");
        }
    }

    private void TryPatchHotkeyGate()
    {
        try
        {
            var hotkeyGateType = AccessTools.TypeByName("RBN.Modlib.Game.HotkeyGate");
            var isHotkeyInputEnabled = AccessTools.Method(hotkeyGateType, "IsHotkeyInputEnabled");
            var postfix = AccessTools.Method(typeof(Rebind), nameof(OnHotkeyGateIsHotkeyInputEnabledPostfix));
            if (isHotkeyInputEnabled == null || postfix == null) return;

            var harmony = GetHarmonyOrWarn("HotkeyGate suppression bridge");
            if (harmony == null) return;

            harmony.Patch(isHotkeyInputEnabled, postfix: new HarmonyMethod(postfix));
            Logger.LogInfo("Patched HotkeyGate suppression bridge.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to patch HotkeyGate bridge: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        try
        {
            _harmony?.UnpatchSelf();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to unpatch Rebind cleanly: {ex.Message}");
        }
    }

    private void Update()
    {
        if (RebindRegistry.ConsumeDirty())
        {
            RebindMenuInjector.RebuildAll();
        }
    }

    private void OnGUI()
    {
        RebindMenuInjector.DrawSearchControls();
        RebindMenuInjector.DrawConflictPrompt();
    }

    private static void OnKeybindsPagePressedPostfix(object __instance)
    {
        RebindMenuInjector.EnsureInjected(__instance);
    }

    private static void OnSettingsMenuEnabledPostfix(object __instance)
    {
        RebindMenuInjector.NotifySettingsMenuEnabled(__instance);
    }

    private static void OnSettingsMenuDisabledPostfix(object __instance)
    {
        RebindMenuInjector.NotifySettingsMenuDisabled(__instance);
    }

    private static bool OnSettingKeybindStartRebindPrefix(object __instance)
    {
        if (RebindMenuInjector.TryStartNativeRebindWithConflictPrompt(__instance))
        {
            return false;
        }

        RebindMenuInjector.NotifyNativeRebindStarted(__instance);
        return true;
    }

    private static void OnSettingKeybindUpdateUiPostfix(object __instance)
    {
        RebindMenuInjector.NotifyNativeKeybindUpdated(__instance);
    }

    private static void OnSettingKeybindOnDisablePostfix(object __instance)
    {
        RebindMenuInjector.NotifyNativeKeybindDisabled(__instance);
    }

    private static void OnHotkeyGateIsHotkeyInputEnabledPostfix(ref bool __result)
    {
        if (!__result) return;
        if (!RebindMenuInjector.ShouldSuppressExternalHotkeys()) return;
        __result = false;
    }

    internal static void LogTiming(string scope, long elapsedMs, bool detailed = false)
    {
        if (detailed && !TimingLogsEnabled) return;
        if (!TimingLogsEnabled && elapsedMs < TimingWarnThresholdMs) return;

        var line = $"{ModInfo.LOG_PREFIX} [Perf] {scope}: {elapsedMs}ms";
        if (elapsedMs >= TimingWarnThresholdMs)
        {
            Log.LogWarning(line);
            return;
        }

        Log.LogInfo(line);
    }
}
