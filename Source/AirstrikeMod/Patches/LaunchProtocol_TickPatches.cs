using HarmonyLib;
using Vehicles;

namespace AirstrikeMod.Patches
{
    /// <summary>
    /// Doubles takeoff/landing animation speed for vehicles flagged via
    /// <see cref="BombingSpeedManager"/>. Direct port of the Local Flight mod's trick.
    ///
    /// Each prefix runs once per game tick, advances <c>TicksPassed</c> by an extra 1, and
    /// returns true so the original method runs and advances another tick of its own —
    /// effectively 2 animation ticks per real tick.
    /// </summary>
    [HarmonyPatch(typeof(LaunchProtocol), "TickTakeoff")]
    public static class LaunchProtocol_TickTakeoff_Patch
    {
        public static void Prefix(LaunchProtocol __instance)
        {
            VehiclePawn vehicle = __instance?.Vehicle;
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
            VehiclePawn vehicle = __instance?.Vehicle;
            if (vehicle == null) return;
            if (!BombingSpeedManager.IsFast(vehicle)) return;

            __instance.SetTickCount(__instance.TicksPassed + 1);
        }
    }

    /// <summary>
    /// Clear the fast-flag once landing completes. Without this, a vehicle that did one
    /// airstrike would get fast takeoff/landing forever (across the session) on its
    /// vanilla launches too — the flag is set on entering the airstrike but otherwise
    /// has no exit. UnmarkFast on a vehicle not in the set is a harmless no-op, so this
    /// is safe to run for every landing, not just airstrike returns.
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
