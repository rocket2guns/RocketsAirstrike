using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    // Set Current when entering a targeting phase, clear it on exit. Each targeter's
    // OnGUI fires only while it's active, so the per-phase patch hook (or the targeter
    // itself) drawing this label is naturally scoped to the right phase.
    public static class CursorLabel
    {
        public static string Current;

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

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }
    }
}
