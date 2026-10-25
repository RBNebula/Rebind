namespace Rebind;

internal static partial class RebindMenuInjector
{
    private sealed class InjectedMenuState
    {
        private static readonly Dictionary<string, (Color keybind, Color value)> NativeDefaultColorsByStableKey = new(StringComparer.Ordinal);
        private readonly GameObject _keybindPage;
        private readonly List<GameObject> _spawnedObjects = new();
        private readonly List<RebindRowController> _modRows = new();
        private Transform? _rowParent;
        private GameObject? _rowTemplate;
        private GameObject? _headerTemplate;
        private string _headerTemplateTextPath = string.Empty;
        private GameObject? _separatorTemplate;
        private Color _separatorColor = new(0.95f, 0.73f, 0.25f, 0.75f);
        private readonly List<NativeKeybindRowVisual> _nativeRows = new();
        private int _lastBuiltRegistryVersion = int.MinValue;
        private const float SeparatorHeight = 1f;

        public InjectedMenuState(GameObject keybindPage)
        {
            _keybindPage = keybindPage;
        }

        public bool IsAlive => _keybindPage != null;

        public bool TryGetKeybindPageScreenRect(out Rect rect)
        {
            rect = default;
            if (_keybindPage == null) return false;
            if (!_keybindPage.activeInHierarchy) return false;

            var rectTransform = _keybindPage.GetComponent<RectTransform>();
            if (rectTransform == null) return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            var canvas = _keybindPage.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            for (var i = 0; i < corners.Length; i++)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }

            if (minX >= maxX || minY >= maxY) return false;

            rect = new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
            return true;
        }

        public bool TryGetConflictPromptUiContext(out ConflictPromptUiContext context)
        {
            context = default;
            if (_keybindPage == null) return false;
            if (!_keybindPage.activeInHierarchy) return false;
            if (!ResolveTemplates()) return false;

            var canvas = _keybindPage.GetComponentInParent<Canvas>();
            if (canvas == null) return false;

            Button? buttonTemplate = null;
            TMP_Text? textTemplate = null;

            if (_rowTemplate != null)
            {
                buttonTemplate = _rowTemplate.GetComponentsInChildren<Button>(includeInactive: true).FirstOrDefault();
                textTemplate = _rowTemplate.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .OrderByDescending(t => t.fontSize)
                    .FirstOrDefault();
            }

            if (textTemplate == null)
            {
                textTemplate = _keybindPage.GetComponentsInChildren<TMP_Text>(includeInactive: true).FirstOrDefault();
            }

            context = new ConflictPromptUiContext(canvas, buttonTemplate, textTemplate);
            return true;
        }

        public void EnsureUpToDate(int registryVersion)
        {
            if (_lastBuiltRegistryVersion == registryVersion)
            {
                var refreshOnly = Stopwatch.StartNew();
                RefreshNativeConflictVisuals();
                Rebind.LogTiming("Rebind.RefreshOnly", refreshOnly.ElapsedMilliseconds, detailed: true);
                return;
            }

            Rebuild(registryVersion);
        }

        public void RebuildIfStale(int registryVersion)
        {
            if (_lastBuiltRegistryVersion == registryVersion) return;
            Rebuild(registryVersion);
        }

