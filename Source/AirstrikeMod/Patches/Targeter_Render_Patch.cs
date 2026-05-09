using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AirstrikeMod.Patches
{
    /// <summary>
    /// Draws the explosion radius ring at the cursor while the airstrike's stage-A
    /// bomb-cell targeter is active. RimWorld's <see cref="Targeter"/> doesn't expose
    /// a per-frame draw hook for callers using <c>BeginTargeting(TargetingParameters,...)</c>,
    /// so we postfix <c>TargeterUpdate</c> (called every frame in world space) and gate
    /// on the static flag set by <see cref="CompAirstrike.StartBombTargeting"/>.
    /// </summary>
    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterUpdate))]
    public static class Targeter_TargeterUpdate_Patch
    {
        public static void Postfix()
        {
            if (!CompAirstrike.BombTargetingActive) return;

            Map map = CompAirstrike.BombTargetingMap;
            if (map == null || map != Find.CurrentMap) return;

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map)) return;

            GenDraw.DrawRadiusRing(cell, CompAirstrike.BombTargetingRadius);
        }
    }
}
