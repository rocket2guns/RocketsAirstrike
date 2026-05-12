using HarmonyLib;
using Vehicles;

namespace AirstrikeMod.Patches
{
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
    /// Restores the vehicle's pre-launch rotation and clears the fast flag once landing finishes.
    /// </summary>
    [HarmonyPatch(typeof(VehicleSkyfaller_Arriving), nameof(VehicleSkyfaller_Arriving.FinalizeLanding))]
    public static class VehicleSkyfaller_Arriving_FinalizeLanding_Patch
    {
        public static void Postfix(VehicleSkyfaller_Arriving __instance)
        {
            var vehicle = __instance?.vehicle;
            if (vehicle == null) return;
            if (BombingSpeedManager.TryGetOriginalRotation(vehicle, out var rot))
                vehicle.Rotation = rot;
            BombingSpeedManager.UnmarkFast(vehicle);
        }
    }
}
