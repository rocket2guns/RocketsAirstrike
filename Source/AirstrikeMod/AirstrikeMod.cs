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

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("ROCKET_FuelScaleLabel".Translate());
            Settings.fuelScale = listing.Slider(Settings.fuelScale, 0f, 1f);
            listing.Label("ROCKET_FuelScaleCurrent".Translate(
                (Settings.fuelScale * 100f).ToString("0")));

            listing.GapLine();

            listing.CheckboxLabeled(
                "ROCKET_HideEmptyOrdinanceLabel".Translate(),
                ref Settings.hideEmptyOrdinance,
                "ROCKET_HideEmptyOrdinanceDesc".Translate());

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class AirstrikeModSettings : ModSettings
    {
        public float fuelScale = 1f;
        public bool hideEmptyOrdinance;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fuelScale, nameof(fuelScale), 1f);
            Scribe_Values.Look(ref hideEmptyOrdinance, nameof(hideEmptyOrdinance));
        }
    }
}
