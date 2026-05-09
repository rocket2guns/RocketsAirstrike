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

        public override string SettingsCategory() => "Rocket's Airstrike";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("Airstrike fuel cost (% of one world-tile flight)");
            Settings.fuelScale = listing.Slider(Settings.fuelScale, 0f, 1f);
            listing.Label($"  current: {Settings.fuelScale * 100f:0}%");

            listing.GapLine();

            listing.CheckboxLabeled(
                "Fast takeoff/landing animation",
                ref Settings.fastTakeoffLanding,
                "Doubles the speed of takeoff and landing animations during airstrike runs.");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class AirstrikeModSettings : ModSettings
    {
        public float fuelScale = 1f;
        public bool fastTakeoffLanding = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fuelScale, nameof(fuelScale), 1f);
            Scribe_Values.Look(ref fastTakeoffLanding, nameof(fastTakeoffLanding), true);
        }
    }
}
