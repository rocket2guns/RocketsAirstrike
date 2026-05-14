using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    public class AirstrikeMod : Mod
    {
        public static AirstrikeModSettings Settings;

        public AirstrikeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AirstrikeModSettings>();

            var harmony = new Harmony("Rockets.Airstrike");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override string SettingsCategory() => "ROCKET_SettingsCategory".Translate();

        private enum SettingsTab { General, Debug }
        private static SettingsTab currentTab = SettingsTab.General;
        private static readonly List<TabRecord> tabBuf = new();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            const float tabBarHeight = 32f;
            var contentRect = new Rect(inRect.x, inRect.y + tabBarHeight, inRect.width, inRect.height - tabBarHeight);

            tabBuf.Clear();
            tabBuf.Add(new TabRecord("ROCKET_TabGeneral".Translate(), () => currentTab = SettingsTab.General, currentTab == SettingsTab.General));
            tabBuf.Add(new TabRecord("ROCKET_TabDebug".Translate(), () => currentTab = SettingsTab.Debug, currentTab == SettingsTab.Debug));

            Widgets.DrawMenuSection(contentRect);
            TabDrawer.DrawTabs(contentRect, tabBuf);

            var inner = contentRect.ContractedBy(12f);
            switch (currentTab)
            {
                case SettingsTab.General: DrawGeneralTab(inner); break;
                case SettingsTab.Debug:   DrawDebugTab(inner); break;
            }
        }

        private static void DrawGeneralTab(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            Text.Font = GameFont.Small;

            listing.Label("ROCKET_FuelScaleLabel".Translate(
                (Settings.fuelScale * 100f).ToString("0")));
            Settings.fuelScale = listing.Slider(Settings.fuelScale, 0f, 1f);

            listing.End();
        }

        private static void DrawDebugTab(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            Text.Font = GameFont.Small;

            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            listing.Label("ROCKET_DebugTabBlurb".Translate());
            GUI.color = Color.white;
            listing.Gap(6f);

            listing.CheckboxLabeled(
                "ROCKET_DebugDrawFlightPathLabel".Translate(),
                ref Settings.debugDrawFlightPath,
                "ROCKET_DebugDrawFlightPathDesc".Translate());

            listing.End();
        }
    }

    public class AirstrikeModSettings : ModSettings
    {
        public float fuelScale = 1f;
        public bool debugDrawFlightPath;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fuelScale, nameof(fuelScale), 1f);
            Scribe_Values.Look(ref debugDrawFlightPath, nameof(debugDrawFlightPath));
        }
    }
}
