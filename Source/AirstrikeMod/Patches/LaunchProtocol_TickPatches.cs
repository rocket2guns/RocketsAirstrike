using HarmonyLib;
using Vehicles;

namespace AirstrikeMod.Patches
{
    /// <summary>
    /// Each prefix bumps TicksPassed by 1, then the original method runs and bumps another tick of its own. Net: 2 animation ticks per real tick for flagged vehicles.
    /// </summary>
    [HarmonyPatch(typeof(LaunchProtocol), "TickTakeoff")]
    public static class LaunchProtocol_TickTakeoff_Patch
    {
        public static void Prefix(LaunchProtocol __instance)
        {
            var vehicle = __instance?.Vehicle;
            if (vehicle == null) return;
            if (!BombingSpeedManager.IsFast(vehicle)) return;
            __instance.SetTickCount(__instance.TicksPassed + 1);
        }
    }

    [HarmonyPatch(typeof(LaunchProtocol), "TickLanding")]
    public static class LaunchProtocol_TickLanding_Patch
    {
        public static void Prefix(LaunchProtocol __instance)
        {
            var vehicle = __instance?.Vehicle;
            if (vehicle == null) return;
            if (!BombingSpeedManager.IsFast(vehicle)) return;
            __instance.SetTickCount(__instance.TicksPassed + 1);
        }
    }

    /// <summary>
    /// Clears the fast flag once landing finishes. Safe to run for every landing since UnmarkFast on a non-flagged vehicle is a no-op.
    /// </summary>
    [HarmonyPatch(typeof(VehicleSkyfaller_Arriving), nameof(VehicleSkyfaller_Arriving.FinalizeLanding))]
    public static class VehicleSkyfaller_Arriving_FinalizeLanding_Patch
    {
        public static void Postfix(VehicleSkyfaller_Arriving __instance)
        {
            BombingSpeedManager.UnmarkFast(__instance?.vehicle);
        }
    }
}
