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

            listing.Label("Airstrike fuel cost (chemfuel)");
            Settings.fuelCost = listing.Slider(Settings.fuelCost, 0f, 500f);
            listing.Label($"  current: {Settings.fuelCost:0}");

            listing.GapLine();

            listing.CheckboxLabeled(
                "Fast takeoff/landing animation",
                ref Settings.fastTakeoffLanding,
                "Doubles the speed of takeoff and landing animations during airstrike runs. " +
                "Borrowed wholesale from the Local Flight mod.");

            listing.GapLine();

            listing.Label($"Bomb explosion radius: {Settings.bombRadius:0.0}");
            Settings.bombRadius = listing.Slider(Settings.bombRadius, 1f, 12f);

            listing.Label($"Bomb damage amount: {Settings.bombDamage:0}");
            Settings.bombDamage = listing.Slider(Settings.bombDamage, 10f, 500f);

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class AirstrikeModSettings : ModSettings
    {
        public float fuelCost = 50f;
        public bool fastTakeoffLanding = true;
        public float bombRadius = 4.5f;
        public float bombDamage = 120f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fuelCost, nameof(fuelCost), 50f);
            Scribe_Values.Look(ref fastTakeoffLanding, nameof(fastTakeoffLanding), true);
            Scribe_Values.Look(ref bombRadius, nameof(bombRadius), 4.5f);
            Scribe_Values.Look(ref bombDamage, nameof(bombDamage), 120f);
        }
    }
}
