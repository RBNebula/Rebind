namespace Rebind;

internal static partial class RebindMenuInjector
{
    private readonly struct SearchFilterQuery
    {
        public SearchFilterQuery(string text, bool searchByKey, bool searchByModTitle, bool searchByKeybindName)
        {
            Text = text;
            SearchByKey = searchByKey;
            SearchByModTitle = searchByModTitle;
            SearchByKeybindName = searchByKeybindName;
        }

        public string Text { get; }
        public bool SearchByKey { get; }
        public bool SearchByModTitle { get; }
        public bool SearchByKeybindName { get; }
        public bool HasText => !string.IsNullOrWhiteSpace(Text);
        public bool AnyFieldSelected => SearchByKey || SearchByModTitle || SearchByKeybindName;
    }

    private static string _searchText = string.Empty;
    private static bool _searchByKey = true;
    private static bool _searchByModTitle = true;
    private static bool _searchByKeybindName = true;
    private static bool _searchFilterDirty = true;

    private static GUIStyle? _searchPanelStyle;
    private static GUIStyle? _searchTextFieldStyle;
    private static GUIStyle? _searchToggleLabelStyle;
    private static GUIStyle? _searchToggleTickStyle;
    private static GUIStyle? _searchPlaceholderStyle;
    private static Texture2D? _searchPanelTexture;
    private static Texture2D? _searchFieldTexture;
    private static Texture2D? _searchToggleOffTexture;
    private static Texture2D? _searchToggleOnTexture;

    private const float SearchPanelHeight = 60f;
    private const float SearchPanelBottomViewportMargin = 8f;
    private const float SearchPanelBottomMenuMargin = 8f;
    private const float SearchPanelTopMinMargin = 8f;
    private const float SearchPanelSideMargin = 16f;
    private const float SearchPanelInnerPadding = 10f;
    private const float KeyToggleWidth = 96f;
    private const float ModTitleToggleWidth = 122f;
    private const float KeybindNameToggleWidth = 156f;
    private const float ToggleHeight = 30f;
    private const float ToggleBoxSize = 22f;
    private const float ToggleLabelGap = 8f;
    private const float FieldHeight = 34f;
    private const float ToggleSpacing = 6f;
    private const float SearchGap = 10f;

    public static void DrawSearchControls()
    {
        if (!TryGetActiveKeybindPageScreenRect(out var keybindRect)) return;

        EnsureSearchStyles();

        var width = Mathf.Max(460f, keybindRect.width - (SearchPanelSideMargin * 2f));
        var x = keybindRect.x + ((keybindRect.width - width) * 0.5f);
        var viewportBottomY = Screen.height - SearchPanelHeight - SearchPanelBottomViewportMargin;
        var menuBottomY = keybindRect.yMax - SearchPanelHeight - SearchPanelBottomMenuMargin;
        var y = Mathf.Max(viewportBottomY, menuBottomY);
        var minY = keybindRect.y + SearchPanelTopMinMargin;
        var maxY = Screen.height - SearchPanelHeight - 2f;
        y = Mathf.Clamp(y, minY, maxY);
        var panelRect = new Rect(x, y, width, SearchPanelHeight);

        GUI.Box(panelRect, GUIContent.none, _searchPanelStyle ?? GUI.skin.box);

        var toggleTotalWidth = KeyToggleWidth + ModTitleToggleWidth + KeybindNameToggleWidth + (ToggleSpacing * 2f);
        var fieldX = panelRect.x + SearchPanelInnerPadding;
        var fieldY = panelRect.y + ((SearchPanelHeight - FieldHeight) * 0.5f);
        var fieldWidth = panelRect.width - toggleTotalWidth - (SearchPanelInnerPadding * 2f) - SearchGap;
        fieldWidth = Mathf.Max(120f, fieldWidth);
        var fieldRect = new Rect(fieldX, fieldY, fieldWidth, FieldHeight);

        var nextSearchText = GUI.TextField(fieldRect, _searchText, _searchTextFieldStyle ?? GUI.skin.textField);
        if (!string.Equals(nextSearchText, _searchText, StringComparison.Ordinal))
        {
            _searchText = nextSearchText;
            _searchFilterDirty = true;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            GUI.Label(fieldRect, "Search keybinds...", _searchPlaceholderStyle ?? GUI.skin.label);
        }

        var toggleX = fieldRect.xMax + SearchGap;
        var toggleY = panelRect.y + ((SearchPanelHeight - ToggleHeight) * 0.5f);
        var nextSearchByKey = DrawSearchToggle(new Rect(toggleX, toggleY, KeyToggleWidth, ToggleHeight), _searchByKey, "Key");
        if (nextSearchByKey != _searchByKey)
        {
            _searchByKey = nextSearchByKey;
            _searchFilterDirty = true;
        }

        toggleX += KeyToggleWidth + ToggleSpacing;
        var nextSearchByModTitle = DrawSearchToggle(new Rect(toggleX, toggleY, ModTitleToggleWidth, ToggleHeight), _searchByModTitle, "Mod Title");
        if (nextSearchByModTitle != _searchByModTitle)
        {
            _searchByModTitle = nextSearchByModTitle;
            _searchFilterDirty = true;
        }

        toggleX += ModTitleToggleWidth + ToggleSpacing;
        var nextSearchByKeybindName = DrawSearchToggle(new Rect(toggleX, toggleY, KeybindNameToggleWidth, ToggleHeight), _searchByKeybindName, "Keybind Name");
        if (nextSearchByKeybindName != _searchByKeybindName)
        {
            _searchByKeybindName = nextSearchByKeybindName;
            _searchFilterDirty = true;
        }

        ApplySearchFilterIfNeeded();
    }

