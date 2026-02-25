namespace Rebind;

internal static partial class RebindMenuInjector
{
    private static class RebindConflictPrompt
    {
        private static UnityEngine.Object? _owner;
        private static string _title = string.Empty;
        private static string _message = string.Empty;
        private static Action? _onContinue;
        private static Action? _onReset;
        private static bool _visible;
        private static GameObject? _overlayRoot;
        private static RectTransform? _panelRect;
        private static TMP_Text? _titleText;
        private static TMP_Text? _messageText;
        private static Button? _continueButton;
        private static Button? _resetButton;

        private const float PanelWidth = 620f;
        private const float PanelHeight = 220f;
        private const float SidePadding = 20f;
        private const float TopPadding = 16f;
        private const float BottomPadding = 16f;
        private const float ButtonWidth = 140f;
        private const float ButtonHeight = 34f;
        private const float ButtonSpacing = 12f;
        private const float ButtonRowHeight = 40f;
        private static readonly Color PromptTextColor = Color.white;
        public static bool IsVisible => _visible && _owner != null;

        public static void Show(UnityEngine.Object owner, string title, string message, Action onContinue, Action onReset)
        {
            _owner = owner;
            _title = title ?? "Keybind Conflict";
            _message = message ?? string.Empty;
            _onContinue = onContinue;
            _onReset = onReset;
            _visible = true;
        }

        public static void ClearForOwner(UnityEngine.Object owner)
        {
            if (!_visible) return;
            if (_owner != owner) return;
            Clear();
        }

        public static void Draw()
        {
            if (!_visible)
            {
                SetOverlayVisible(false);
                return;
            }

            if (_owner == null)
            {
                Clear();
                return;
            }

            if (!EnsureOverlay())
            {
                return;
            }

            SetOverlayVisible(true);
            RefreshOverlayContent();
        }

        private static bool EnsureOverlay()
        {
            if (_overlayRoot != null && _panelRect != null && _titleText != null && _messageText != null && _continueButton != null && _resetButton != null)
            {
                _overlayRoot.transform.SetAsLastSibling();
                return true;
            }

            DestroyOverlay();
            if (!TryGetConflictPromptUiContext(out var context))
            {
                return false;
            }

            _overlayRoot = new GameObject("RebindConflictOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var overlayRect = _overlayRoot.GetComponent<RectTransform>();
            if (overlayRect == null)
            {
                DestroyOverlay();
                return false;
            }

            overlayRect.SetParent(context.Canvas.transform, worldPositionStays: false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.SetAsLastSibling();

            var overlayImage = _overlayRoot.GetComponent<Image>();
            if (overlayImage != null)
            {
                overlayImage.raycastTarget = true;
                overlayImage.color = new Color(0f, 0f, 0f, 0.45f);
            }

            _panelRect = CreatePanel(overlayRect, context);
            if (_panelRect == null)
            {
                DestroyOverlay();
                return false;
            }

            return _titleText != null && _messageText != null && _continueButton != null && _resetButton != null;
        }

        private static RectTransform? CreatePanel(RectTransform parent, ConflictPromptUiContext context)
        {
            var panelGo = new GameObject("PromptPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRect = panelGo.GetComponent<RectTransform>();
            if (panelRect == null) return null;

            panelRect.SetParent(parent, worldPositionStays: false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRect.anchoredPosition = Vector2.zero;

            var panelImage = panelGo.GetComponent<Image>();
            if (panelImage != null)
            {
                ApplyPanelVisualStyle(panelImage, context.ButtonTemplate);
            }

            _titleText = CreatePromptText("Title", panelRect, context.TextTemplate, 20f, TextAlignmentOptions.MidlineLeft, wordWrap: false);
            if (_titleText != null)
            {
                var rect = _titleText.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.offsetMin = new Vector2(SidePadding, -(TopPadding + 34f));
                rect.offsetMax = new Vector2(-SidePadding, -TopPadding);
            }

            _messageText = CreatePromptText("Message", panelRect, context.TextTemplate, 16f, TextAlignmentOptions.TopLeft, wordWrap: true);
            if (_messageText != null)
            {
                var rect = _messageText.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.offsetMin = new Vector2(SidePadding, BottomPadding + ButtonRowHeight + 12f);
                rect.offsetMax = new Vector2(-SidePadding, -(TopPadding + 40f));
            }

            var buttonsRowGo = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var buttonsRowRect = buttonsRowGo.GetComponent<RectTransform>();
            if (buttonsRowRect != null)
            {
                buttonsRowRect.SetParent(panelRect, worldPositionStays: false);
                buttonsRowRect.anchorMin = new Vector2(1f, 0f);
                buttonsRowRect.anchorMax = new Vector2(1f, 0f);
                buttonsRowRect.pivot = new Vector2(1f, 0f);
                buttonsRowRect.sizeDelta = new Vector2((ButtonWidth * 2f) + ButtonSpacing, ButtonRowHeight);
                buttonsRowRect.anchoredPosition = new Vector2(-SidePadding, BottomPadding);
            }

            var rowLayout = buttonsRowGo.GetComponent<HorizontalLayoutGroup>();
            if (rowLayout != null)
            {
                rowLayout.childAlignment = TextAnchor.MiddleRight;
                rowLayout.childControlWidth = false;
                rowLayout.childControlHeight = false;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = false;
                rowLayout.spacing = ButtonSpacing;
            }

            _continueButton = CreatePromptButton("ContinueButton", "Continue", buttonsRowGo.transform, context.ButtonTemplate, context.TextTemplate);
            if (_continueButton != null)
            {
                _continueButton.onClick = new Button.ButtonClickedEvent();
                _continueButton.onClick.AddListener(OnContinueClicked);
            }

            _resetButton = CreatePromptButton("ResetButton", "Reset", buttonsRowGo.transform, context.ButtonTemplate, context.TextTemplate);
            if (_resetButton != null)
            {
                _resetButton.onClick = new Button.ButtonClickedEvent();
                _resetButton.onClick.AddListener(OnResetClicked);
            }

            return panelRect;
        }

        private static void ApplyPanelVisualStyle(Image panelImage, Button? buttonTemplate)
        {
            if (panelImage == null) return;

            panelImage.raycastTarget = true;
            panelImage.color = new Color(0.30f, 0.33f, 0.38f, 0.97f);
            if (buttonTemplate == null || buttonTemplate.image == null) return;

            var templateImage = buttonTemplate.image;
            if (templateImage.sprite == null) return;

            panelImage.sprite = templateImage.sprite;
            panelImage.type = templateImage.type == Image.Type.Simple ? Image.Type.Sliced : templateImage.type;
            panelImage.material = templateImage.material;
            panelImage.pixelsPerUnitMultiplier = templateImage.pixelsPerUnitMultiplier;
        }

        private static TMP_Text? CreatePromptText(string name, RectTransform parent, TMP_Text? template, float fontSize, TextAlignmentOptions alignment, bool wordWrap)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) return null;

            rect.SetParent(parent, worldPositionStays: false);

            var text = go.GetComponent<TextMeshProUGUI>();
            if (text == null) return null;

            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
                text.lineSpacing = template.lineSpacing;
                text.characterSpacing = template.characterSpacing;
                text.wordSpacing = template.wordSpacing;
            }

            text.color = PromptTextColor;

            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.textWrappingMode = wordWrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button? CreatePromptButton(string name, string text, Transform parent, Button? templateButton, TMP_Text? textTemplate)
        {
            Button? button = null;
            GameObject? root = null;

            if (templateButton != null)
            {
                root = UnityEngine.Object.Instantiate(templateButton.gameObject, parent);
                button = root.GetComponent<Button>();
            }

            if (root == null)
            {
                root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                var rectTransform = root.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.SetParent(parent, worldPositionStays: false);
                }

                var image = root.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0.20f, 0.24f, 0.30f, 1f);
                }

                button = root.GetComponent<Button>();
            }

