using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    public class ArrivalAction_LocalFlight : VehicleArrivalAction, IHoverArrival
    {
        protected ThingDef transitSkyfallerDef;

        public IntVec3 originCell;
        public Rot4 originForward;
        public IntVec3 destCell;
        public Rot4 destForward;

        public float flyAltitude = 6f;
        public int hoverTakeoffTicks = 90;
        public int hoverLandingTicks = 90;
        public int hoverApproachCells = 5;
        public float sortieSpeedMultiplier = 1f;
        protected Pawn chosenPilot;

        public override bool DestroyOnArrival => true;

        int IHoverArrival.HoverTakeoffTicks => hoverTakeoffTicks;
        float IHoverArrival.FlyAltitude => flyAltitude;

        public ArrivalAction_LocalFlight()
        {
        }

        public ArrivalAction_LocalFlight(
            VehiclePawn vehicle,
            ThingDef transitSkyfallerDef,
            IntVec3 originCell,
            Rot4 originForward,
            IntVec3 destCell,
            Rot4 destForward,
            float flyAltitude,
            int hoverTakeoffTicks,
            int hoverLandingTicks,
            int hoverApproachCells,
            float sortieSpeedMultiplier,
            Pawn chosenPilot)
            : base(vehicle)
        {
            this.transitSkyfallerDef = transitSkyfallerDef;
            this.originCell = originCell;
            this.originForward = originForward;
            this.destCell = destCell;
            this.destForward = destForward;
            this.flyAltitude = flyAltitude;
            this.hoverTakeoffTicks = hoverTakeoffTicks;
            this.hoverLandingTicks = hoverLandingTicks;
            this.hoverApproachCells = hoverApproachCells;
            this.sortieSpeedMultiplier = sortieSpeedMultiplier;
            this.chosenPilot = chosenPilot;
        }

        public override void Arrived(GlobalTargetInfo target)
        {
            base.Arrived(target);
            Log.Warning("[Rockets.Airstrike] LocalFlight arrival reached the world-flight path; falling back to caravan.");
            AerialVehicle?.SwitchToCaravan();
        }

        public bool SpawnNextSkyfaller(Map map)
        {
            if (map == null) return false;
            if (transitSkyfallerDef == null)
            {
                Log.Error("[Rockets.Airstrike] LocalFlight: transitSkyfallerDef is null.");
                return false;
            }

            var skyfaller = (VehicleSkyfaller_Bombing)
                VehicleSkyfallerMaker.MakeSkyfaller(transitSkyfallerDef, vehicle);
            if (skyfaller == null)
            {
                Log.Error("[Rockets.Airstrike] LocalFlight: failed to create transit skyfaller. Check thingClass on "
                          + transitSkyfallerDef.defName);
                return false;
            }

            BuildPolyline(out var waypoints, out var waypointIsDrop);

            skyfaller.segments = null;
            skyfaller.segmentIdx = 0;
            skyfaller.pattern = OrdinancePattern.Single;
            skyfaller.ordinance = null;
            skyfaller.returnCell = destCell;
            skyfaller.returnRot = destForward;
            skyfaller.originMapParent = null;
            skyfaller.scatter = 0f;
            skyfaller.visualAltitude = flyAltitude;
            skyfaller.sortieSpeedMultiplier = sortieSpeedMultiplier;
            skyfaller.bombFireSound = null;
            skyfaller.chosenPilot = chosenPilot;
            skyfaller.xpSkill = null;
            skyfaller.strafingProjectileDef = null;
            skyfaller.leadCells = 0;
            skyfaller.strafingFireSound = null;
            skyfaller.strafingBulletsPerRound = 1;
            skyfaller.strafingSpreadCells = 0;
            skyfaller.strafingFireOriginOffset = 3;
            skyfaller.strafingRunWidth = 1;
            skyfaller.inPlaceMode = true;
            skyfaller.hoverLandingTicks = hoverLandingTicks;

            skyfaller.waypoints = waypoints;
            skyfaller.waypointIsDrop = waypointIsDrop;
            skyfaller.traveled = 0f;
            skyfaller.startCell = waypoints[0];
            skyfaller.endCell = waypoints[waypoints.Count - 1];
            skyfaller.bombCells = null;
            skyfaller.totalTicks = ArrivalAction_BombMap.ComputeBuzzTicks(
                waypoints[0], waypoints[waypoints.Count - 1], vehicle);

            GenSpawn.Spawn(skyfaller, originCell, map, originForward);
            return true;
        }

        // 4-point spline: endpoint tangents mirror the missing neighbor in ComputeWaypointTangents,
        // so takeoff/landing direction emerges from the +N*fwd / -N*fwd anchors.
        private void BuildPolyline(out List<IntVec3> waypoints, out List<bool> waypointIsDrop)
        {
            var n = Mathf.Max(1, hoverApproachCells);
            var origFwd = originForward.FacingCell;
            var destFwd = destForward.FacingCell;
            var forwardNode = new IntVec3(
                originCell.x + origFwd.x * n, originCell.y, originCell.z + origFwd.z * n);
            var behindNode = new IntVec3(
                destCell.x - destFwd.x * n, destCell.y, destCell.z - destFwd.z * n);

            waypoints = new List<IntVec3>(4) { originCell, forwardNode, behindNode, destCell };
            waypointIsDrop = new List<bool>(4) { false, false, false, false };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref transitSkyfallerDef, nameof(transitSkyfallerDef));
            Scribe_Values.Look(ref originCell, nameof(originCell));
            Scribe_Values.Look(ref originForward, nameof(originForward));
            Scribe_Values.Look(ref destCell, nameof(destCell));
            Scribe_Values.Look(ref destForward, nameof(destForward));
            Scribe_Values.Look(ref flyAltitude, nameof(flyAltitude), 6f);
            Scribe_Values.Look(ref hoverTakeoffTicks, nameof(hoverTakeoffTicks), 90);
            Scribe_Values.Look(ref hoverLandingTicks, nameof(hoverLandingTicks), 90);
            Scribe_Values.Look(ref hoverApproachCells, nameof(hoverApproachCells), 5);
            Scribe_Values.Look(ref sortieSpeedMultiplier, nameof(sortieSpeedMultiplier), 1f);
            Scribe_References.Look(ref chosenPilot, nameof(chosenPilot));
        }
    }
}
