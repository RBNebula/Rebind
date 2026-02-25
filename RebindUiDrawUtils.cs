namespace Rebind;

internal static class RebindUiDrawUtils
{
    public static void DrawSolidRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
        GUI.color = prev;
    }
}
