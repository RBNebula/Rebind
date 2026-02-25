namespace Rebind;

internal static class RebindUiButtonUtils
{
    public static bool DrawRichButton(string text, GUIStyle? richButtonStyle, params GUILayoutOption[] options)
    {
        var style = richButtonStyle ?? GUI.skin.button;
        var content = new GUIContent(text);
        var rect = GUILayoutUtility.GetRect(content, style, options);
        var isHovered = rect.Contains(Event.current.mousePosition);

        var prevColor = GUI.color;
        if (isHovered)
        {
            GUI.color = Color.Lerp(prevColor, Color.white, 0.18f);
        }

        var clicked = GUI.Button(rect, content, style);
        GUI.color = prevColor;

        if (Event.current.type == EventType.Repaint && isHovered)
        {
            RebindUiDrawUtils.DrawSolidRect(rect, new Color(1f, 1f, 1f, 0.10f));
        }

        return clicked;
    }
}
