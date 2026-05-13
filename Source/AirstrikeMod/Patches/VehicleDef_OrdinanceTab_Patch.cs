using System;
using System.Collections.Generic;
using HarmonyLib;
using Vehicles;
using Verse;

namespace AirstrikeMod.Patches
{
    /// <summary>
    /// Auto-injects ITab_Vehicle_Ordinance onto any VehicleDef whose XML adds at least one ordinance-requiring airstrike comp
    /// </summary>
    [HarmonyPatch(typeof(VehicleDef), nameof(VehicleDef.ResolveReferences))]
    internal static class VehicleDef_OrdinanceTab_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(VehicleDef __instance)
        {
            if (__instance.comps == null) return;
            var needsTab = false;
            for (var i = 0; i < __instance.comps.Count; i++)
            {
                if (__instance.comps[i] is CompProperties_AirstrikeBase
                    && !(__instance.comps[i] is CompProperties_AirstrikeStrafingRun))
                {
                    needsTab = true;
                    break;
                }
            }
            if (!needsTab) return;

            __instance.inspectorTabs ??= new List<Type>();
            if (!__instance.inspectorTabs.Contains(typeof(ITab_Vehicle_Ordinance)))
                __instance.inspectorTabs.Add(typeof(ITab_Vehicle_Ordinance));
        }
    }
}