        private void Rebuild(int registryVersion)
        {
            var total = Stopwatch.StartNew();
            var succeeded = false;
            RebindRowController.BeginRefreshBatch();
            NativeKeybindBridge.InvalidateConflictCache();
            try
            {
                var step = Stopwatch.StartNew();
                ClearSpawned();
                Rebind.LogTiming("Rebind.ClearSpawned", step.ElapsedMilliseconds, detailed: true);

                step.Restart();
                if (!ResolveTemplates()) return;
                if (_rowParent == null || _rowTemplate == null) return;
                Rebind.LogTiming("Rebind.ResolveTemplates", step.ElapsedMilliseconds, detailed: true);

                step.Restart();
                var entries = RebindRegistry.Snapshot();
                Rebind.LogTiming("Rebind.Snapshot", step.ElapsedMilliseconds, detailed: true);
                if (entries.Count == 0)
                {
                    step.Restart();
                    RefreshNativeConflictVisuals();
                    Rebind.LogTiming("Rebind.RefreshNativeConflictVisuals(empty)", step.ElapsedMilliseconds, detailed: true);
                    succeeded = true;
                    return;
                }

                step.Restart();
                SpawnHeader("MODS");
                foreach (var group in entries.GroupBy(e => e.SectionTitle))
                {
                    SpawnHeader(group.Key);
                    foreach (var entry in group)
                    {
                        SpawnRow(entry);
                    }
                }
                Rebind.LogTiming("Rebind.SpawnRows", step.ElapsedMilliseconds, detailed: true);
                succeeded = true;
            }
            catch (Exception ex)
            {
                Rebind.Log.LogError($"Failed to rebuild injected keybind UI: {ex}");
            }
            finally
            {
                RebindRowController.EndRefreshBatch();
                total.Stop();
                Rebind.LogTiming("Rebind.RebuildTotal", total.ElapsedMilliseconds);
                if (succeeded)
                {
                    _lastBuiltRegistryVersion = registryVersion;
                    MarkSearchFilterDirty();
                }
            }
        }

        private bool ResolveTemplates()
        {
            if (_rowParent != null && _rowTemplate != null && _headerTemplate != null) return true;

            if (SettingKeybindType == null) return false;

            var keybindRows = _keybindPage
                .GetComponentsInChildren(SettingKeybindType, includeInactive: true)
                .OfType<Component>()
                .ToList();
            if (keybindRows.Count == 0) return false;

            var templateComponent = keybindRows[0];
            _rowTemplate = templateComponent.gameObject;
            _rowParent = _rowTemplate.transform.parent;
            var rowRoots = keybindRows.Select(r => r.transform).ToList();
            _headerTemplate = FindHeaderTemplateRoot(rowRoots, out var headerTextInTemplate);
            _headerTemplateTextPath = GetRelativePath(_headerTemplate?.transform, headerTextInTemplate?.transform) ?? string.Empty;
            _separatorTemplate = FindSeparatorTemplate(rowRoots);
            ResolveSeparatorStyle(rowRoots);
            RebuildNativeRowVisuals(keybindRows);

            return _rowParent != null && _rowTemplate != null && _headerTemplate != null;
        }

