using RimWorld;
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

        public static Texture2D Icon;
        public static Vector2 FootprintHalfExtent;

        private const float ICON_SIZE = 70f;
        private const float ICON_TEXT_GAP = 8f;
        private const float TEXT_WIDTH = 220f;
        private const float ROW_HEIGHT = 16f;
        private const float WINDOW_PAD = 6f;
        private const float FOOTPRINT_CLEARANCE_PX = 12f;
        private const float CURSOR_CLEARANCE_PX = 14f;

        public static void Clear()
        {
            Current = null;
            SecondLine = null;
            ThirdLine = null;
            FourthLine = null;
            Icon = null;
            FootprintHalfExtent = Vector2.zero;
        }

        public static void Draw()
        {
            if (string.IsNullOrEmpty(Current)) return;

            var rowCount = 1
                           + (string.IsNullOrEmpty(SecondLine) ? 0 : 1)
                           + (string.IsNullOrEmpty(ThirdLine) ? 0 : 1)
                           + (string.IsNullOrEmpty(FourthLine) ? 0 : 1);

            var textBlockHeight = rowCount * ROW_HEIGHT;
            var contentHeight = Mathf.Max(ICON_SIZE, textBlockHeight);
            var windowWidth = WINDOW_PAD + ICON_SIZE + ICON_TEXT_GAP + TEXT_WIDTH + WINDOW_PAD;
            var windowHeight = WINDOW_PAD + contentHeight + WINDOW_PAD;

            var anchor = ComputeAnchor();
            anchor.x = Mathf.Min(anchor.x, UI.screenWidth - windowWidth - 4f);
            anchor.y = Mathf.Min(anchor.y, UI.screenHeight - windowHeight - 4f);
            anchor.x = Mathf.Max(anchor.x, 4f);
            anchor.y = Mathf.Max(anchor.y, 4f);

            var windowRect = new Rect(anchor.x, anchor.y, windowWidth, windowHeight);
            Widgets.DrawWindowBackground(windowRect);

            DrawIconBlock(windowRect, contentHeight);
            DrawTextBlock(windowRect, contentHeight);
        }

        private static Vector2 ComputeAnchor()
        {
            var mousePos = Event.current.mousePosition;
            if (Find.Camera == null)
                return new Vector2(mousePos.x + CURSOR_CLEARANCE_PX * 2f,
                                   mousePos.y + CURSOR_CLEARANCE_PX * 2f);

            var cursor = UI.MouseCell();
            var altitude = AltitudeLayer.MetaOverlays.AltitudeFor();

            var pushDown = FootprintHalfExtent.x >= FootprintHalfExtent.y;
            if (pushDown)
            {
                var below = WorldToGUI(new Vector3(
                    cursor.x + 0.5f, altitude,
                    cursor.z - FootprintHalfExtent.y - 0.5f));
                return new Vector2(
                    mousePos.x + CURSOR_CLEARANCE_PX,
                    Mathf.Max(below.y + FOOTPRINT_CLEARANCE_PX,
                              mousePos.y + CURSOR_CLEARANCE_PX * 2f));
            }
            else
            {
                var right = WorldToGUI(new Vector3(
                    cursor.x + FootprintHalfExtent.x + 0.5f, altitude,
                    cursor.z + 0.5f));
                return new Vector2(
                    Mathf.Max(right.x + FOOTPRINT_CLEARANCE_PX,
                              mousePos.x + CURSOR_CLEARANCE_PX * 2f),
                    mousePos.y + CURSOR_CLEARANCE_PX);
            }
        }

        // Canonical RimWorld pattern (see GenMapUI.LabelDrawPosFor): WorldToScreenPoint
        // returns Unity screen coords in PHYSICAL pixels (bottom-up). GUI uses
        // top-down coords scaled by Prefs.UIScale. Without dividing by UIScale the
        // offset compounds with the GUI scale on high-DPI displays.
        private static Vector2 WorldToGUI(Vector3 worldPos)
        {
            var v = (Vector2)Find.Camera.WorldToScreenPoint(worldPos) / Prefs.UIScale;
            v.y = UI.screenHeight - v.y;
            return v;
        }

        private static void DrawIconBlock(Rect windowRect, float contentHeight)
        {
            if (Icon == null) return;
            var iconRect = new Rect(
                windowRect.x + WINDOW_PAD,
                windowRect.y + WINDOW_PAD + (contentHeight - ICON_SIZE) * 0.5f,
                ICON_SIZE, ICON_SIZE);
            Widgets.DrawTextureFitted(iconRect, Icon, 1f);
        }

        private static void DrawTextBlock(Rect windowRect, float contentHeight)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;

            var textX = windowRect.x + WINDOW_PAD + ICON_SIZE + ICON_TEXT_GAP;
            var rows = 1
                       + (string.IsNullOrEmpty(SecondLine) ? 0 : 1)
                       + (string.IsNullOrEmpty(ThirdLine) ? 0 : 1)
                       + (string.IsNullOrEmpty(FourthLine) ? 0 : 1);
            var blockHeight = rows * ROW_HEIGHT;
            var ty = windowRect.y + WINDOW_PAD + (contentHeight - blockHeight) * 0.5f;

            Widgets.Label(new Rect(textX, ty, TEXT_WIDTH, ROW_HEIGHT), Current);
            ty += ROW_HEIGHT;

            if (!string.IsNullOrEmpty(SecondLine))
            {
                Widgets.Label(new Rect(textX, ty, TEXT_WIDTH, ROW_HEIGHT), SecondLine);
                ty += ROW_HEIGHT;
            }
            if (!string.IsNullOrEmpty(ThirdLine))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(textX, ty, TEXT_WIDTH, ROW_HEIGHT), ThirdLine);
                ty += ROW_HEIGHT;
            }
            if (!string.IsNullOrEmpty(FourthLine))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(textX, ty, TEXT_WIDTH, ROW_HEIGHT), FourthLine);
            }

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }
    }
}