            if (root == null || button == null) return null;

            root.name = name;
            root.SetActive(true);

            var layout = root.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = root.AddComponent<LayoutElement>();
            }

            layout.minWidth = ButtonWidth;
            layout.preferredWidth = ButtonWidth;
            layout.minHeight = ButtonHeight;
            layout.preferredHeight = ButtonHeight;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            var label = root.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (label == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var labelRect = labelGo.GetComponent<RectTransform>();
                if (labelRect != null)
                {
                    labelRect.SetParent(root.transform, worldPositionStays: false);
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;
                }

                label = labelGo.GetComponent<TextMeshProUGUI>();
            }

            if (label != null)
            {
                if (textTemplate != null)
                {
                    label.font = textTemplate.font;
                    label.fontSharedMaterial = textTemplate.fontSharedMaterial;
                }

                label.color = PromptTextColor;
                label.fontSize = 16f;
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.text = text;
                label.raycastTarget = false;
            }

            return button;
        }

        private static void OnContinueClicked()
        {
            var action = _onContinue;
            Clear();
            action?.Invoke();
        }

        private static void OnResetClicked()
        {
            var action = _onReset;
            Clear();
            action?.Invoke();
        }

        private static void RefreshOverlayContent()
        {
            if (_titleText != null) _titleText.text = _title;
            if (_messageText != null) _messageText.text = _message;
            if (_overlayRoot != null) _overlayRoot.transform.SetAsLastSibling();
        }

        private static void SetOverlayVisible(bool visible)
        {
            if (_overlayRoot == null) return;
            if (_overlayRoot.activeSelf == visible) return;
            _overlayRoot.SetActive(visible);
        }

        private static void Clear()
        {
            _owner = null;
            _title = string.Empty;
            _message = string.Empty;
            _onContinue = null;
            _onReset = null;
            _visible = false;
            SetOverlayVisible(false);
        }

        private static void DestroyOverlay()
        {
            if (_overlayRoot != null)
            {
                UnityEngine.Object.Destroy(_overlayRoot);
            }

            _overlayRoot = null;
            _panelRect = null;
            _titleText = null;
            _messageText = null;
            _continueButton = null;
            _resetButton = null;
        }
    }
}
