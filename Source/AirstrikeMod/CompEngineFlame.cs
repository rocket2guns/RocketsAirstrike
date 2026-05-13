using System.Collections.Generic;
using SmashTools;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompEngineFlame : VehicleComp
    {
        public CompProperties_EngineFlame Props => (CompProperties_EngineFlame)props;
        private Material _flameMaterial;
        private int _lastDrawTick = -10;
        private int _rampStartTick = -10;

        private Material GetFlameMaterial()
        {
            if (_flameMaterial != null) return _flameMaterial;
            var path = Props?.texturePath;
            if (string.IsNullOrEmpty(path)) return null;
            if (ContentFinder<Texture2D>.Get(path, reportFailure: false) == null) return null;
            _flameMaterial = MaterialPool.MatFrom(path, ShaderDatabase.Transparent);
            return _flameMaterial;
        }

        // Per-def cache so the per-frame draw patch doesn't walk AllComps
        private static readonly Dictionary<VehicleDef, bool> _defHasFlameComp = new();

        public static void DrawFlamesFor(VehiclePawn vehicle, Vector3 basePos, Rot8 rot,
            float extraRotation = 0f)
        {
            if (vehicle == null) return;
            if (!DefHasFlameComp(vehicle.VehicleDef)) return;
            var comp = vehicle.GetCachedComp<CompEngineFlame>();
            comp?.DrawFlames(basePos, rot, extraRotation);
        }

        private static bool DefHasFlameComp(VehicleDef def)
        {
            if (def == null) return false;
            if (_defHasFlameComp.TryGetValue(def, out var has)) return has;
            has = false;
            var compProps = def.comps;
            if (compProps != null)
            {
                for (var i = 0; i < compProps.Count; i++)
                {
                    if (compProps[i] is CompProperties_EngineFlame)
                    {
                        has = true;
                        break;
                    }
                }
            }
            _defHasFlameComp[def] = has;
            return has;
        }

        public void DrawFlames(Vector3 basePos, Rot8 rot, float extraRotation)
        {
            var props = Props;
            if (props == null) return;
            var mat = GetFlameMaterial();
            if (mat == null) return;

            List<Vector2> offsets;
            if (rot == Rot8.North && props.exhaustOffsetsNorth?.Count > 0)
                offsets = props.exhaustOffsetsNorth;
            else if (rot == Rot8.South && props.exhaustOffsetsSouth?.Count > 0)
                offsets = props.exhaustOffsetsSouth;
            else
                offsets = props.exhaustOffsetsEast;
            if (offsets == null || offsets.Count == 0) return;
            var tilt = extraRotation;
            if (rot == Rot8.West || rot == Rot8.NorthWest || rot == Rot8.SouthWest)
                tilt = -tilt;

            var baseAngle = rot.AsAngle + tilt;
            var rotPos = Quaternion.Euler(0f, baseAngle, 0f);
            var rotQuad = Quaternion.Euler(0f, baseAngle + props.rotationDeg, 0f);
            var flameSize = props.flameSize;
            var pivot = props.pivot;
            var flicker = props.flicker;
            var paused = Find.TickManager?.Paused ?? false;
            var jitterEnabled = flicker > 0f && !paused;
            var altitude = basePos.y + 0.05f;

            var tick = Find.TickManager?.TicksGame ?? 0;
            if (tick > _lastDrawTick + 1) _rampStartTick = tick;
            _lastDrawTick = tick;
            var ramp = props.rampTicks > 0
                ? Mathf.Clamp01((float)(tick - _rampStartTick) / props.rampTicks)
                : 1f;

            var n = offsets.Count;
            for (var i = 0; i < n; i++)
            {
                var local = offsets[i];
                var pos = basePos + rotPos * new Vector3(local.x, 0f, local.y);
                pos.y = altitude;
                var scale = jitterEnabled ? 1f + (Rand.Value - 0.5f) * 2f * flicker : 1f;
                var size = new Vector3(flameSize.x * scale, 1f, flameSize.y * scale * ramp);
                var pivotShift = rotQuad * new Vector3((0.5f - pivot.x) * size.x, 0f, (0.5f - pivot.y) * size.z);
                var matrix = Matrix4x4.TRS(pos + pivotShift, rotQuad, size);
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
            }
        }
    }
}
