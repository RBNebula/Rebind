namespace Rebind;

internal static partial class RebindMenuInjector
{
    private sealed class RebindRowController : MonoBehaviour
    {
        private static readonly KeyCode[] AllKeys = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>().ToArray();
        private static readonly HashSet<KeyCode> ModifierPrefixKeys = new()
        {
            KeyCode.LeftControl,
            KeyCode.RightControl,
            KeyCode.LeftShift,
            KeyCode.RightShift,
            KeyCode.LeftAlt,
            KeyCode.RightAlt
        };
        private const float ShortcutCommitDelaySeconds = 0.25f;
        private static RebindRowController? _activeCapture;
        private static readonly List<RebindRowController> AllRows = new();
        private static int _refreshBatchDepth;
        private static bool _refreshQueued;
        public static readonly Color ConflictColor = new(0.95f, 0.25f, 0.25f, 1f);

        private RebindEntry? _entry;
        private TMP_Text? _keybindLabel;
        private TMP_Text? _valueLabel;
        private Button? _valueButton;
        private Button? _resetButton;
        private GameObject? _showWhenNonDefault;
        private InputActionRebindingExtensions.RebindingOperation? _rebindOp;
        private Color _keybindDefaultColor = Color.white;
        private Color _valueDefaultColor = Color.white;
        private bool _shortcutCaptureStarted;
        private KeyCode _shortcutLeadingModifier = KeyCode.None;
        private float _shortcutLastKeyDownAt;
        private bool _searchVisible = true;
        private readonly List<KeyCode> _shortcutOrderedKeys = new();

        public void Initialize(
            RebindEntry entry,
            TMP_Text? keybindLabel,
            TMP_Text? valueLabel,
            Button? valueButton,
            Button? resetButton,
            GameObject? showWhenNonDefault)
        {
            _entry = entry;
            _keybindLabel = keybindLabel;
            _valueLabel = valueLabel;
            _valueButton = valueButton;
            _resetButton = resetButton;
            _showWhenNonDefault = showWhenNonDefault;

            if (_keybindLabel != null)
            {
                _keybindDefaultColor = _keybindLabel.color;
            }
            if (_valueLabel != null)
            {
                _valueDefaultColor = _valueLabel.color;
            }

            if (_keybindLabel != null)
            {
                _keybindLabel.text = _entry.KeybindTitle;
            }

            if (_valueButton != null)
            {
                _valueButton.onClick = new Button.ButtonClickedEvent();
                _valueButton.onClick.AddListener(BeginCapture);
            }

            if (_resetButton != null)
            {
                _resetButton.onClick = new Button.ButtonClickedEvent();
                _resetButton.onClick.AddListener(ResetToDefault);
            }

            AllRows.Add(this);
            RefreshAllConflictVisuals();
        }

        public static void CancelActiveCapture()
        {
            if (_activeCapture != null)
            {
                _activeCapture.StopCapture(restoreValue: true);
            }
        }

        public bool MatchesSearchFilter(SearchFilterQuery query)
        {
            if (_entry == null) return false;
            if (!query.HasText) return true;

            var searchByKey = query.SearchByKey;
            var searchByModTitle = query.SearchByModTitle;
            var searchByKeybindName = query.SearchByKeybindName;
            if (!query.AnyFieldSelected)
            {
                searchByKey = true;
                searchByModTitle = true;
                searchByKeybindName = true;
            }

            if (searchByModTitle && MatchesFilterField(_entry.SectionTitle, query.Text)) return true;
            if (searchByKeybindName && MatchesFilterField(_entry.KeybindTitle, query.Text)) return true;
            if (searchByKey && MatchesKeyFilter(GetCurrentDisplayText(), query.Text)) return true;
            return false;
        }

