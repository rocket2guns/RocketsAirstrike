using HarmonyLib;
using SmashTools.Rendering;
using UnityEngine;
using Vehicles;

namespace AirstrikeMod.Patches
{
    [HarmonyPatch(typeof(GraphicOverlay), "ParallelGetPreRenderResults")]
    internal static class GraphicOverlay_ParallelGetPreRenderResults_PerRot_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(GraphicOverlay __instance, in TransformData transformData,
            ref PreRenderResults __result)
        {
            if (__instance?.data?.graphicData is not GraphicDataOverlayPerRot perRot) return;
            if (!__result.draw) return;

            Vector3? offset = transformData.orientation.AsInt switch
            {
                0 => perRot.drawOffsetNorth,
                1 => perRot.drawOffsetEast,
                2 => perRot.drawOffsetSouth,
                3 => perRot.drawOffsetWest,
                4 => perRot.drawOffsetNorthEast,
                5 => perRot.drawOffsetSouthEast,
                6 => perRot.drawOffsetSouthWest,
                7 => perRot.drawOffsetNorthWest,
                _ => null
            };

            if (!offset.HasValue) return;

            var v = offset.Value;
            var effectiveRotation = transformData.rotation;

            if (transformData.orientation.AsInt == 3
                && __instance.Vehicle?.VehicleDef?.graphicData?.Graphic is Graphic_Rgb bodyG
                && bodyG.WestFlipped && !bodyG.EastRotated)
            {
                effectiveRotation = -effectiveRotation;
            }

            if (effectiveRotation != 0f)
            {
                v = Quaternion.AngleAxis(effectiveRotation, Vector3.up) * v;
            }
            var pos = transformData.position + v;
            pos.y = __result.position.y;
            __result.position = pos;
        }
    }
}