        private void RebuildNativeRowVisuals(List<Component> keybindRows)
        {
            var existingDefaultsByRowId = new Dictionary<int, (Color keybind, Color value)>();
            var existingDefaultsByStableKey = new Dictionary<string, (Color keybind, Color value)>(StringComparer.Ordinal);
            for (var i = 0; i < _nativeRows.Count; i++)
            {
                var existing = _nativeRows[i];
                var row = existing.Row;
                if (row == null) continue;
                existingDefaultsByRowId[row.GetInstanceID()] = (existing.KeybindDefaultColor, existing.ValueDefaultColor);
                var stableKey = existing.GetStableKey();
                if (!string.IsNullOrWhiteSpace(stableKey))
                {
                    existingDefaultsByStableKey[stableKey] = (existing.KeybindDefaultColor, existing.ValueDefaultColor);
                }
            }

            _nativeRows.Clear();
            foreach (var row in keybindRows)
            {
                if (row == null) continue;
                if (row.gameObject.name.StartsWith("RebindRow_", StringComparison.Ordinal)) continue;

                var setting = row as Behaviour;
                if (setting == null) continue;

                var keybindLabel = BaseLabelField?.GetValue(setting) as TMP_Text;
                var valueLabel = KeybindValueLabelField?.GetValue(setting) as TMP_Text;
                var action = SettingKeybindActionField?.GetValue(setting);
                var sectionTitle = ResolveNativeRowSectionTitle(setting.transform);
                var keybindTitle = keybindLabel != null ? keybindLabel.text?.Trim() ?? string.Empty : string.Empty;
                var stableKey = BuildNativeRowStableKey(action, sectionTitle, keybindTitle);
                var rowId = setting.GetInstanceID();
                var keybindDefaultColor = keybindLabel != null ? keybindLabel.color : Color.white;
                var valueDefaultColor = valueLabel != null ? valueLabel.color : Color.white;
                if (existingDefaultsByRowId.TryGetValue(rowId, out var preservedDefaults))
                {
                    keybindDefaultColor = preservedDefaults.keybind;
                    valueDefaultColor = preservedDefaults.value;
                }
                else if (!string.IsNullOrWhiteSpace(stableKey))
                {
                    if (existingDefaultsByStableKey.TryGetValue(stableKey, out preservedDefaults) ||
                        NativeDefaultColorsByStableKey.TryGetValue(stableKey, out preservedDefaults))
                    {
                        keybindDefaultColor = preservedDefaults.keybind;
                        valueDefaultColor = preservedDefaults.value;
                    }
                    else
                    {
                        if (keybindLabel != null && ColorsMatch(keybindLabel.color, RebindRowController.ConflictColor))
                        {
                            keybindDefaultColor = Color.white;
                        }

                        if (valueLabel != null && ColorsMatch(valueLabel.color, RebindRowController.ConflictColor))
                        {
                            valueDefaultColor = Color.white;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(stableKey) &&
                    !ColorsMatch(keybindDefaultColor, RebindRowController.ConflictColor) &&
                    !ColorsMatch(valueDefaultColor, RebindRowController.ConflictColor))
                {
                    NativeDefaultColorsByStableKey[stableKey] = (keybindDefaultColor, valueDefaultColor);
                }

                _nativeRows.Add(new NativeKeybindRowVisual
                {
                    Row = setting,
                    ActionEnum = action,
                    SectionTitle = sectionTitle,
                    KeybindTitle = keybindTitle,
                    KeybindLabel = keybindLabel,
                    ValueLabel = valueLabel,
                    KeybindDefaultColor = keybindDefaultColor,
                    ValueDefaultColor = valueDefaultColor
                });
            }
        }

        private static string BuildNativeRowStableKey(object? actionEnum, string sectionTitle, string keybindTitle)
        {
            var actionName = actionEnum?.ToString();
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                return "action:" + actionName.Trim();
            }

            var section = sectionTitle?.Trim() ?? string.Empty;
            var keybind = keybindTitle?.Trim() ?? string.Empty;
            return $"label:{section}|{keybind}";
        }

        private static bool ColorsMatch(Color left, Color right)
        {
            return Mathf.Approximately(left.r, right.r) &&
                   Mathf.Approximately(left.g, right.g) &&
                   Mathf.Approximately(left.b, right.b) &&
                   Mathf.Approximately(left.a, right.a);
        }

        private string ResolveNativeRowSectionTitle(Transform rowTransform)
        {
            if (_rowParent == null) return string.Empty;

            var rowSiblingIndex = rowTransform.GetSiblingIndex();
            for (var i = rowSiblingIndex - 1; i >= 0; i--)
            {
                var sibling = _rowParent.GetChild(i);
                if (sibling == null) continue;
                if (SettingKeybindType != null && sibling.GetComponent(SettingKeybindType) != null) continue;

                var headerText = sibling.GetComponentInChildren<TMP_Text>(includeInactive: true);
                if (headerText == null) continue;

                var title = headerText.text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title)) continue;
                return title;
            }