        public void SetSearchVisible(bool visible)
        {
            if (_searchVisible == visible && gameObject.activeSelf == visible) return;

            _searchVisible = visible;
            if (!visible && _activeCapture == this)
            {
                StopCapture(restoreValue: true);
            }

            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public static void BeginRefreshBatch()
        {
            _refreshBatchDepth++;
        }

        public static void EndRefreshBatch()
        {
            if (_refreshBatchDepth <= 0)
            {
                _refreshBatchDepth = 0;
                return;
            }

            _refreshBatchDepth--;
            if (_refreshBatchDepth == 0 && _refreshQueued)
            {
                _refreshQueued = false;
                RefreshAllConflictVisualsImmediate();
            }
        }

        private void OnDestroy()
        {
            if (_valueButton != null)
            {
                _valueButton.onClick.RemoveListener(BeginCapture);
            }

            if (_resetButton != null)
            {
                _resetButton.onClick.RemoveListener(ResetToDefault);
            }

            if (_activeCapture == this)
            {
                _activeCapture = null;
            }

            _rebindOp?.Dispose();
            _rebindOp = null;

            AllRows.Remove(this);
            RebindConflictPrompt.ClearForOwner(this);
            RefreshAllConflictVisuals();
        }

        private void Update()
        {
            if (_entry == null || _activeCapture != this) return;
            if (_entry.Mode == RebindEntryMode.InputAction) return;
            UpdateShortcutCapture();
        }

        private void UpdateShortcutCapture()
        {
            if (_entry == null) return;

            foreach (var key in AllKeys)
            {
                if (!Input.GetKeyDown(key)) continue;

                if (!_shortcutCaptureStarted)
                {
                    _shortcutCaptureStarted = true;
                    _shortcutOrderedKeys.Clear();
                    _shortcutOrderedKeys.Add(key);
                    _shortcutLastKeyDownAt = Time.unscaledTime;

                    if (!IsModifierPrefixKey(key))
                    {
                        var appliedSingle = TryApplyShortcutWithConflictCheck(new KeyboardShortcut(key));
                        StopCapture(restoreValue: !appliedSingle);
                        return;
                    }

                    if (HasHeldNonModifierBeforeStart(key))
                    {
                        ResetShortcutCaptureState();
                        continue;
                    }

                    _shortcutLeadingModifier = key;
                    UpdateShortcutCapturePreview();
                    continue;
                }

                if (!_shortcutOrderedKeys.Contains(key))
                {
                    _shortcutOrderedKeys.Add(key);
                }
                _shortcutLastKeyDownAt = Time.unscaledTime;
                UpdateShortcutCapturePreview();
            }

            if (!_shortcutCaptureStarted)
            {
                if (_valueLabel != null)
                {
                    _valueLabel.text = "Press key or Ctrl/Shift/Alt...";
                }
                return;
            }

            if (Input.GetKeyUp(_shortcutLeadingModifier))
            {
                CommitShortcutCaptureFromSequence();
                return;
            }

            if (!Input.GetKey(_shortcutLeadingModifier))
            {
                StopCapture(restoreValue: true);
                return;
            }

            if (!HasNonModifierInSequence()) return;
            if (Time.unscaledTime - _shortcutLastKeyDownAt < ShortcutCommitDelaySeconds) return;
            CommitShortcutCaptureFromSequence();
        }

        private void BeginCapture()
        {
            if (_entry == null) return;
            if (_entry.Mode == RebindEntryMode.InputAction)
            {
                BeginInputActionCapture();
                return;
            }

            BeginKeyboardCapture();
        }

        private void BeginKeyboardCapture()
        {
            if (_entry == null) return;
            if (_activeCapture != null && _activeCapture != this)
            {
                _activeCapture.StopCapture(restoreValue: true);
            }

            _activeCapture = this;
            ResetShortcutCaptureState();
            if (_valueLabel != null)
            {
                _valueLabel.text = "Press key or Ctrl/Shift/Alt...";
            }
        }

        private void StopCapture(bool restoreValue)
        {
            if (_activeCapture == this)
            {
                _activeCapture = null;
            }
            ResetShortcutCaptureState();

            if (restoreValue)
            {
                RefreshValueText();
            }
        }

        private void ResetToDefault()
        {
            if (_entry == null) return;
            if (_entry.Mode == RebindEntryMode.InputAction)
            {
                ResetInputActionToDefault();
                return;
            }

            ResetShortcutToDefault();
        }

        private void ResetInputActionToDefault()
        {
            if (_entry == null) return;

            var action = NativeKeybindBridge.ResolveRuntimeAction(_entry.ActionReference);
            if (action == null || _entry.BindingIndex < 0 || _entry.BindingIndex >= action.bindings.Count) return;

            var defaultPath = _entry.DefaultBindingPath;
            if (string.IsNullOrWhiteSpace(defaultPath))
            {
                NativeKeybindBridge.SetOverridePath(action, _entry.BindingIndex, null);
            }
            else
            {
                var basePath = NativeKeybindBridge.GetBaseBindingPath(action, _entry.BindingIndex) ?? string.Empty;
                var sameAsBase = string.Equals(
                    KeyDisplayFormatter.NormalizeBindingPath(defaultPath),
                    KeyDisplayFormatter.NormalizeBindingPath(basePath),
                    StringComparison.Ordinal);
                NativeKeybindBridge.SetOverridePath(action, _entry.BindingIndex, sameAsBase ? null : defaultPath);
            }

            ApplyInputActionChange();
        }

        private void ResetShortcutToDefault()
        {
            if (_entry == null) return;

            var appliedShortcut = TryApplyShortcutWithConflictCheck(_entry.DefaultShortcut);
            if (!appliedShortcut)
            {
                RefreshValueText();
            }
        }

        private void BeginInputActionCapture()
        {
            if (_entry == null) return;
            var action = NativeKeybindBridge.ResolveRuntimeAction(_entry.ActionReference);
            if (action == null) return;
            if (_entry.BindingIndex < 0 || _entry.BindingIndex >= action.bindings.Count) return;

            if (_activeCapture != null && _activeCapture != this)
            {
                _activeCapture.StopCapture(restoreValue: true);
            }

            _activeCapture = this;
            if (_valueLabel != null)
            {
                _valueLabel.text = "Press a button";
            }

            var oldOverridePath = NativeKeybindBridge.GetOverridePath(action, _entry.BindingIndex);

            action.Disable();
            _rebindOp?.Dispose();
            _rebindOp = action
                .PerformInteractiveRebinding(_entry.BindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .OnMatchWaitForAnother(0.1f)
                .OnCancel(op =>
                {
                    action.Enable();
                    op.Dispose();
                    _rebindOp = null;
                    StopCapture(restoreValue: true);
                })
                .OnComplete(op =>
                {
                    action.Enable();
                    op.Dispose();
                    _rebindOp = null;
                    StopCapture(restoreValue: false);
                    HandleCompletedInputActionRebind(action, _entry.BindingIndex, oldOverridePath);
                });

            _rebindOp.Start();
        }

        private void HandleCompletedInputActionRebind(InputAction action, int bindingIndex, string? oldOverridePath)
        {
            var display = GetCurrentDisplayText();
            var token = GetCurrentConflictToken();
            if (TryShowConflictPrompt(
                token,
                display,
                onContinue: ApplyInputActionChange,
                onReset: () =>
                {
                    NativeKeybindBridge.SetOverridePath(action, bindingIndex, oldOverridePath);
                    RefreshAllConflictVisuals();
                }))
            {
                return;
            }

            ApplyInputActionChange();
        }

        private bool TryApplyShortcutWithConflictCheck(KeyboardShortcut shortcut)
        {
            if (_entry == null) return false;

            var displayText = KeyDisplayFormatter.ToDisplayText(shortcut);
            var token = KeyDisplayFormatter.ToConflictToken(shortcut);
            return TryApplyWithConflictCheck(
                displayText,
                token,
                shouldCheckConflict: shortcut.MainKey != KeyCode.None,
                onApply: () => ApplyShortcutValue(shortcut));
        }

        private bool TryApplyWithConflictCheck(string displayText, string token, bool shouldCheckConflict, Action onApply)
        {
            if (_entry == null) return false;
            if (shouldCheckConflict &&
                TryShowConflictPrompt(
                    token,
                    displayText,
                    onContinue: onApply,
                    onReset: RefreshValueText))
            {
                return false;
            }

            onApply();
            return true;
        }

        private bool TryShowConflictPrompt(string token, string displayText, Action onContinue, Action onReset)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (IsUnboundDisplay(displayText)) return false;
            if (!TryGetConflictSummary(this, token, displayText, out var message)) return false;

            RebindConflictPrompt.Show(
                owner: this,
                title: "Keybind Conflict",
                message: message,
                onContinue: () =>
                {
                    if (this == null) return;
                    onContinue();
                },
                onReset: () =>
                {
                    if (this == null) return;
                    onReset();
                });
            RefreshAllConflictVisuals();
            return true;
        }

        private void ApplyShortcutValue(KeyboardShortcut shortcut)
        {
            if (_entry == null) return;

            ApplyWithRefresh(
                applyAction: () =>
                {
                    _entry.ShortcutSetter(shortcut);
                    InvokeChangedCallback();
                },
                keybindTitle: _entry.KeybindTitle);
        }

        private void ApplyWithRefresh(Action applyAction, string keybindTitle)
        {
            try
            {
                applyAction();
            }
            catch (Exception ex)
            {
                Rebind.Log.LogError($"Failed to apply keybind '{keybindTitle}': {ex}");
            }

            RefreshAfterBindingMutation();
        }

        private void ApplyInputActionChange()
        {
            InvokeChangedCallback();
            RefreshAfterBindingMutation();
        }

        private static void RefreshAfterBindingMutation()
        {
            NativeKeybindBridge.RefreshTokenTexts();
            RefreshAllConflictVisuals();
        }

        private void ResetShortcutCaptureState()
        {
            _shortcutCaptureStarted = false;
            _shortcutLeadingModifier = KeyCode.None;
            _shortcutLastKeyDownAt = 0f;
            _shortcutOrderedKeys.Clear();
        }

        private void UpdateShortcutCapturePreview()
        {
            if (_valueLabel == null) return;

            var shortcut = BuildShortcutFromCaptureSequence();
            if (shortcut.MainKey == KeyCode.None)
            {
                _valueLabel.text = "Press key or Ctrl/Shift/Alt...";
                return;
            }

            _valueLabel.text = KeyDisplayFormatter.ToDisplayText(shortcut);
        }

        private void CommitShortcutCaptureFromSequence()
        {
            var shortcut = BuildShortcutFromCaptureSequence();
            if (shortcut.MainKey == KeyCode.None)
            {
                StopCapture(restoreValue: true);
                return;
            }

            var appliedShortcut = TryApplyShortcutWithConflictCheck(shortcut);
            StopCapture(restoreValue: !appliedShortcut);
        }

        private KeyboardShortcut BuildShortcutFromCaptureSequence()
        {
            if (!_shortcutCaptureStarted) return KeyboardShortcut.Empty;
            if (_shortcutLeadingModifier == KeyCode.None) return KeyboardShortcut.Empty;

            var orderedKeys = new List<KeyCode> { _shortcutLeadingModifier };
            var seen = new HashSet<KeyCode> { _shortcutLeadingModifier };
            for (var i = 0; i < _shortcutOrderedKeys.Count; i++)
            {
                var key = _shortcutOrderedKeys[i];
                if (key == KeyCode.None) continue;
                if (!seen.Add(key)) continue;
                orderedKeys.Add(key);
            }

            if (orderedKeys.Count < 2) return KeyboardShortcut.Empty;
            var mainKey = KeyCode.None;
            for (var i = 0; i < orderedKeys.Count; i++)
            {
                var key = orderedKeys[i];
                if (IsModifierPrefixKey(key)) continue;
                mainKey = key;
            }

            if (mainKey == KeyCode.None) return KeyboardShortcut.Empty;

            var modifiers = new List<KeyCode>(orderedKeys.Count - 1);
            for (var i = 0; i < orderedKeys.Count; i++)
            {
                var key = orderedKeys[i];
                if (key == mainKey) continue;
                modifiers.Add(key);
            }

            return new KeyboardShortcut(mainKey, modifiers.ToArray());
        }

        private static bool IsModifierPrefixKey(KeyCode key)
        {
            return ModifierPrefixKeys.Contains(key);
        }

        private static bool HasHeldNonModifierBeforeStart(KeyCode initialModifierDown)
        {
            for (var i = 0; i < AllKeys.Length; i++)
            {
                var key = AllKeys[i];
                if (key == KeyCode.None) continue;
                if (key == initialModifierDown) continue;
                if (!Input.GetKey(key)) continue;
                if (IsModifierPrefixKey(key)) continue;
                return true;
            }

            return false;
        }

        private bool HasNonModifierInSequence()
        {
            for (var i = 0; i < _shortcutOrderedKeys.Count; i++)
            {
                var key = _shortcutOrderedKeys[i];
                if (key == KeyCode.None) continue;
                if (IsModifierPrefixKey(key)) continue;
                return true;
            }

            return false;
        }

        private void RefreshValueText()
        {
            if (_entry == null || _valueLabel == null) return;

            var display = GetCurrentDisplayText();
            var token = GetCurrentConflictToken();

            _valueLabel.text = display;
            var hasConflict = !string.IsNullOrEmpty(token) && !IsUnboundDisplay(display) && HasAnyConflict(this, token);
            if (_valueLabel != null) _valueLabel.color = hasConflict ? ConflictColor : _valueDefaultColor;
            if (_keybindLabel != null) _keybindLabel.color = hasConflict ? ConflictColor : _keybindDefaultColor;
            UpdateResetVisibility();
        }

        private static bool HasAnyConflict(RebindRowController source, string normalizedToken)
        {
            if (string.IsNullOrEmpty(normalizedToken)) return false;

            for (var i = 0; i < AllRows.Count; i++)
            {
                var row = AllRows[i];
                if (row == null || row == source || row._entry == null) continue;
                if (string.Equals(row.GetCurrentConflictToken(), normalizedToken, StringComparison.Ordinal)) return true;
            }

            return NativeKeybindBridge.HasNativeConflict(normalizedToken, out _);
        }

        private static bool TryGetConflictSummary(RebindRowController source, string normalizedToken, string keyText, out string message)
        {
            var matches = new List<string>();
            for (var i = 0; i < AllRows.Count; i++)
            {
                var row = AllRows[i];
                if (row == null || row == source || row._entry == null) continue;
                if (!string.Equals(row.GetCurrentConflictToken(), normalizedToken, StringComparison.Ordinal)) continue;
                matches.Add($"{row._entry.SectionTitle}: {row._entry.KeybindTitle}");
            }

            NativeKeybindBridge.GetNativeConflictLabels(normalizedToken, keyText, out var nativeMatches);

            if (matches.Count == 0 && nativeMatches.Count == 0)
            {
                message = string.Empty;
                return false;
            }

            var allTargets = new List<string>(matches.Count + nativeMatches.Count);
            allTargets.AddRange(matches);
            allTargets.AddRange(nativeMatches);

            var target = allTargets.Count == 1 ? allTargets[0] : $"{allTargets[0]} (+{allTargets.Count - 1} more)";
            message = $"{keyText} is already bound to '{target}'. Continue to keep duplicate binding?";
            return true;
        }

        private string GetCurrentDisplayText()
        {
            if (_entry == null) return "-";
            if (_entry.Mode == RebindEntryMode.InputAction)
            {
                var display = NativeKeybindBridge.GetBindingDisplayText(_entry.ActionReference, _entry.BindingIndex);
                return string.IsNullOrWhiteSpace(display) ? "-" : display;
            }

            return KeyDisplayFormatter.ToDisplayText(GetCurrentShortcutOrEmpty());
        }

        private string GetCurrentConflictToken()
        {
            if (_entry == null) return string.Empty;
            if (_entry.Mode == RebindEntryMode.InputAction)
            {
                var path = NativeKeybindBridge.GetBindingPath(_entry.ActionReference, _entry.BindingIndex);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return KeyDisplayFormatter.ToConflictTokenFromBindingPath(path!);
                }
            }

            var display = GetCurrentDisplayText();
            if (IsUnboundDisplay(display)) return string.Empty;
            return KeyDisplayFormatter.ToConflictToken(GetCurrentShortcutOrEmpty());
        }

