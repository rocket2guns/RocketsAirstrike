using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    public static class CursorLabel
    {
        public static string Current;
        public static string SecondLine;
        public static string ThirdLine;
        public static string FourthLine;

        public static void Clear()
        {
            Current = null;
            SecondLine = null;
            ThirdLine = null;
            FourthLine = null;
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
                Widgets.Label(rect2, SecondLine);
            }

            if (!string.IsNullOrEmpty(ThirdLine))
            {
                var rect3 = new Rect(rect.x, rect.y + 32f, rect.width, 22f);
                GUI.color = Color.gray;
                Widgets.Label(rect3, ThirdLine);
            }

            if (!string.IsNullOrEmpty(FourthLine))
            {
                var rect4 = new Rect(rect.x, rect.y + 48f, rect.width, 22f);
                GUI.color = Color.gray;
                Widgets.Label(rect4, FourthLine);
            }

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }
    }
}
