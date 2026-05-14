using HarmonyLib;
using Verse;

namespace AirstrikeMod.Patches
{
    [HarmonyPatch(typeof(GenExplosion), nameof(GenExplosion.DoExplosion))]
    public static class GenExplosion_DoExplosion_Patch
    {
        public static void Postfix(IntVec3 center, Map map, float radius, ThingDef projectile)
        {
            if (projectile == null || map == null) return;
            var ext = projectile.GetModExtension<PolluteOnExplodeModExtension>();
            if (ext == null) return;
            PolluteHelper.PolluteCellsInRadius(map, center, radius * ext.radiusMultiplier, ext.chance);
        }
    }
}