        private static bool IsUnboundDisplay(string display)
        {
            return string.IsNullOrWhiteSpace(display) ||
                   string.Equals(display, "-", StringComparison.Ordinal) ||
                   string.Equals(display, "Unbound", StringComparison.OrdinalIgnoreCase);
        }

        private void InvokeChangedCallback()
        {
            if (_entry == null) return;

            if (_entry.Mode == RebindEntryMode.KeyboardShortcut)
            {
                _entry.OnShortcutChanged?.Invoke(GetCurrentShortcutOrEmpty());
            }

            _entry.OnChangedRaw?.Invoke();
        }

        private void UpdateResetVisibility()
        {
            if (_entry == null) return;

            var usingDefault = IsUsingDefault();
            if (_showWhenNonDefault != null)
            {
                _showWhenNonDefault.SetActive(!usingDefault);
            }
            else if (_resetButton != null)
            {
                _resetButton.gameObject.SetActive(!usingDefault);
            }
        }

        private bool IsUsingDefault()
        {
            if (_entry == null) return true;

            if (_entry.Mode == RebindEntryMode.KeyboardShortcut)
            {
                return GetCurrentShortcutOrEmpty().Equals(_entry.DefaultShortcut);
            }

            var effectivePath = NativeKeybindBridge.GetBindingPath(_entry.ActionReference, _entry.BindingIndex);
            if (string.IsNullOrWhiteSpace(effectivePath)) return true;

            if (!string.IsNullOrWhiteSpace(_entry.DefaultBindingPath))
            {
                return string.Equals(
                    KeyDisplayFormatter.NormalizeBindingPath(effectivePath!),
                    KeyDisplayFormatter.NormalizeBindingPath(_entry.DefaultBindingPath),
                    StringComparison.Ordinal);
            }

            return NativeKeybindBridge.IsUsingDefault(_entry.ActionReference, _entry.BindingIndex);
        }

