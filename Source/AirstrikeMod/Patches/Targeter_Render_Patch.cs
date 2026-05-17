using HarmonyLib;
using RimWorld;
using Verse;

namespace AirstrikeMod.Patches
{
    /// <summary>
    /// Draws the explosion radius ring at the cursor during stage-A bomb-cell targeting.
    /// </summary>
    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterUpdate))]
    public static class Targeter_TargeterUpdate_Patch
    {
        public static void Postfix()
        {
            if (!CompAirstrikeBase.BombTargetingActive) return;

            var map = CompAirstrikeBase.BombTargetingMap;
            if (map == null || map != Find.CurrentMap) return;
            var cell = UI.MouseCell();
            if (!cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(cell, CompAirstrikeBase.BombTargetingRadius);
        }
    }

    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterOnGUI))]
    public static class Targeter_TargeterOnGUI_Patch
    {
        public static void Postfix() => CursorLabel.Draw();
    }

    [HarmonyPatch(typeof(Vehicles.LandingTargeter), nameof(Vehicles.LandingTargeter.TargeterOnGUI))]
    public static class LandingTargeter_TargeterOnGUI_Patch
    {
        public static void Postfix()
        {
            CursorLabel.Draw();
            // Null icon → text-only attachment, stacks below VF's arrow already drawn this frame.
            if (!string.IsNullOrEmpty(CompAirstrikeLocalFlight.PendingMouseLabel))
                GenUI.DrawMouseAttachment(null, CompAirstrikeLocalFlight.PendingMouseLabel);
        }
    }
}
