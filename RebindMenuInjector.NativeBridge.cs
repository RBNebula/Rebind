namespace Rebind;

internal static partial class RebindMenuInjector
{
    private static class NativeKeybindBridge
    {
        private static readonly Type? KeybindManagerType = AccessTools.TypeByName("KeybindManager");
        private static readonly Type? KeybindActionType = AccessTools.TypeByName("KeybindAction");
        private static readonly Type? KeybindTokenTextType = AccessTools.TypeByName("KeybindTokenText");
        private static readonly PropertyInfo? ManagerInstanceProperty = AccessTools.Property(KeybindManagerType, "Instance");
        private static readonly MethodInfo? TryGetBindingMethod = AccessTools.Method(KeybindManagerType, "TryGetBinding", new[] { KeybindActionType!, typeof(InputAction).MakeByRefType(), typeof(int).MakeByRefType() });
        private static readonly MethodInfo? GetBindingTextByActionMethod = AccessTools.Method(KeybindManagerType, "GetBindingText", new[] { KeybindActionType! });
        private static readonly MethodInfo? RefreshTokenMethod = AccessTools.Method(KeybindTokenTextType, "Refresh");
        private static readonly object ConflictCacheSync = new();
        private static Dictionary<string, List<NativeConflictMatch>>? _nativeConflictsByToken;
        private static Dictionary<string, string>? _nativeTokenByActionName;

        private sealed class NativeConflictMatch
        {
            public string Label = string.Empty;
            public string DisplayToken = string.Empty;
        }

        public static void InvalidateConflictCache()
        {
            lock (ConflictCacheSync)
            {
                _nativeConflictsByToken = null;
                _nativeTokenByActionName = null;
            }
        }

        public static bool HasNativeConflict(string conflictToken, out string label)
        {
            label = string.Empty;
            if (string.IsNullOrEmpty(conflictToken)) return false;

            var lookup = GetOrBuildConflictLookup();
            if (!lookup.TryGetValue(conflictToken, out var matches)) return false;
            if (matches.Count == 0) return false;

            for (var i = 0; i < matches.Count; i++)
            {
                var candidate = matches[i].Label;
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                label = candidate;
                return !string.IsNullOrEmpty(label);
            }

            return false;
        }

        public static void GetNativeConflictLabels(string conflictToken, string expectedDisplayText, out List<string> labels)
        {
            labels = new List<string>();
            if (string.IsNullOrEmpty(conflictToken)) return;

            var expectedDisplayToken = string.IsNullOrWhiteSpace(expectedDisplayText)
                ? string.Empty
                : "disp:" + KeyDisplayFormatter.Normalize(expectedDisplayText);
            var lookup = GetOrBuildConflictLookup();
            if (!lookup.TryGetValue(conflictToken, out var matches)) return;

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!string.IsNullOrEmpty(expectedDisplayToken) &&
                    !string.Equals(match.DisplayToken, expectedDisplayToken, StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(match.Label)) continue;
                labels.Add(match.Label);
            }
        }

        public static string GetBindingDisplayText(InputActionReference? actionRef, int bindingIndex)
        {
            var action = ResolveRuntimeAction(actionRef);
            if (action == null) return "Unbound";
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return "Unbound";

            try
            {
                return action.GetBindingDisplayString(bindingIndex);
            }
            catch
            {
                return "Unbound";
            }
        }

        public static string? GetBindingPath(InputActionReference? actionRef, int bindingIndex)
        {
            var action = ResolveRuntimeAction(actionRef);
            if (action == null) return null;
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return null;
            var binding = action.bindings[bindingIndex];
            return !string.IsNullOrWhiteSpace(binding.overridePath) ? binding.overridePath : binding.path;
        }

        public static string? GetBaseBindingPath(InputAction action, int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return null;
            return action.bindings[bindingIndex].path;
        }

        public static bool IsUsingDefault(InputActionReference? actionRef, int bindingIndex)
        {
            var action = ResolveRuntimeAction(actionRef);
            if (action == null) return true;
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return true;
            return string.IsNullOrEmpty(action.bindings[bindingIndex].overridePath);
        }