        private KeyboardShortcut GetCurrentShortcutOrEmpty()
        {
            if (_entry == null) return KeyboardShortcut.Empty;
            try
            {
                return _entry.ShortcutGetter();
            }
            catch
            {
                return KeyboardShortcut.Empty;
            }
        }

        private static void RefreshAllConflictVisuals()
        {
            if (_refreshBatchDepth > 0)
            {
                _refreshQueued = true;
                return;
            }

            RefreshAllConflictVisualsImmediate();
        }

        private static void RefreshAllConflictVisualsImmediate()
        {
            var step = Stopwatch.StartNew();
            for (var i = 0; i < AllRows.Count; i++)
            {
                var row = AllRows[i];
                if (row == null) continue;
                row.RefreshValueText();
            }
            Rebind.LogTiming("Rebind.RefreshAll.ModRows", step.ElapsedMilliseconds, detailed: true);

            step.Restart();
            RebindMenuInjector.RefreshNativeConflictVisualsAll();
            Rebind.LogTiming("Rebind.RefreshAll.NativeRows", step.ElapsedMilliseconds, detailed: true);
        }

        public static HashSet<string> GetModConflictTokensSnapshot()
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < AllRows.Count; i++)
            {
                var row = AllRows[i];
                if (row == null || row._entry == null) continue;
                var token = row.GetCurrentConflictToken();
                if (string.IsNullOrEmpty(token)) continue;
                tokens.Add(token);
            }

            return tokens;
        }

        public static bool HasModConflictToken(string normalizedToken)
        {
            if (string.IsNullOrEmpty(normalizedToken)) return false;
            for (var i = 0; i < AllRows.Count; i++)
            {
                var row = AllRows[i];
                if (row == null || row._entry == null) continue;
                if (string.Equals(row.GetCurrentConflictToken(), normalizedToken, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static void AppendModConflictLabels(string normalizedToken, List<string> labels)
        {
            if (labels == null) return;
            if (string.IsNullOrEmpty(normalizedToken)) return;

            for (var i = 0; i < AllRows.Count; i++)
            {
                var row = AllRows[i];
                if (row == null || row._entry == null) continue;
                if (!string.Equals(row.GetCurrentConflictToken(), normalizedToken, StringComparison.Ordinal)) continue;
                labels.Add($"{row._entry.SectionTitle}: {row._entry.KeybindTitle}");
            }
        }

        public static void RefreshAllConflictVisualsFromExternalChange()
        {
            RefreshAllConflictVisuals();
        }

    }
}

