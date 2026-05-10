using HarmonyLib;
using UnityEngine;
using Vehicles;

namespace AirstrikeMod.Patches
{
    [HarmonyPatch(typeof(LaunchProtocol), nameof(LaunchProtocol.Draw))]
    public static class LaunchProtocol_Draw_Patch
    {
        public static void Postfix(LaunchProtocol __instance,
            (Vector3 drawPos, float rotation) __result)
        {
            var vehicle = __instance?.Vehicle;
            if (vehicle == null) return;
            var rot = __instance.CurAnimationProperties?.forcedRotation ?? vehicle.Rotation;
            CompEngineFlame.DrawFlamesFor(vehicle, __result.drawPos, rot, __result.rotation);
        }
    }
}
