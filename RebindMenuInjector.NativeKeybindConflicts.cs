namespace Rebind;

internal static partial class RebindMenuInjector
{
    private static readonly FieldInfo? SettingKeybindRebindOpField = AccessTools.Field(SettingKeybindType, "_rebindOp");
    private static readonly MethodInfo? SettingKeybindUpdateUiMethod = AccessTools.Method(SettingKeybindType, "UpdateUI");
    private static readonly Type? KeybindManagerType = AccessTools.TypeByName("KeybindManager");
    private static readonly PropertyInfo? KeybindManagerInstanceProperty = AccessTools.Property(KeybindManagerType, "Instance");
    private static readonly PropertyInfo? KeybindManagerHasUnsavedChangesProperty = AccessTools.Property(KeybindManagerType, "HasUnsavedKeybindChanges");
    private static readonly FieldInfo? KeybindManagerHasUnsavedChangesField = AccessTools.Field(KeybindManagerType, "HasUnsavedKeybindChanges");

    private sealed class PendingNativeRebindContext
    {
        public object? ActionEnum;
        public string? PreviousOverridePath;
    }

    private static readonly object NativeRebindSync = new();
    private static readonly Dictionary<int, PendingNativeRebindContext> PendingNativeRebinds = new();
    private static readonly Dictionary<int, string> LastNativeConflictTokensBySettingId = new();

    public static bool TryStartNativeRebindWithConflictPrompt(object settingKeybindInstance)
    {
        if (!TryGetNativeSettingAction(settingKeybindInstance, out _, out var actionEnum)) return false;
        if (!NativeKeybindBridge.TryResolveBindingForAction(actionEnum, out var action, out var bindingIndex)) return false;

        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        var keybindLabel = KeybindValueLabelField?.GetValue(settingKeybindInstance) as TMP_Text;
        if (keybindLabel != null)
        {
            keybindLabel.text = "Press a button";
        }

        var oldOverridePath = NativeKeybindBridge.GetOverridePath(action, bindingIndex);
        action.Disable();

        DisposeNativeRebindOperation(settingKeybindInstance);
        var rebindOp = action
            .PerformInteractiveRebinding(bindingIndex)
            .WithCancelingThrough("<Keyboard>/escape")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .OnMatchWaitForAnother(0.1f)
            .OnCancel(op =>
            {
                action.Enable();
                op.Dispose();
                SetNativeRebindOperation(settingKeybindInstance, null);
            })
            .OnComplete(op =>
            {
                action.Enable();
                op.Dispose();
                SetNativeRebindOperation(settingKeybindInstance, null);
                OnNativeRebindCompleted(settingKeybindInstance, actionEnum, oldOverridePath);
            });

        SetNativeRebindOperation(settingKeybindInstance, rebindOp);
        rebindOp.Start();
        return true;
    }

    public static void NotifyNativeRebindStarted(object settingKeybindInstance)
    {
        if (!TryGetNativeSettingAction(settingKeybindInstance, out var settingId, out var actionEnum)) return;

        var pending = new PendingNativeRebindContext
        {
            ActionEnum = actionEnum,
            PreviousOverridePath = NativeKeybindBridge.GetOverridePathForAction(actionEnum)
        };

        lock (NativeRebindSync)
        {
            PendingNativeRebinds[settingId] = pending;
        }
    }

