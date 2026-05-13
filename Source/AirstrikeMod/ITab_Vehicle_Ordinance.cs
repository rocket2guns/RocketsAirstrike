using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using SmashTools;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace AirstrikeMod
{
    public class ITab_Vehicle_Ordinance : ITab
    {
        private const float TOP_PADDING = 20f;
        private const float ROW_HEIGHT = 30f;
        private const float ICON_SIZE = 28f;
        private const float RADIO_BOX_SIZE = 20f;
        private const float RADIO_COLUMN_WIDTH = 24f;
        private const float TOGGLE_HEIGHT = 24f;
        private const float TOGGLE_GAP = 4f;
        private const float HEADER_HEIGHT = 26f;
        private const float CHEVRON_SIZE = 14f;

        private const string UncategorizedKey = "__uncategorized";

        private Vector2 scrollPosition;
        private float scrollViewHeight;

        // Collapse state lives on the tab instance
        private readonly Dictionary<string, bool> collapsed = new Dictionary<string, bool>();

        // Scratch buffers reused across FillTab calls so we don't allocate every frame.
        private readonly Dictionary<ThingCategoryDef, List<RowData>> groupBuf =
            new Dictionary<ThingCategoryDef, List<RowData>>();
        private readonly List<ThingCategoryDef> orderBuf = new List<ThingCategoryDef>();

        public ITab_Vehicle_Ordinance()
        {
            size = new Vector2(340f, 480f);
            labelKey = "ROCKET_TabOrdinance";
        }

        private VehiclePawn Vehicle => SelPawn as VehiclePawn;

        private CompAirstrikeBase FindOrdinanceComp()
        {
            var v = Vehicle;
            if (v == null) return null;
            var comps = v.AllComps;
            for (var i = 0; i < comps.Count; i++)
                if (comps[i] is CompAirstrikeBase c && c.RequiresOrdinance && c.HasAvailableOrdinance())
                    return c;
            return null;
        }

        public override bool IsVisible => FindOrdinanceComp() != null;

        protected override void FillTab()
        {
            var comp = FindOrdinanceComp();
            if (comp == null) return;

            using var textBlock = new TextBlock(GameFont.Small);

            var outer = new Rect(0f, TOP_PADDING, size.x, size.y - TOP_PADDING).ContractedBy(10f);
            Widgets.BeginGroup(outer);

            var curY = 0f;
            DrawToggles(ref curY, outer.width);

            var outRect = new Rect(0f, curY, outer.width, outer.height - curY);
            var viewRect = new Rect(0f, 0f, outer.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, true);

            var rowY = 0f;
            DrawNoneRow(ref rowY, viewRect.width, comp);
            DrawCategorySections(ref rowY, viewRect.width, comp);

            if (Event.current.type == EventType.Layout)
                scrollViewHeight = rowY + 4f;

            Widgets.EndScrollView();
            Widgets.EndGroup();
        }

        private static void DrawToggles(ref float curY, float width)
        {
            var rect = new Rect(0f, curY, width, TOGGLE_HEIGHT);
            var hide = AirstrikeMod.Settings.hideEmptyOrdinance;
            var before = hide;
            Widgets.CheckboxLabeled(rect,
                "ROCKET_TabOrdinance_HideUnavailable".Translate(),
                ref hide);
            if (hide != before)
            {
                AirstrikeMod.Settings.hideEmptyOrdinance = hide;
                AirstrikeMod.Settings.Write();
            }
            curY += TOGGLE_HEIGHT + TOGGLE_GAP;
            Widgets.ListSeparator(ref curY, width,
                "ROCKET_TabOrdinance_Header".Translate());
        }

        private void DrawCategorySections(ref float y, float width, CompAirstrikeBase comp)
        {
            var hideEmpty = AirstrikeMod.Settings.hideEmptyOrdinance;
            BuildGroups(comp);

            foreach (var category in orderBuf)
            {
                var rows = groupBuf[category];
                var visibleRows = 0;
                var hasSelected = false;
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].isSelected) hasSelected = true;
                    if (rows[i].empty && hideEmpty && !rows[i].isSelected) continue;
                    visibleRows++;
                }
                if (visibleRows == 0) continue;

                var key = category?.defName ?? UncategorizedKey;
                DrawCategoryHeader(ref y, width, category, key, visibleRows, hasSelected);
                if (IsCollapsed(key)) continue;

                for (var i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (r.empty && hideEmpty && !r.isSelected) continue;
                    DrawOrdinanceRow(ref y, width, comp, r.thing, r.count, r.isSelected, r.empty);
                }
            }
        }

        private void BuildGroups(CompAirstrikeBase comp)
        {
            foreach (var list in groupBuf.Values) list.Clear();
            orderBuf.Clear();

            foreach (var thing in comp.AvailableOrdinance())
            {
                var category = thing.thingCategories is { Count: > 0 }
                    ? thing.thingCategories[0]
                    : null;
                if (!groupBuf.TryGetValue(category, out var list))
                {
                    list = new List<RowData>();
                    groupBuf[category] = list;
                }
                if (!orderBuf.Contains(category)) orderBuf.Add(category);

                var count = comp.CountInCargo(thing);
                list.Add(new RowData {
                    thing = thing,
                    count = count,
                    empty = count <= 0,
                    isSelected = comp.SelectedOrdinance == thing,
                });
            }
            orderBuf.Sort(CompareCategories);
        }

        private static int CompareCategories(ThingCategoryDef a, ThingCategoryDef b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return string.Compare(a.LabelCap.RawText, b.LabelCap.RawText,
                System.StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCollapsed(string key) =>
            collapsed.TryGetValue(key, out var v) && v;

        private void DrawCategoryHeader(ref float y, float width, ThingCategoryDef category,
            string key, int visibleCount, bool hasSelected)
        {
            var rect = new Rect(0f, y, width, HEADER_HEIGHT);

            if (Mouse.IsOver(rect))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                GUI.DrawTexture(rect, TexUI.HighlightTex);
                GUI.color = Color.white;
            }

            var isCollapsed = IsCollapsed(key);
            var chevronRect = new Rect(rect.x + 4f,
                rect.y + (HEADER_HEIGHT - CHEVRON_SIZE) * 0.5f,
                CHEVRON_SIZE, CHEVRON_SIZE);
            GUI.DrawTexture(chevronRect, isCollapsed ? TexButton.Reveal : TexButton.Collapse);

            var labelX = chevronRect.xMax + 6f;
            var labelRect = new Rect(labelX, rect.y, rect.width - labelX - 6f, rect.height);
            var labelText = category != null
                ? category.LabelCap.ToString()
                : "ROCKET_Category_Uncategorized".Translate().ToString();
            var label = $"{labelText} ({visibleCount})";
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft))
            {
                GUI.color = new Color(0.65f, 0.65f, 0.65f, 1f);
                Widgets.Label(labelRect, label);
                GUI.color = Color.white;
            }

            if (Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                collapsed[key] = !isCollapsed;
            }

            y += HEADER_HEIGHT;
        }

        private static void DrawNoneRow(ref float y, float width, CompAirstrikeBase comp)
        {
            var rect = new Rect(0f, y, width, ROW_HEIGHT);
            var isSelected = comp.SelectedOrdinance == null;

            DrawRowHighlight(rect, isSelected);
            DrawRadio(rect, isSelected);

            var labelX = RADIO_COLUMN_WIDTH + 8f;
            var labelRect = new Rect(labelX, y, width - labelX - 4f, ROW_HEIGHT);
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(labelRect, "ROCKET_OrdinanceNone".Translate());

            if (!isSelected && Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                comp.SelectedOrdinance = null;
            }
            y += ROW_HEIGHT;
        }

        private static void DrawOrdinanceRow(ref float y, float width, CompAirstrikeBase comp,
            ThingDef thing, int count, bool isSelected, bool disabled)
        {
            var rect = new Rect(0f, y, width, ROW_HEIGHT);

            DrawRowHighlight(rect, isSelected);
            DrawRadio(rect, isSelected);

            var iconX = RADIO_COLUMN_WIDTH + 8f;
            var iconRect = new Rect(iconX, y + (ROW_HEIGHT - ICON_SIZE) * 0.5f, ICON_SIZE, ICON_SIZE);
            var icon = thing.uiIcon;
            if (icon != null)
            {
                GUI.color = disabled ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
                Widgets.DrawTextureFitted(iconRect, icon, 1f);
                GUI.color = Color.white;
            }

            var labelX = iconRect.xMax + 6f;
            var labelRect = new Rect(labelX, y, width - labelX - 4f, ROW_HEIGHT);
            var label = "ROCKET_OrdinanceCount".Translate(
                thing.LabelCap, count).Resolve();
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                GUI.color = disabled ? new Color(0.7f, 0.7f, 0.7f, 0.6f) : Color.white;
                Widgets.Label(labelRect, label);
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(thing.description))
                TooltipHandler.TipRegion(rect, thing.description);

            if (!isSelected && Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                comp.SelectedOrdinance = thing;
            }
            y += ROW_HEIGHT;
        }

        private static void DrawRowHighlight(Rect rect, bool isSelected)
        {
            if (isSelected)
            {
                GUI.color = new Color(0.3f, 0.5f, 0.3f, 0.35f);
                GUI.DrawTexture(rect, TexUI.HighlightTex);
                GUI.color = Color.white;
            }
            else if (Mouse.IsOver(rect))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                GUI.DrawTexture(rect, TexUI.HighlightTex);
                GUI.color = Color.white;
            }
        }

        private static Texture2D _radioOffTex;
        private static Texture2D RadioOffTex =>
            _radioOffTex ??= (Texture2D)typeof(Widgets)
                .GetField("RadioButOffTex", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) ?? BaseContent.BadTex;

        private static void DrawRadio(Rect rowRect, bool isSelected)
        {
            var rect = new Rect(
                rowRect.x + (RADIO_COLUMN_WIDTH - RADIO_BOX_SIZE) * 0.5f + 2f,
                rowRect.y + (rowRect.height - RADIO_BOX_SIZE) * 0.5f,
                RADIO_BOX_SIZE, RADIO_BOX_SIZE);
            GUI.DrawTexture(rect, isSelected ? Widgets.RadioButOnTex : RadioOffTex);
        }

        private struct RowData
        {
            public ThingDef thing;
            public int count;
            public bool empty;
            public bool isSelected;
        }
    }
}
