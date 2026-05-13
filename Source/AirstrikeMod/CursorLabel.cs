using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    public static class CursorLabel
    {
        public static string Current;
        public static string SecondLine;
        public static Color SecondLineColor = Color.white;

        public static void Clear()
        {
            Current = null;
            SecondLine = null;
            SecondLineColor = Color.white;
        }

        public static void Draw()
        {
            if (string.IsNullOrEmpty(Current)) return;

            var mousePos = Event.current.mousePosition;
            var rect = new Rect(mousePos.x + 32f, mousePos.y + 36f, 220f, 22f);

            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(rect, Current);

            if (!string.IsNullOrEmpty(SecondLine))
            {
                var rect2 = new Rect(rect.x, rect.y + 16f, rect.width, 22f);
                GUI.color = SecondLineColor;
                Widgets.Label(rect2, SecondLine);
            }

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }
    }
}