    public static void NotifyNativeKeybindUpdated(object settingKeybindInstance)
    {
        if (!TryGetNativeSettingAction(settingKeybindInstance, out var settingId, out var actionEnum)) return;

        var currentToken = NativeKeybindBridge.GetConflictTokenForAction(actionEnum);
        var currentDisplay = NativeKeybindBridge.GetBindingDisplayTextForAction(actionEnum);
        var pending = TryGetPendingNativeRebind(settingId);
        var tokenChanged = UpdateNativeConflictTokenCache(settingId, currentToken);

        if (pending == null)
        {
            if (tokenChanged)
            {
                RefreshConflictVisualsAfterNativeMutation();
            }
            return;
        }

        var currentOverridePath = NativeKeybindBridge.GetOverridePathForAction(actionEnum);
        if (!HasOverridePathChanged(pending.PreviousOverridePath, currentOverridePath))
        {
            if (tokenChanged)
            {
                RefreshConflictVisualsAfterNativeMutation();
            }
            return;
        }

        RemovePendingNativeRebind(settingId);

        if (!TryBuildNativeConflictPromptMessage(actionEnum, currentToken, currentDisplay, out var message))
        {
            RefreshConflictVisualsAfterNativeMutation();
            return;
        }

        if (settingKeybindInstance is not UnityEngine.Object owner || owner == null)
        {
            RefreshConflictVisualsAfterNativeMutation();
            return;
        }

        var previousOverride = pending.PreviousOverridePath;
        RebindConflictPrompt.Show(
            owner: owner,
            title: "Keybind Conflict",
            message: message,
            onContinue: RefreshConflictVisualsAfterNativeMutation,
            onReset: () =>
            {
                RestoreNativeBindingOverride(actionEnum, previousOverride);
                InvokeNativeSettingUpdateUi(settingKeybindInstance);
                RefreshConflictVisualsAfterNativeMutation();
            });

        RefreshConflictVisualsAfterNativeMutation();
    }

    public static void NotifyNativeKeybindDisabled(object settingKeybindInstance)
    {
        if (settingKeybindInstance is not UnityEngine.Object settingObject || settingObject == null) return;

        var settingId = settingObject.GetInstanceID();
        lock (NativeRebindSync)
        {
            PendingNativeRebinds.Remove(settingId);
            LastNativeConflictTokensBySettingId.Remove(settingId);
        }

        RebindConflictPrompt.ClearForOwner(settingObject);
    }

    private static void OnNativeRebindCompleted(object settingKeybindInstance, object actionEnum, string? previousOverridePath)
    {
        MarkNativeKeybindsDirty();
        InvokeNativeSettingUpdateUi(settingKeybindInstance);
        NativeKeybindBridge.InvalidateConflictCache();

        var currentToken = NativeKeybindBridge.GetConflictTokenForAction(actionEnum);
        var currentDisplay = NativeKeybindBridge.GetBindingDisplayTextForAction(actionEnum);

        if (!TryBuildNativeConflictPromptMessage(actionEnum, currentToken, currentDisplay, out var message))
        {
            RefreshConflictVisualsAfterNativeMutation();
            return;
        }

        if (settingKeybindInstance is not UnityEngine.Object owner || owner == null)
        {
            RefreshConflictVisualsAfterNativeMutation();
            return;
        }

        RebindConflictPrompt.Show(
            owner: owner,
            title: "Keybind Conflict",
            message: message,
            onContinue: RefreshConflictVisualsAfterNativeMutation,
            onReset: () =>
            {
                RestoreNativeBindingOverride(actionEnum, previousOverridePath);
                InvokeNativeSettingUpdateUi(settingKeybindInstance);
                RefreshConflictVisualsAfterNativeMutation();
            });

        RefreshConflictVisualsAfterNativeMutation();
    }

    private static bool TryGetNativeSettingAction(object settingKeybindInstance, out int settingId, out object actionEnum)
    {
        settingId = 0;
        actionEnum = null!;

        if (settingKeybindInstance is not UnityEngine.Object settingObject || settingObject == null) return false;
        if (SettingKeybindActionField == null) return false;

        var resolvedAction = SettingKeybindActionField.GetValue(settingKeybindInstance);
        if (resolvedAction == null) return false;

        settingId = settingObject.GetInstanceID();
        actionEnum = resolvedAction;
        return true;
    }

    private static PendingNativeRebindContext? TryGetPendingNativeRebind(int settingId)
    {
        lock (NativeRebindSync)
        {
            if (!PendingNativeRebinds.TryGetValue(settingId, out var pending)) return null;
            return pending;
        }
    }

    private static void RemovePendingNativeRebind(int settingId)
    {
        lock (NativeRebindSync)
        {
            PendingNativeRebinds.Remove(settingId);
        }
    }

    private static bool UpdateNativeConflictTokenCache(int settingId, string token)
    {
        lock (NativeRebindSync)
        {
            if (!LastNativeConflictTokensBySettingId.TryGetValue(settingId, out var previousToken))
            {
                LastNativeConflictTokensBySettingId[settingId] = token ?? string.Empty;
                return false;
            }

            var nextToken = token ?? string.Empty;
            LastNativeConflictTokensBySettingId[settingId] = nextToken;
            return !string.Equals(previousToken, nextToken, StringComparison.Ordinal);
        }
    }

