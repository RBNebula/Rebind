namespace Rebind;

internal sealed class RebindUiStyleSet
{
    public RebindUiStyleSet(
        GUIStyle windowStyle,
        GUIStyle labelStyle,
        GUIStyle textFieldStyle,
        GUIStyle richButtonStyle,
        GUIStyle sliderStyle,
        GUIStyle sliderThumbStyle)
    {
        WindowStyle = windowStyle;
        LabelStyle = labelStyle;
        TextFieldStyle = textFieldStyle;
        RichButtonStyle = richButtonStyle;
        SliderStyle = sliderStyle;
        SliderThumbStyle = sliderThumbStyle;
    }

    public GUIStyle WindowStyle { get; }
    public GUIStyle LabelStyle { get; }
    public GUIStyle TextFieldStyle { get; }
    public GUIStyle RichButtonStyle { get; }
    public GUIStyle SliderStyle { get; }
    public GUIStyle SliderThumbStyle { get; }
}

internal static class RebindUiStylePresets
{
    public const string Style1 = "style_1";
    public const string DefaultPresetId = Style1;

    public static RebindUiStyleSet Build(string? _presetId, float windowOpacity, bool includeSliderStyles)
    {
        var opacity = Mathf.Clamp01(windowOpacity);

        var textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        var fieldColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        var sliderBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        var sliderThumbColor = new Color(0f, 0.8f, 1f, 1f);
        var top = new Color(0.24f, 0.30f, 0.36f, 0.90f);
        var bottom = new Color(0.06f, 0.08f, 0.11f, 0.95f);
        var buttonNormalColor = new Color(0.16f, 0.21f, 0.26f, 0.95f);
        var buttonHoverColor = new Color(0.22f, 0.29f, 0.36f, 0.98f);
        var buttonActiveColor = new Color(0.11f, 0.16f, 0.21f, 1f);

        top.a *= opacity;
        bottom.a *= opacity;

        var windowBgTex = MakeGlassTex(128, 128, top, bottom);
        var fieldTex = MakeTex(1, 1, fieldColor);

        var windowStyle = new GUIStyle(GUI.skin.window)
        {
            normal = { background = windowBgTex },
            onNormal = { background = windowBgTex },
            border = new RectOffset(2, 2, 2, 2),
            padding = new RectOffset(12, 12, 10, 12),
            contentOffset = new Vector2(0f, 1f)
        };

        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = textColor },
            richText = true
        };

        var textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            normal = { textColor = textColor, background = fieldTex },
            focused = { textColor = textColor, background = fieldTex }
        };

        var richButtonStyle = new GUIStyle(GUI.skin.button)
        {
            richText = true
        };
        var buttonNormalTex = MakeTex(1, 1, buttonNormalColor);
        var buttonHoverTex = MakeTex(1, 1, buttonHoverColor);
        var buttonActiveTex = MakeTex(1, 1, buttonActiveColor);
        richButtonStyle.normal.background = buttonNormalTex;
        richButtonStyle.hover.background = buttonHoverTex;
        richButtonStyle.active.background = buttonActiveTex;
        richButtonStyle.focused.background = buttonNormalTex;
        richButtonStyle.onNormal.background = buttonActiveTex;
        richButtonStyle.onHover.background = buttonHoverTex;
        richButtonStyle.onActive.background = buttonActiveTex;
        richButtonStyle.onFocused.background = buttonActiveTex;
        richButtonStyle.normal.textColor = textColor;
        richButtonStyle.hover.textColor = textColor;
        richButtonStyle.active.textColor = textColor;
        richButtonStyle.focused.textColor = textColor;
        richButtonStyle.onNormal.textColor = textColor;
        richButtonStyle.onHover.textColor = textColor;
        richButtonStyle.onActive.textColor = textColor;
        richButtonStyle.onFocused.textColor = textColor;
        richButtonStyle.border = new RectOffset(1, 1, 1, 1);
        richButtonStyle.padding = new RectOffset(8, 8, 4, 4);

        var sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
        var sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
        if (includeSliderStyles)
        {
            var sliderBgTex = MakeTex(1, 1, sliderBackgroundColor);
            var sliderThumbTex = MakeTex(1, 1, sliderThumbColor);

            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 18f,
                border = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 6, 6)
            };
            sliderStyle.normal.background = sliderBgTex;

            sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 12f,
                fixedHeight = 16f,
                border = new RectOffset(0, 0, 0, 0)
            };
            sliderThumbStyle.normal.background = sliderThumbTex;
        }

        return new RebindUiStyleSet(
            windowStyle,
            labelStyle,
            textFieldStyle,
            richButtonStyle,
            sliderStyle,
            sliderThumbStyle);
    }

    private static Texture2D MakeTex(int width, int height, Color col)
    {
        var pixels = new Color[width * height];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = col;
        var tex = new Texture2D(width, height);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeGlassTex(int width, int height, Color top, Color bottom)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            var tY = y / (float)(height - 1);
            var row = Color.Lerp(bottom, top, tY);
            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var noise = Mathf.PerlinNoise((x + 11f) * 0.11f, (y + 23f) * 0.11f);
                var grain = (noise - 0.5f) * 0.06f;
                var c = row;
                c.r = Mathf.Clamp01(c.r + grain);
                c.g = Mathf.Clamp01(c.g + grain);
                c.b = Mathf.Clamp01(c.b + grain);
                pixels[idx] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