            return string.Empty;
        }

        private GameObject? FindHeaderTemplateRoot(List<Transform> rowRoots, out TMP_Text? headerText)
        {
            headerText = null;
            var texts = _keybindPage.GetComponentsInChildren<TMP_Text>(includeInactive: true);
            TMP_Text? movement = null;
            TMP_Text? firstCandidate = null;
            foreach (var text in texts)
            {
                if (text == null) continue;
                if (string.IsNullOrWhiteSpace(text.text)) continue;
                if (IsDescendantOfAny(text.transform, rowRoots)) continue;

                firstCandidate ??= text;
                if (string.Equals(text.text.Trim(), "Movement", StringComparison.OrdinalIgnoreCase))
                {
                    movement = text;
                    break;
                }
            }

            headerText = movement ?? firstCandidate;
            if (headerText == null) return null;
            return GetDirectChildOfRowParent(headerText.transform)?.gameObject ?? headerText.gameObject;
        }

        private GameObject? FindSeparatorTemplate(List<Transform> rowRoots)
        {
            if (_rowParent == null) return null;

            var images = _keybindPage.GetComponentsInChildren<Image>(includeInactive: true);
            foreach (var image in images)
            {
                if (!IsSeparatorStyleCandidate(image, rowRoots)) continue;

                var candidateRoot = GetDirectChildOfRowParent(image.transform);
                if (!IsUsableSeparatorTemplateRoot(candidateRoot)) continue;
                return candidateRoot!.gameObject;
            }

            return null;
        }

        private void ResolveSeparatorStyle(List<Transform> rowRoots)
        {
            var images = _keybindPage.GetComponentsInChildren<Image>(includeInactive: true);
            foreach (var image in images)
            {
                if (!IsSeparatorStyleCandidate(image, rowRoots)) continue;
                _separatorColor = image.color;
                return;
            }
        }

        private bool IsUsableSeparatorTemplateRoot(Transform? candidateRoot)
        {
            if (candidateRoot == null) return false;
            if (_rowTemplate != null && candidateRoot == _rowTemplate.transform) return false;
            if (_headerTemplate != null && candidateRoot == _headerTemplate.transform) return false;
            if (candidateRoot.GetComponentInChildren<TMP_Text>(includeInactive: true) != null) return false;
            return true;
        }

        private static bool IsSeparatorStyleCandidate(Image? image, List<Transform> rowRoots)
        {
            if (image == null) return false;
            if (IsDescendantOfAny(image.transform, rowRoots)) return false;
            return IsSeparatorShape(image.rectTransform.rect);
        }

        private static bool IsSeparatorShape(Rect rect)
        {
            return rect.height > 0f && rect.height <= 6f && rect.width >= 40f;
        }

        private static bool IsDescendantOfAny(Transform child, List<Transform> roots)
        {
            foreach (var root in roots)
            {
                if (child.IsChildOf(root)) return true;
            }

            return false;
        }

        private Transform? GetDirectChildOfRowParent(Transform target)
        {
            if (_rowParent == null) return null;
            var current = target;
            while (current.parent != null && current.parent != _rowParent)
            {
                current = current.parent;
            }

            return current.parent == _rowParent ? current : null;
        }

        private static string? GetRelativePath(Transform? root, Transform? target)
        {
            if (root == null || target == null) return null;
            if (target == root) return string.Empty;
            if (!target.IsChildOf(root)) return null;

            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private void SpawnHeader(string title)
        {
            if (_rowParent == null || _headerTemplate == null) return;

            var header = UnityEngine.Object.Instantiate(_headerTemplate, _rowParent);
            header.name = $"RebindHeader_{title}";
            header.transform.SetAsLastSibling();
            header.SetActive(true);

            var text = ResolveHeaderText(header);
            if (text != null)
            {
                text.text = title;
            }

            _spawnedObjects.Add(header);
        }

        private void SpawnSeparator()
        {
            if (_rowParent == null) return;

            if (_separatorTemplate != null)
            {
                var separatorClone = UnityEngine.Object.Instantiate(_separatorTemplate, _rowParent);
                separatorClone.name = "RebindSeparator";
                separatorClone.transform.SetAsLastSibling();
                separatorClone.SetActive(true);
                _spawnedObjects.Add(separatorClone);
                return;
            }

            var separator = new GameObject("RebindSeparator", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            separator.transform.SetParent(_rowParent, worldPositionStays: false);
            separator.transform.localScale = Vector3.one;
            var separatorRect = separator.GetComponent<RectTransform>();
            if (separatorRect != null)
            {
                separatorRect.sizeDelta = new Vector2(0f, SeparatorHeight);
            }
            var layout = separator.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minHeight = SeparatorHeight;
                layout.preferredHeight = SeparatorHeight;
                layout.flexibleHeight = 0f;
            }

            var image = separator.GetComponent<Image>();
            if (image != null)
            {
                image.color = _separatorColor;
            }

            separator.name = "RebindSeparator";
            separator.transform.SetAsLastSibling();
            separator.SetActive(true);
            _spawnedObjects.Add(separator);
        }

        private TMP_Text? ResolveHeaderText(GameObject headerRoot)
        {
            if (headerRoot == null) return null;

            if (!string.IsNullOrWhiteSpace(_headerTemplateTextPath))
            {
                var byPath = headerRoot.transform.Find(_headerTemplateTextPath);
                if (byPath != null)
                {
                    var textAtPath = byPath.GetComponent<TMP_Text>();
                    if (textAtPath != null) return textAtPath;
                }
            }

            return headerRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true).FirstOrDefault();
        }

        private void SpawnRow(RebindEntry entry)
        {
            if (_rowParent == null || _rowTemplate == null || SettingKeybindType == null) return;

            var row = UnityEngine.Object.Instantiate(_rowTemplate, _rowParent);
            row.name = $"RebindRow_{entry.SectionTitle}_{entry.KeybindTitle}";
            row.transform.SetAsLastSibling();
            row.SetActive(true);

            var settingKeybind = row.GetComponent(SettingKeybindType) as Behaviour;
            GameObject? showWhenNonDefault = null;
            if (settingKeybind != null)
            {
                showWhenNonDefault = HideDefaultBindField?.GetValue(settingKeybind) as GameObject;
                settingKeybind.enabled = false;
            }

            var keybindLabel = ResolveLeftLabel(row, settingKeybind);
            var valueLabel = ResolveValueLabel(row, settingKeybind);
            var buttons = row.GetComponentsInChildren<Button>(includeInactive: true).ToList();

            var valueButton = FindValueButton(buttons, valueLabel);
            var resetButton = FindResetButton(buttons, valueButton);

            var controller = row.AddComponent<RebindRowController>();
            controller.Initialize(entry, keybindLabel, valueLabel, valueButton, resetButton, showWhenNonDefault);

            _modRows.Add(controller);
            _spawnedObjects.Add(row);
        }

        private static TMP_Text? ResolveLeftLabel(GameObject row, Behaviour? settingKeybind)
        {
            if (settingKeybind != null && BaseLabelField != null)
            {
                var fromField = BaseLabelField.GetValue(settingKeybind) as TMP_Text;
                if (fromField != null) return fromField;
            }

            return row
                .GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .OrderBy(t => t.transform.GetSiblingIndex())
                .FirstOrDefault();
        }

        private static TMP_Text? ResolveValueLabel(GameObject row, Behaviour? settingKeybind)
        {
            if (settingKeybind != null && KeybindValueLabelField != null)
            {
                var fromField = KeybindValueLabelField.GetValue(settingKeybind) as TMP_Text;
                if (fromField != null) return fromField;
            }

            return row
                .GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .OrderByDescending(t => t.fontSize)
                .FirstOrDefault();
        }

        private static Button? FindValueButton(List<Button> buttons, TMP_Text? valueLabel)
        {
            if (buttons.Count == 0) return null;
            if (valueLabel == null) return buttons[0];

            foreach (var button in buttons)
            {
                if (valueLabel.transform.IsChildOf(button.transform))
                {
                    return button;
                }
            }

            return buttons[0];
        }

        private static Button? FindResetButton(List<Button> buttons, Button? valueButton)
        {
            return buttons.FirstOrDefault(b => b != valueButton);
        }

        private void ClearSpawned()
        {
            RebindRowController.CancelActiveCapture();
            _modRows.Clear();

            for (var i = _spawnedObjects.Count - 1; i >= 0; i--)
            {
                var go = _spawnedObjects[i];
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _spawnedObjects.Clear();
        }

        public void RefreshNativeConflictVisuals()
        {
            if (_nativeRows.Count == 0) return;

            var step = Stopwatch.StartNew();
            var modTokens = RebindRowController.GetModConflictTokensSnapshot();
            Rebind.LogTiming("Rebind.Native.ModTokenSnapshot", step.ElapsedMilliseconds, detailed: true);

            step.Restart();
            var tokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowTokens = new string[_nativeRows.Count];
            for (var i = 0; i < _nativeRows.Count; i++)
            {
                var row = _nativeRows[i];
                if (!row.IsAlive) continue;
                var token = row.GetConflictToken();
                rowTokens[i] = token;
                if (string.IsNullOrEmpty(token)) continue;

                if (tokenCounts.TryGetValue(token, out var count))
                {
                    tokenCounts[token] = count + 1;
                }
                else
                {
                    tokenCounts[token] = 1;
                }
            }
            Rebind.LogTiming("Rebind.Native.TokenScan", step.ElapsedMilliseconds, detailed: true);

            step.Restart();
            for (var i = 0; i < _nativeRows.Count; i++)
            {
                var row = _nativeRows[i];
                if (!row.IsAlive) continue;
                var token = rowTokens[i];
                var hasConflict = false;
                if (!string.IsNullOrEmpty(token))
                {
                    var nativeCount = tokenCounts.TryGetValue(token, out var count) ? count : 0;
                    hasConflict = nativeCount > 1 || modTokens.Contains(token);
                }

                row.ApplyConflictColor(hasConflict, RebindRowController.ConflictColor);
            }
            Rebind.LogTiming("Rebind.Native.ApplyColors", step.ElapsedMilliseconds, detailed: true);
        }

        public void ApplySearchFilter(SearchFilterQuery query)
        {
            ApplyNativeRowSearchFilter(query);
            ApplyModRowSearchFilter(query);
        }

        private void ApplyNativeRowSearchFilter(SearchFilterQuery query)
        {
            for (var i = 0; i < _nativeRows.Count; i++)
            {
                var row = _nativeRows[i];
                if (!row.IsAlive) continue;

                var visible = row.MatchesSearchFilter(query);
                var gameObject = row.Row?.gameObject;
                if (gameObject != null && gameObject.activeSelf != visible)
                {
                    gameObject.SetActive(visible);
                }
            }
        }

        private void ApplyModRowSearchFilter(SearchFilterQuery query)
        {
            for (var i = 0; i < _modRows.Count; i++)
            {
                var row = _modRows[i];
                if (row == null) continue;

                var visible = row.MatchesSearchFilter(query);
                row.SetSearchVisible(visible);
            }
        }
    }

    private sealed class NativeKeybindRowVisual
    {
        public Behaviour? Row;
        public object? ActionEnum;
        public string SectionTitle = string.Empty;
        public string KeybindTitle = string.Empty;
        public TMP_Text? KeybindLabel;
        public TMP_Text? ValueLabel;
        public Color KeybindDefaultColor = Color.white;
        public Color ValueDefaultColor = Color.white;

        public bool IsAlive => Row != null && Row.gameObject != null;

        public string GetConflictToken()
        {
            if (ActionEnum != null && NativeKeybindBridge.TryGetNativeTokenForAction(ActionEnum, out var token))
            {
                return token;
            }

            if (ValueLabel == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(ValueLabel.text)) return string.Empty;
            if (string.Equals(ValueLabel.text, "Unbound", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(ValueLabel.text, "-", StringComparison.Ordinal)) return string.Empty;
            return "disp:" + KeyDisplayFormatter.Normalize(ValueLabel.text);
        }

        public void ApplyConflictColor(bool hasConflict, Color conflictColor)
        {
            if (KeybindLabel != null) KeybindLabel.color = hasConflict ? conflictColor : KeybindDefaultColor;
            if (ValueLabel != null) ValueLabel.color = hasConflict ? conflictColor : ValueDefaultColor;
        }

        public string GetStableKey()
        {
            var actionName = ActionEnum?.ToString();
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                return "action:" + actionName.Trim();
            }

            var section = SectionTitle?.Trim() ?? string.Empty;
            var keybind = KeybindTitle?.Trim() ?? string.Empty;
            return $"label:{section}|{keybind}";
        }

        public bool MatchesSearchFilter(SearchFilterQuery query)
        {
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

            if (searchByModTitle && MatchesFilterField(SectionTitle, query.Text)) return true;
            if (searchByKeybindName && MatchesFilterField(KeybindTitle, query.Text)) return true;
            if (searchByKey && ValueLabel != null && MatchesKeyFilter(ValueLabel.text, query.Text)) return true;

            return false;
        }
    }
}

