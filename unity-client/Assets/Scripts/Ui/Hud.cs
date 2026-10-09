using UnityEngine;

public static class Hud
{
    public static int FontSize(float screenFraction, int minimum)
    {
        return Mathf.Max(minimum, Mathf.RoundToInt(Screen.height * screenFraction));
    }

    public static GUIStyle Label(int fontSize, Color color,
                                 bool bold = false, bool wordWrap = false)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            wordWrap = wordWrap,
            normal = { textColor = color }
        };
    }

    public static void Backdrop(Rect area, float opacity)
    {
        GUI.color = new Color(0f, 0f, 0f, opacity);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