    private static bool HasOverridePathChanged(string? previousOverridePath, string? currentOverridePath)
    {
        var previous = NormalizeOverridePathForCompare(previousOverridePath);
        var current = NormalizeOverridePathForCompare(currentOverridePath);
        return !string.Equals(previous, current, StringComparison.Ordinal);
    }

    private static string NormalizeOverridePathForCompare(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path!.Trim();
    }

    private static bool TryBuildNativeConflictPromptMessage(object actionEnum, string token, string displayText, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrEmpty(token)) return false;
        if (IsUnboundDisplay(displayText)) return false;

        var matches = new List<string>();
        RebindRowController.AppendModConflictLabels(token, matches);

        NativeKeybindBridge.GetNativeConflictLabels(token, displayText, out var nativeMatches);
        var ownLabel = "Game: " + actionEnum;
        for (var i = 0; i < nativeMatches.Count; i++)
        {
            var native = nativeMatches[i];
            if (string.Equals(native, ownLabel, StringComparison.Ordinal)) continue;
            matches.Add(native);
        }

        if (matches.Count == 0) return false;

        var target = matches.Count == 1
            ? matches[0]
            : $"{matches[0]} (+{matches.Count - 1} more)";
        message = $"{displayText} is already bound to '{target}'. Continue to keep duplicate binding?";
        return true;
    }

    private static bool IsUnboundDisplay(string display)
    {
        return string.IsNullOrWhiteSpace(display) ||
               string.Equals(display, "-", StringComparison.Ordinal) ||
               string.Equals(display, "Unbound", StringComparison.OrdinalIgnoreCase);
    }

    private static void RestoreNativeBindingOverride(object actionEnum, string? previousOverridePath)
    {
        if (!NativeKeybindBridge.TryResolveBindingForAction(actionEnum, out var action, out var bindingIndex)) return;
        NativeKeybindBridge.SetOverridePath(action, bindingIndex, previousOverridePath);
    }

    private static void InvokeNativeSettingUpdateUi(object settingKeybindInstance)
    {
        var updateUiMethod = SettingKeybindUpdateUiMethod ?? AccessTools.Method(settingKeybindInstance.GetType(), "UpdateUI");
        if (updateUiMethod == null) return;
        try
        {
            updateUiMethod.Invoke(settingKeybindInstance, null);
        }
        catch
        {
            // ignored; visuals are refreshed below regardless
        }
    }

    private static void RefreshConflictVisualsAfterNativeMutation()
    {
        NativeKeybindBridge.RefreshTokenTexts();
        RebindRowController.RefreshAllConflictVisualsFromExternalChange();
    }

    private static void DisposeNativeRebindOperation(object settingKeybindInstance)
    {
        if (SettingKeybindRebindOpField == null) return;
        try
        {
            var existing = SettingKeybindRebindOpField.GetValue(settingKeybindInstance) as InputActionRebindingExtensions.RebindingOperation;
            existing?.Dispose();
        }
        catch
        {
            // best effort only
        }
    }

    private static void SetNativeRebindOperation(object settingKeybindInstance, InputActionRebindingExtensions.RebindingOperation? operation)
    {
        if (SettingKeybindRebindOpField == null) return;
        try
        {
            SettingKeybindRebindOpField.SetValue(settingKeybindInstance, operation);
        }
        catch
        {
            // best effort only
        }
    }

    private static void MarkNativeKeybindsDirty()
    {
        var manager = GetKeybindManagerInstance();
        if (manager == null) return;

        try
        {
            if (KeybindManagerHasUnsavedChangesProperty != null && KeybindManagerHasUnsavedChangesProperty.CanWrite)
            {
                KeybindManagerHasUnsavedChangesProperty.SetValue(manager, true);
                return;
            }

            if (KeybindManagerHasUnsavedChangesField != null)
            {
                KeybindManagerHasUnsavedChangesField.SetValue(manager, true);
            }
        }
        catch
        {
            // best effort only
        }
    }

    private static object? GetKeybindManagerInstance()
    {
        if (KeybindManagerInstanceProperty == null) return null;
        try
        {
            return KeybindManagerInstanceProperty.GetValue(null);
        }
        catch
        {
            return null;
        }
    }
}
