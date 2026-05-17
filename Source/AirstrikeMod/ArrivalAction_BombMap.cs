using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    // For same-map strikes the world-flight leg is degenerate (origin == destination
    // tile); ClearAndDestroy MUST run synchronously, otherwise MoveForward calls
    // InitializeNextFlight on an empty FlightPath and SetSpeed reads First[0],
    // throwing IndexOutOfRange. For cross-map the flight is real but the same path
    // works once Arrived has consumed the last node.
    public class ArrivalAction_BombMap : VehicleArrivalAction, IHoverArrival
    {
        protected MapParent mapParent;
        protected List<BombingSegment> segments;
        protected OrdinancePattern pattern;

        protected IntVec3 returnCell;
        protected Rot4 returnRot;

        protected ThingDef bombingSkyfallerDef;
        protected ThingDef ordinance;
        protected float scatter;
        public float flyAltitude = 6f;
        protected float sortieSpeedMultiplier = 1f;
        protected SoundDef bombFireSound;
        protected Pawn chosenPilot;
        protected SkillDef xpSkill;

        int IHoverArrival.HoverTakeoffTicks => hoverTakeoffTicks;
        float IHoverArrival.FlyAltitude => flyAltitude;
        bool IHoverArrival.SpawnNextSkyfaller(Map map) => SpawnBombingSkyfaller(map);

        // Strafing-only: when set, the skyfaller fires Projectile.Launch(...) from its
        // DrawPos toward each bombCell, with firing leading the plane by strafingLeadCells.
        protected ThingDef strafingProjectileDef;
        protected int strafingLeadCells;
        protected SoundDef strafingFireSound;
        protected int strafingBulletsPerRound = 1;
        protected int strafingSpreadCells;
        protected int strafingFireOriginOffset = 3;
        protected int strafingRunWidth = 1;

        // Null = same-map. Non-null = cross-map: the bombing skyfaller's ExitMap builds
        // a return AerialVehicleInFlight back to this MapParent.
        protected MapParent originMapParent;

        public IntVec3? inPlaceAnchor;
        public Rot4? inPlaceForward;
        public int hoverApproachCells = 5;
        public int hoverTakeoffTicks = 90;
        public int hoverLandingTicks = 90;

        public override bool DestroyOnArrival => false;

        public ArrivalAction_BombMap()
        {
        }

        public ArrivalAction_BombMap(
            VehiclePawn vehicle,
            MapParent mapParent,
            List<BombingSegment> segments,
            OrdinancePattern pattern,
            IntVec3 returnCell,
            Rot4 returnRot,
            ThingDef bombingSkyfallerDef,
            ThingDef ordinance,
            float scatter = 0f,
            MapParent originMapParent = null,
            float flyAltitude = 6f,
            float sortieSpeedMultiplier = 1f,
            SoundDef bombFireSound = null,
            Pawn chosenPilot = null,
            SkillDef xpSkill = null,
            ThingDef strafingProjectileDef = null,
            int strafingLeadCells = 0,
            SoundDef strafingFireSound = null,
            int strafingBulletsPerRound = 1,
            int strafingSpreadCells = 0,
            int strafingFireOriginOffset = 3,
            int strafingRunWidth = 1,
            IntVec3? inPlaceAnchor = null,
            Rot4? inPlaceForward = null,
            int hoverApproachCells = 5,
            int hoverTakeoffTicks = 90,
            int hoverLandingTicks = 90)
            : base(vehicle)
        {
            this.mapParent = mapParent;
            this.segments = segments;
            this.pattern = pattern;
            this.returnCell = returnCell;
            this.returnRot = returnRot;
            this.bombingSkyfallerDef = bombingSkyfallerDef;
            this.ordinance = ordinance;
            this.scatter = scatter;
            this.originMapParent = originMapParent;
            this.flyAltitude = flyAltitude;
            this.sortieSpeedMultiplier = sortieSpeedMultiplier;
            this.bombFireSound = bombFireSound;
            this.chosenPilot = chosenPilot;
            this.xpSkill = xpSkill;
            this.strafingProjectileDef = strafingProjectileDef;
            this.strafingLeadCells = strafingLeadCells;
            this.strafingFireSound = strafingFireSound;
            this.strafingBulletsPerRound = strafingBulletsPerRound;
            this.strafingSpreadCells = strafingSpreadCells;
            this.strafingFireOriginOffset = strafingFireOriginOffset;
            this.strafingRunWidth = strafingRunWidth;
            this.inPlaceAnchor = inPlaceAnchor;
            this.inPlaceForward = inPlaceForward;
            this.hoverApproachCells = hoverApproachCells;
            this.hoverTakeoffTicks = hoverTakeoffTicks;
            this.hoverLandingTicks = hoverLandingTicks;
        }

        public override void Arrived(GlobalTargetInfo target)
        {
            base.Arrived(target);

            if (mapParent == null)
            {
                Log.Error("[Rockets.Airstrike] BombMap arrival: mapParent is null.");
                AerialVehicle?.SwitchToCaravan();
                return;
            }

            var aerialVehicle = vehicle.GetAerialVehicle();
            var map = mapParent.Map;
            if (map == null)
            {
                Log.Error("[Rockets.Airstrike] BombMap arrival: mapParent has no Map.");
                aerialVehicle?.SwitchToCaravan();
                return;
            }

            if (!SpawnBombingSkyfaller(map))
            {
                aerialVehicle?.SwitchToCaravan();
                return;
            }

            aerialVehicle?.ClearAndDestroy();
        }

        // Called by both the world-flight arrival path and the in-place
        // hover-launch path; returns false if validation/creation failed.
        public bool SpawnBombingSkyfaller(Map map)
        {
            if (map == null) return false;
            if (segments == null || segments.Count == 0
                || segments[0].bombCells == null || segments[0].bombCells.Count == 0)
            {
                Log.Error("[Rockets.Airstrike] BombMap arrival: no segments / bomb cells.");
                return false;
            }

            var skyfaller = (VehicleSkyfaller_Bombing)
                VehicleSkyfallerMaker.MakeSkyfaller(bombingSkyfallerDef, vehicle);
            if (skyfaller == null)
            {
                Log.Error("[Rockets.Airstrike] Failed to create bombing skyfaller. Check thingClass on " +
                          bombingSkyfallerDef?.defName);
                return false;
            }

            skyfaller.segments = segments;
            skyfaller.segmentIdx = 0;
            skyfaller.pattern = pattern;
            skyfaller.ordinance = ordinance;
            skyfaller.returnCell = returnCell;
            skyfaller.returnRot = returnRot;
            skyfaller.originMapParent = originMapParent;
            skyfaller.scatter = scatter;
            skyfaller.visualAltitude = flyAltitude;
            skyfaller.sortieSpeedMultiplier = sortieSpeedMultiplier;
            skyfaller.bombFireSound = bombFireSound;
            skyfaller.chosenPilot = chosenPilot;
            skyfaller.xpSkill = xpSkill;
            skyfaller.strafingProjectileDef = strafingProjectileDef;
            skyfaller.leadCells = strafingLeadCells;
            skyfaller.strafingFireSound = strafingFireSound;
            skyfaller.strafingBulletsPerRound = strafingBulletsPerRound;
            skyfaller.strafingSpreadCells = strafingSpreadCells;
            skyfaller.strafingFireOriginOffset = strafingFireOriginOffset;
            skyfaller.strafingRunWidth = strafingRunWidth;
            skyfaller.inPlaceMode = inPlaceAnchor.HasValue;
            skyfaller.hoverLandingTicks = hoverLandingTicks;

            IntVec3 spawnCell;
            Rot4 spawnRot;
            // In-place sorties always go spline so entry/exit anchor consistently.
            var splineEligible = inPlaceAnchor.HasValue
                || (segments.Count > 1
                    && (pattern == OrdinancePattern.Single
                        || pattern == OrdinancePattern.Line
                        || pattern == OrdinancePattern.Strafing));
            if (splineEligible)
            {
                BuildPolyline(map, segments, inPlaceAnchor, inPlaceForward, hoverApproachCells,
                    out var waypoints, out var waypointIsDrop);
                skyfaller.waypoints = waypoints;
                skyfaller.waypointIsDrop = waypointIsDrop;
                skyfaller.traveled = 0f;
                skyfaller.startCell = waypoints[0];
                skyfaller.endCell = waypoints[waypoints.Count - 1];
                skyfaller.bombCells = null;
                skyfaller.totalTicks = ComputeBuzzTicks(waypoints[0],
                    waypoints[waypoints.Count - 1], vehicle);
                spawnCell = waypoints[0];
                spawnRot = segments[0].flightDir;
            }
            else
            {
                ChooseFlightLine(map, segments[0].bombCells[0], segments[0].flightDir,
                    out var start, out var end);
                skyfaller.startCell = start;
                skyfaller.endCell = end;
                skyfaller.bombCells = segments[0].bombCells;
                skyfaller.totalTicks = ComputeBuzzTicks(start, end, vehicle);
                spawnCell = start;
                spawnRot = segments[0].flightDir;
            }

            GenSpawn.Spawn(skyfaller, spawnCell, map, spawnRot);
            return true;
        }

        private const int POLYLINE_EDGE_MARGIN = 1;

        private static void BuildPolyline(Map map, List<BombingSegment> segments,
            IntVec3? inPlaceAnchor, Rot4? inPlaceForward, int hoverApproachCells,
            out List<IntVec3> waypoints, out List<bool> waypointIsDrop)
        {
            waypoints = new List<IntVec3>();
            waypointIsDrop = new List<bool>();
            if (segments.Count == 0) return;

            var inner = new List<IntVec3>();
            for (var seg = 0; seg < segments.Count; seg++)
            {
                var s = segments[seg];
                if (s?.bombCells == null || s.bombCells.Count == 0) continue;
                if (s.bombCells.Count == 1)
                {
                    inner.Add(s.bombCells[0]);
                }
                else
                {
                    inner.Add(s.bombCells[0]);
                    inner.Add(s.bombCells[s.bombCells.Count - 1]);
                }
            }
            if (inner.Count == 0) return;

            if (inPlaceAnchor.HasValue && inPlaceForward.HasValue)
            {
                var anchor = inPlaceAnchor.Value;
                var fwd = inPlaceForward.Value.FacingCell;
                var n = Mathf.Max(1, hoverApproachCells);
                var forwardNode = new IntVec3(
                    anchor.x + fwd.x * n, anchor.y, anchor.z + fwd.z * n);
                var behindNode = new IntVec3(
                    anchor.x - fwd.x * n, anchor.y, anchor.z - fwd.z * n);

                waypoints.Add(anchor); waypointIsDrop.Add(false);
                waypoints.Add(forwardNode); waypointIsDrop.Add(false);
                for (var i = 0; i < inner.Count; i++)
                {
                    waypoints.Add(inner[i]);
                    waypointIsDrop.Add(true);
                }
                waypoints.Add(behindNode); waypointIsDrop.Add(false);
                waypoints.Add(anchor); waypointIsDrop.Add(false);
                return;
            }

            var entryFrom = inner.Count >= 2 ? inner[1] : inner[0];
            var exitFrom = inner.Count >= 2 ? inner[inner.Count - 2] : inner[inner.Count - 1];
            var entry = ExtrapolateToEdge(map, entryFrom, inner[0]);
            var exit = ExtrapolateToEdge(map, exitFrom, inner[inner.Count - 1]);

            waypoints.Add(entry); waypointIsDrop.Add(false);
            for (var i = 0; i < inner.Count; i++)
            {
                waypoints.Add(inner[i]);
                waypointIsDrop.Add(true);
            }
            waypoints.Add(exit); waypointIsDrop.Add(false);
        }

        private static IntVec3 ExtrapolateToEdge(Map map, IntVec3 from, IntVec3 through)
        {
            var dx = through.x - from.x;
            var dz = through.z - from.z;
            var sx = map.Size.x;
            var sz = map.Size.z;
            if (dx == 0 && dz == 0)
            {
                return new IntVec3(sx - 1 - POLYLINE_EDGE_MARGIN, through.y, through.z);
            }
            var tCandidates = new float[4];
            tCandidates[0] = dx != 0 ? (POLYLINE_EDGE_MARGIN - through.x) / (float)dx : float.PositiveInfinity;
            tCandidates[1] = dx != 0 ? (sx - 1 - POLYLINE_EDGE_MARGIN - through.x) / (float)dx : float.PositiveInfinity;
            tCandidates[2] = dz != 0 ? (POLYLINE_EDGE_MARGIN - through.z) / (float)dz : float.PositiveInfinity;
            tCandidates[3] = dz != 0 ? (sz - 1 - POLYLINE_EDGE_MARGIN - through.z) / (float)dz : float.PositiveInfinity;
            var tBest = float.PositiveInfinity;
            for (var i = 0; i < tCandidates.Length; i++)
                if (tCandidates[i] > 0f && tCandidates[i] < tBest) tBest = tCandidates[i];
            if (float.IsInfinity(tBest) || tBest <= 0f) return through;
            var x = Mathf.Clamp(Mathf.RoundToInt(through.x + tBest * dx), 0, sx - 1);
            var z = Mathf.Clamp(Mathf.RoundToInt(through.z + tBest * dz), 0, sz - 1);
            return new IntVec3(x, through.y, z);
        }

        /// <summary>
        /// Larger constant = slower buzz. Bounded so pathological FlightSpeed values
        /// don't produce a one-frame blur or a 60-second camera lock.
        /// </summary>
        private const float BUZZ_TIME_CONSTANT = 30f;

        public static int ComputeBuzzTicks(IntVec3 start, IntVec3 end, VehiclePawn vehicle)
        {
            var flightSpeed = vehicle.CompVehicleLauncher?.FlightSpeed ?? 10f;
            if (flightSpeed <= 0f) flightSpeed = 10f;
            var distanceInCells = (end - start).LengthHorizontal;
            var ticks = Mathf.RoundToInt(distanceInCells * BUZZ_TIME_CONSTANT / flightSpeed);
            return Mathf.Clamp(ticks, 60, 1800);
        }

        /// <summary>
        /// Full-map edge-to-edge in flightDir. The buzz always enters and exits off-map
        /// for visual consistency regardless of where the target is.
        /// </summary>
        public static void ChooseFlightLine(Map map, IntVec3 anchor, Rot4 dir,
            out IntVec3 start, out IntVec3 end)
        {
            var sx = map.Size.x;
            var sz = map.Size.z;
            if (dir == Rot4.North)
            {
                var x = Mathf.Clamp(anchor.x, 1, sx - 2);
                start = new IntVec3(x, 0, 0);
                end = new IntVec3(x, 0, sz - 1);
            }
            else if (dir == Rot4.South)
            {
                var x = Mathf.Clamp(anchor.x, 1, sx - 2);
                start = new IntVec3(x, 0, sz - 1);
                end = new IntVec3(x, 0, 0);
            }
            else if (dir == Rot4.West)
            {
                var z = Mathf.Clamp(anchor.z, 1, sz - 2);
                start = new IntVec3(sx - 1, 0, z);
                end = new IntVec3(0, 0, z);
            }
            else
            {
                var z = Mathf.Clamp(anchor.z, 1, sz - 2);
                start = new IntVec3(0, 0, z);
                end = new IntVec3(sx - 1, 0, z);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mapParent, nameof(mapParent));
            Scribe_Collections.Look(ref segments, nameof(segments), LookMode.Deep);
            Scribe_Values.Look(ref pattern, nameof(pattern));
            Scribe_Values.Look(ref returnCell, nameof(returnCell));
            Scribe_Values.Look(ref returnRot, nameof(returnRot));
            Scribe_Defs.Look(ref bombingSkyfallerDef, nameof(bombingSkyfallerDef));
            Scribe_Defs.Look(ref ordinance, nameof(ordinance));
            Scribe_Values.Look(ref scatter, nameof(scatter));
            Scribe_References.Look(ref originMapParent, nameof(originMapParent));
            Scribe_Values.Look(ref flyAltitude, nameof(flyAltitude), 6f);
            Scribe_Values.Look(ref sortieSpeedMultiplier, nameof(sortieSpeedMultiplier), 1f);
            Scribe_Defs.Look(ref bombFireSound, nameof(bombFireSound));
            Scribe_References.Look(ref chosenPilot, nameof(chosenPilot));
            Scribe_Defs.Look(ref xpSkill, nameof(xpSkill));
            Scribe_Defs.Look(ref strafingProjectileDef, nameof(strafingProjectileDef));
            Scribe_Values.Look(ref strafingLeadCells, nameof(strafingLeadCells));
            Scribe_Defs.Look(ref strafingFireSound, nameof(strafingFireSound));
            Scribe_Values.Look(ref strafingBulletsPerRound, nameof(strafingBulletsPerRound), 1);
            Scribe_Values.Look(ref strafingSpreadCells, nameof(strafingSpreadCells));
            Scribe_Values.Look(ref strafingFireOriginOffset, nameof(strafingFireOriginOffset), 3);
            Scribe_Values.Look(ref strafingRunWidth, nameof(strafingRunWidth), 1);
            Scribe_Values.Look(ref inPlaceAnchor, nameof(inPlaceAnchor));
            Scribe_Values.Look(ref inPlaceForward, nameof(inPlaceForward));
            Scribe_Values.Look(ref hoverApproachCells, nameof(hoverApproachCells), 5);
            Scribe_Values.Look(ref hoverTakeoffTicks, nameof(hoverTakeoffTicks), 90);
            Scribe_Values.Look(ref hoverLandingTicks, nameof(hoverLandingTicks), 90);
        }
    }
}
