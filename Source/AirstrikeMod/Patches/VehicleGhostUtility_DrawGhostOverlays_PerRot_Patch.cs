using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SmashTools;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace AirstrikeMod.Patches
{
    [HarmonyPatch(typeof(VehicleGhostUtility), nameof(VehicleGhostUtility.DrawGhostOverlays))]
    internal static class VehicleGhostUtility_DrawGhostOverlays_PerRot_Patch
    {
        private static readonly MethodInfo GhostGraphicOverlaysForMI = AccessTools.Method(
            typeof(VehicleGhostUtility), "GhostGraphicOverlaysFor");
        private static readonly MethodInfo DrawGhostTurretTexturesMI = AccessTools.Method(
            typeof(VehicleGhostUtility), "DrawGhostTurretTextures");

        [HarmonyPrefix]
        private static bool Prefix(IntVec3 center, Rot8 rot, VehicleDef vehicleDef,
            Graphic baseGraphic, Color ghostCol, AltitudeLayer drawAltitude, Thing thing)
        {
            var overlays = vehicleDef.drawProperties?.overlays;
            if (overlays == null || overlays.Count == 0) return true;

            var hasPerRot = false;
            for (var i = 0; i < overlays.Count; i++)
            {
                if (overlays[i].data.graphicData is GraphicDataOverlayPerRot)
                {
                    hasPerRot = true;
                    break;
                }
            }
            if (!hasPerRot) return true;

            var baseLoc = GenThing.TrueCenter(center, (Rot4)rot, vehicleDef.Size,
                drawAltitude.AltitudeFor());
            var bodyDrawLoc = baseLoc + baseGraphic.DrawOffsetFull(rot);

            var ghostList = (IEnumerable<(Graphic graphic, float rotation)>)
                GhostGraphicOverlaysForMI.Invoke(null, new object[] { vehicleDef, ghostCol });

            var idx = 0;
            foreach (var entry in ghostList)
            {
                var graphic = entry.graphic;
                var overlayRotation = entry.rotation;

                Vector3? perRotOffset = null;
                if (idx < overlays.Count
                    && overlays[idx].data.graphicData is GraphicDataOverlayPerRot perRot)
                {
                    perRotOffset = rot.AsInt switch
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
                }

                if (perRotOffset.HasValue)
                {
                    DrawAtExactLoc(graphic, bodyDrawLoc + perRotOffset.Value, (Rot4)rot,
                        vehicleDef, thing, overlayRotation);
                }
                else
                {
                    graphic.DrawWorker(bodyDrawLoc, (Rot4)rot, vehicleDef, thing, overlayRotation);
                }
                idx++;
            }

            if (vehicleDef.GetSortedCompProperties<CompProperties_VehicleTurrets>() != null)
            {
                DrawGhostTurretTexturesMI.Invoke(null,
                    new object[] { vehicleDef, baseLoc, rot, ghostCol });
            }

            return false;
        }

        private static void DrawAtExactLoc(Graphic graphic, Vector3 loc, Rot4 rot, ThingDef def,
            Thing thing, float extraRotation)
        {
            var mesh = graphic.MeshAt(rot);
            var quat = Quaternion.AngleAxis(rot.AsAngle, Vector3.up);
            if (extraRotation != 0f) quat *= Quaternion.Euler(Vector3.up * extraRotation);
            if (graphic.data is { addTopAltitudeBias: true })
                quat *= Quaternion.Euler(Vector3.left * 2f);
            var mat = graphic.MatAt(rot, thing);
            Graphics.DrawMesh(mesh, loc, quat, mat, 0);
            graphic.ShadowGraphic?.DrawWorker(loc, rot, def, thing, extraRotation);
        }
    }
}
