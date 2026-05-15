using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class LaunchRestriction_PreparedRunway : LaunchRestriction_Runway
    {
        public override bool CanStartProtocol(VehiclePawn vehicle, Map map, IntVec3 position,
            Rot4 rot)
        {
            if (!base.CanStartProtocol(vehicle, map, position, rot)) return false;
            var comp = vehicle?.GetComp<CompPreparedRunway>();
            if (comp == null) return true;
            return comp.ValidateRunway(map, ComputeRunwayRect(position, rot));
        }

        public override void DrawRestrictionsTargeter(VehiclePawn vehicle, Map map,
            IntVec3 position, Rot4 rot)
        {
            base.DrawRestrictionsTargeter(vehicle, map, position, rot);
            var comp = vehicle?.GetComp<CompPreparedRunway>();
            if (comp == null) return;
            comp.DrawTerrainFailures(map, ComputeRunwayRect(position, rot), colorInvalid);
        }

        private CellRect ComputeRunwayRect(IntVec3 position, Rot4 rot)
        {
            var adjWidth = width;
            var adjHeight = height;
            if (rot == Rot4.West)
            {
                adjWidth.x = -width.x;
                adjWidth.z = -width.z;
            }
            else if (rot == Rot4.South)
            {
                adjHeight.x = -height.x;
                adjHeight.z = -height.z;
            }
            return CellRect.FromLimits(position.x + adjWidth.x, position.z + adjHeight.x,
                position.x + adjWidth.z, position.z + adjHeight.z);
        }
    }
}