        public static bool TryGetNativeTokenForAction(object actionEnum, out string token)
        {
            token = string.Empty;
            var actionName = actionEnum.ToString();
            if (string.IsNullOrWhiteSpace(actionName)) return false;

            var lookup = GetOrBuildActionTokenLookup();
            if (lookup.TryGetValue(actionName, out var cachedToken) && !string.IsNullOrEmpty(cachedToken))
            {
                token = cachedToken;
                return true;
            }

            var manager = GetManagerInstance();
            if (manager == null) return false;
            var pathToken = GetNativeBindingPathToken(actionEnum);
            if (!string.IsNullOrEmpty(pathToken))
            {
                token = pathToken!;
                return true;
            }

            if (GetBindingTextByActionMethod == null) return false;
            try
            {
                var display = GetBindingTextByActionMethod.Invoke(manager, new[] { actionEnum }) as string;
                if (string.IsNullOrWhiteSpace(display)) return false;
                if (string.Equals(display, "Unbound", StringComparison.OrdinalIgnoreCase)) return false;
                token = "disp:" + KeyDisplayFormatter.Normalize(display!);
                return !string.IsNullOrEmpty(token);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryResolveBindingForAction(object actionEnum, out InputAction action, out int bindingIndex)
        {
            action = null!;
            bindingIndex = -1;

            var manager = GetManagerInstance();
            if (manager == null) return false;
            if (TryGetBindingMethod == null) return false;

            try
            {
                var args = new object?[] { actionEnum, null, -1 };
                var okObj = TryGetBindingMethod.Invoke(manager, args);
                if (okObj is not bool ok || !ok) return false;
                if (args[1] is not InputAction resolvedAction) return false;
                if (args[2] is not int resolvedIndex) return false;
                if (resolvedIndex < 0 || resolvedIndex >= resolvedAction.bindings.Count) return false;

                action = resolvedAction;
                bindingIndex = resolvedIndex;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string? GetOverridePathForAction(object actionEnum)
        {
            if (!TryResolveBindingForAction(actionEnum, out var action, out var bindingIndex)) return null;
            return GetOverridePath(action, bindingIndex);
        }

        public static string GetBindingDisplayTextForAction(object actionEnum)
        {
            var manager = GetManagerInstance();
            if (manager != null && GetBindingTextByActionMethod != null)
            {
                try
                {
                    var display = GetBindingTextByActionMethod.Invoke(manager, new[] { actionEnum }) as string;
                    if (!string.IsNullOrWhiteSpace(display))
                    {
                        return display!;
                    }
                }
                catch
                {
                    // fall through to action-based lookup
                }
            }

            if (!TryResolveBindingForAction(actionEnum, out var action, out var bindingIndex)) return "Unbound";

            try
            {
                var display = action.GetBindingDisplayString(bindingIndex);
                return string.IsNullOrWhiteSpace(display) ? "Unbound" : display;
            }
            catch
            {
                return "Unbound";
            }
        }

        public static string GetConflictTokenForAction(object actionEnum)
        {
            if (TryGetNativeTokenForAction(actionEnum, out var token))
            {
                return token;
            }

            var display = GetBindingDisplayTextForAction(actionEnum);
            if (string.IsNullOrWhiteSpace(display)) return string.Empty;
            if (string.Equals(display, "Unbound", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(display, "-", StringComparison.Ordinal)) return string.Empty;
            return "disp:" + KeyDisplayFormatter.Normalize(display);
        }

        public static InputAction? ResolveRuntimeAction(InputActionReference? actionRef)
        {
            if (actionRef == null) return null;
            return actionRef.action;
        }

        public static string? GetOverridePath(InputAction action, int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return null;
            return action.bindings[bindingIndex].overridePath;
        }

        public static void SetOverridePath(InputAction action, int bindingIndex, string? overridePath)
        {
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;

            if (string.IsNullOrEmpty(overridePath))
            {
                action.RemoveBindingOverride(bindingIndex);
            }
            else
            {
                action.ApplyBindingOverride(bindingIndex, overridePath);
            }

            InvalidateConflictCache();
        }

        public static void RefreshTokenTexts()
        {
            if (KeybindTokenTextType == null || RefreshTokenMethod == null) return;
            try
            {
                var components = UnityEngine.Object.FindObjectsOfType(KeybindTokenTextType, includeInactive: true).OfType<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    RefreshTokenMethod.Invoke(comp, null);
                }
            }
            catch (Exception ex)
            {
                Rebind.Log.LogWarning($"Failed to refresh KeybindTokenText components: {ex.Message}");
            }
            finally
            {
                InvalidateConflictCache();
            }
        }

        private static Dictionary<string, List<NativeConflictMatch>> GetOrBuildConflictLookup()
        {
            lock (ConflictCacheSync)
            {
                if (_nativeConflictsByToken != null)
                {
                    return _nativeConflictsByToken;
                }

                var builtLookup = new Dictionary<string, List<NativeConflictMatch>>(StringComparer.Ordinal);
                var tokenByAction = new Dictionary<string, string>(StringComparer.Ordinal);
                if (KeybindActionType != null && GetBindingTextByActionMethod != null)
                {
                    var manager = GetManagerInstance();
                    if (manager != null)
                    {
                        var enumValues = Enum.GetValues(KeybindActionType);
                        for (var i = 0; i < enumValues.Length; i++)
                        {
                            var action = enumValues.GetValue(i);
                            if (action == null) continue;

                            string? display;
                            try
                            {
                                display = GetBindingTextByActionMethod.Invoke(manager, new[] { action }) as string;
                            }
                            catch
                            {
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(display)) continue;
                            if (string.Equals(display, "Unbound", StringComparison.OrdinalIgnoreCase)) continue;

                            var displayToken = "disp:" + KeyDisplayFormatter.Normalize(display!);
                            if (string.IsNullOrEmpty(displayToken)) continue;

                            var label = $"Game: {action}";
                            AddConflictMatch(builtLookup, displayToken, label, displayToken);

                            var pathToken = GetNativeBindingPathToken(action);
                            if (!string.IsNullOrEmpty(pathToken))
                            {
                                AddConflictMatch(builtLookup, pathToken!, label, displayToken);
                                tokenByAction[action.ToString() ?? string.Empty] = pathToken!;
                            }
                            else
                            {
                                tokenByAction[action.ToString() ?? string.Empty] = displayToken;
                            }
                        }
                    }
                }

                _nativeConflictsByToken = builtLookup;
                _nativeTokenByActionName = tokenByAction;
                return _nativeConflictsByToken;
            }
        }

        private static Dictionary<string, string> GetOrBuildActionTokenLookup()
        {
            lock (ConflictCacheSync)
            {
                if (_nativeTokenByActionName != null)
                {
                    return _nativeTokenByActionName;
                }
            }

            GetOrBuildConflictLookup();
            lock (ConflictCacheSync)
            {
                return _nativeTokenByActionName ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static void AddConflictMatch(
            Dictionary<string, List<NativeConflictMatch>> lookup,
            string token,
            string label,
            string displayToken)
        {
            if (string.IsNullOrEmpty(token)) return;
            if (string.IsNullOrWhiteSpace(label)) return;

            if (!lookup.TryGetValue(token, out var matches))
            {
                matches = new List<NativeConflictMatch>();
                lookup[token] = matches;
            }

            matches.Add(new NativeConflictMatch
            {
                Label = label,
                DisplayToken = displayToken ?? string.Empty
            });
        }

        private static object? GetManagerInstance()
        {
            if (ManagerInstanceProperty == null) return null;
            try
            {
                return ManagerInstanceProperty.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static string? GetNativeBindingPathToken(object actionEnum)
        {
            if (!TryResolveBindingForAction(actionEnum, out var action, out var index)) return null;

            var binding = action.bindings[index];
            var path = !string.IsNullOrWhiteSpace(binding.overridePath) ? binding.overridePath : binding.path;
            if (string.IsNullOrWhiteSpace(path)) return null;
            return KeyDisplayFormatter.ToConflictTokenFromBindingPath(path);
        }
    }
}