    public static void MarkSearchFilterDirty()
    {
        _searchFilterDirty = true;
    }

    private static bool TryGetActiveKeybindPageScreenRect(out Rect rect)
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

            if (state.TryGetKeybindPageScreenRect(out rect))
            {
                RemoveStates(deadIds);
                return true;
            }
        }

        RemoveStates(deadIds);
        rect = default;
        return false;
    }

    private static void EnsureSearchStyles()
    {
        if (_searchPanelStyle == null)
        {
            _searchPanelTexture ??= BuildSearchPanelBackground();
            _searchPanelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _searchPanelTexture },
                border = new RectOffset(12, 12, 12, 12),
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
        }

        if (_searchTextFieldStyle == null)
        {
            _searchFieldTexture ??= BuildSearchFieldBackground();
            _searchTextFieldStyle = new GUIStyle(GUI.skin.textField);
            _searchTextFieldStyle.fontSize = 15;
            _searchTextFieldStyle.alignment = TextAnchor.MiddleLeft;
            _searchTextFieldStyle.padding = new RectOffset(10, 8, 0, 0);
            _searchTextFieldStyle.border = new RectOffset(10, 10, 10, 10);
            _searchTextFieldStyle.normal.background = _searchFieldTexture;
            _searchTextFieldStyle.hover.background = _searchFieldTexture;
            _searchTextFieldStyle.focused.background = _searchFieldTexture;
            _searchTextFieldStyle.active.background = _searchFieldTexture;
            _searchTextFieldStyle.normal.textColor = Color.white;
            _searchTextFieldStyle.hover.textColor = Color.white;
            _searchTextFieldStyle.focused.textColor = Color.white;
            _searchTextFieldStyle.active.textColor = Color.white;
        }

        if (_searchToggleOnTexture == null)
        {
            _searchToggleOffTexture = BuildSearchToggleTexture(isChecked: false);
            _searchToggleOnTexture = BuildSearchToggleTexture(isChecked: true);
        }

        if (_searchToggleLabelStyle == null)
        {
            _searchToggleLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f, 0.98f) },
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        if (_searchToggleTickStyle == null)
        {
            _searchToggleTickStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        if (_searchPlaceholderStyle == null)
        {
            _searchPlaceholderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f, 0.85f) },
                padding = new RectOffset(10, 0, 0, 0)
            };
        }
    }

    private static Texture2D BuildSearchPanelBackground()
    {
        return BuildRoundedRectTexture(
            size: 64,
            radius: 10f,
            border: 2f,
            topColor: new Color(0.33f, 0.36f, 0.40f, 0.97f),
            bottomColor: new Color(0.27f, 0.30f, 0.35f, 0.97f),
            borderColor: new Color(0.14f, 0.16f, 0.19f, 0.98f));
    }

    private static Texture2D BuildSearchFieldBackground()
    {
        return BuildRoundedRectTexture(
            size: 64,
            radius: 8f,
            border: 2f,
            topColor: new Color(0.18f, 0.20f, 0.24f, 1f),
            bottomColor: new Color(0.14f, 0.16f, 0.20f, 1f),
            borderColor: new Color(0.08f, 0.10f, 0.13f, 1f));
    }

    private static Texture2D BuildRoundedRectTexture(
        int size,
        float radius,
        float border,
        Color topColor,
        Color bottomColor,
        Color borderColor)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (var y = 0; y < size; y++)
        {
            var t = y / (float)(size - 1);
            var fillColor = Color.Lerp(topColor, bottomColor, t);
            for (var x = 0; x < size; x++)
            {
                var alpha = ComputeRoundedRectAlpha(x, y, size, radius);
                if (alpha <= 0f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var distToEdgeX = Mathf.Min(x, size - 1 - x);
                var distToEdgeY = Mathf.Min(y, size - 1 - y);
                var edgeDistance = Mathf.Min(distToEdgeX, distToEdgeY);
                var edgeLerp = Mathf.Clamp01(edgeDistance / Mathf.Max(0.001f, border));
                var color = Color.Lerp(borderColor, fillColor, edgeLerp);
                color.a *= alpha;
                tex.SetPixel(x, y, color);
            }
        }

        tex.Apply();
        return tex;
    }

    private static float ComputeRoundedRectAlpha(int x, int y, int size, float radius)
    {
        if (radius <= 0f) return 1f;

        var left = radius;
        var right = size - 1 - radius;
        var top = radius;
        var bottom = size - 1 - radius;

        var clampedX = Mathf.Clamp(x, left, right);
        var clampedY = Mathf.Clamp(y, top, bottom);

        var dx = x - clampedX;
        var dy = y - clampedY;
        var distance = Mathf.Sqrt((dx * dx) + (dy * dy));
        if (distance >= radius) return 0f;
        if (distance <= radius - 1f) return 1f;
        return Mathf.Clamp01(radius - distance);
    }

    private static bool DrawSearchToggle(Rect rect, bool value, string label)
    {
        var boxSize = Mathf.Min(ToggleBoxSize, rect.height - 2f);
        var boxY = rect.y + ((rect.height - boxSize) * 0.5f);
        var boxRect = new Rect(rect.x, boxY, boxSize, boxSize);
        var labelRect = new Rect(
            boxRect.xMax + ToggleLabelGap,
            rect.y,
            Mathf.Max(0f, rect.width - boxSize - ToggleLabelGap),
            rect.height);

        var nextValue = value;
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            nextValue = !nextValue;
        }

        var boxTexture = nextValue ? _searchToggleOnTexture : _searchToggleOffTexture;
        if (boxTexture != null)
        {
            GUI.DrawTexture(boxRect, boxTexture, ScaleMode.StretchToFill, true);
        }

        if (nextValue)
        {
            GUI.Label(boxRect, "\u2713", _searchToggleTickStyle ?? GUI.skin.label);
        }

        GUI.Label(labelRect, label, _searchToggleLabelStyle ?? GUI.skin.label);
        return nextValue;
    }

    private static Texture2D BuildSearchToggleTexture(bool isChecked)
    {
        const int size = 64;
        const int border = 4;
        var blueOn = new Color(0.13f, 0.59f, 0.95f, 1f); // ~#2196F3
        var offFill = new Color(0f, 0f, 0f, 0f);
        var borderColor = new Color(0f, 0f, 0f, 0.95f);

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var isBorder = x < border || x >= size - border || y < border || y >= size - border;
                if (isBorder)
                {
                    tex.SetPixel(x, y, borderColor);
                }
                else
                {
                    tex.SetPixel(x, y, isChecked ? blueOn : offFill);
                }
            }
        }

        tex.Apply();
        return tex;
    }

    private static void ApplySearchFilterIfNeeded()
    {
        if (!_searchFilterDirty) return;

        var query = BuildSearchFilterQuery();
        VisitAliveStates(state => state.ApplySearchFilter(query));
        _searchFilterDirty = false;
    }

    private static SearchFilterQuery BuildSearchFilterQuery()
    {
        var text = _searchText?.Trim() ?? string.Empty;
        return new SearchFilterQuery(text, _searchByKey, _searchByModTitle, _searchByKeybindName);
    }

    private static bool MatchesFilterField(string? fieldValue, string queryText)
    {
        if (string.IsNullOrEmpty(queryText)) return true;
        var text = fieldValue ?? string.Empty;
        if (text.Length == 0) return false;
        return text.IndexOf(queryText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesKeyFilter(string? keyDisplay, string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText)) return true;
        var display = keyDisplay ?? string.Empty;
        if (display.Length == 0) return false;

        var trimmedQuery = queryText.Trim();
        if (trimmedQuery.Length == 1 && char.IsLetterOrDigit(trimmedQuery[0]))
        {
            return ContainsSingleCharacterKeyToken(display, trimmedQuery[0]);
        }

        return MatchesFilterField(display, trimmedQuery);
    }

    private static bool ContainsSingleCharacterKeyToken(string display, char queryChar)
    {
        var normalizedQuery = char.ToUpperInvariant(queryChar);
        var tokenStart = -1;

        for (var i = 0; i <= display.Length; i++)
        {
            var isTokenChar = i < display.Length && char.IsLetterOrDigit(display[i]);
            if (isTokenChar)
            {
                if (tokenStart < 0)
                {
                    tokenStart = i;
                }
                continue;
            }

            if (tokenStart >= 0)
            {
                var tokenLength = i - tokenStart;
                if (tokenLength == 1)
                {
                    var tokenChar = char.ToUpperInvariant(display[tokenStart]);
                    if (tokenChar == normalizedQuery) return true;
                }

                tokenStart = -1;
            }
        }

        return false;
    }
}
