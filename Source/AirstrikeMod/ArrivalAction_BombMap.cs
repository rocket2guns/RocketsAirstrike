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
    public class ArrivalAction_BombMap : VehicleArrivalAction
    {
        protected MapParent mapParent;
        protected List<IntVec3> bombCells;
        protected Rot4 flightDir;
        protected OrdinancePattern pattern;

        protected IntVec3 returnCell;
        protected Rot4 returnRot;

        protected ThingDef bombingSkyfallerDef;
        protected ThingDef ordinance;
        protected float scatter;
        protected float flyAltitude = 6f;
        protected float buzzSpeedMultiplier = 1f;
        protected int buzzSpeedRampCells = 3;

        // Strafing-only: when set, the skyfaller fires Projectile.Launch(...) from its
        // DrawPos toward each bombCell, with firing leading the plane by strafingLeadCells.
        protected ThingDef strafingProjectileDef;
        protected int strafingLeadCells;
        protected SoundDef strafingFireSound;
        protected int strafingBulletsPerRound = 1;
        protected int strafingSpreadCells;
        protected int strafingFireOriginOffset = 3;

        // Null = same-map. Non-null = cross-map: the bombing skyfaller's ExitMap builds
        // a return AerialVehicleInFlight back to this MapParent.
        protected MapParent originMapParent;

        public override bool DestroyOnArrival => false;

        public ArrivalAction_BombMap()
        {
        }

        public ArrivalAction_BombMap(
            VehiclePawn vehicle,
            MapParent mapParent,
            List<IntVec3> bombCells,
            Rot4 flightDir,
            OrdinancePattern pattern,
            IntVec3 returnCell,
            Rot4 returnRot,
            ThingDef bombingSkyfallerDef,
            ThingDef ordinance,
            float scatter = 0f,
            MapParent originMapParent = null,
            float flyAltitude = 6f,
            float buzzSpeedMultiplier = 1f,
            int buzzSpeedRampCells = 3,
            ThingDef strafingProjectileDef = null,
            int strafingLeadCells = 0,
            SoundDef strafingFireSound = null,
            int strafingBulletsPerRound = 1,
            int strafingSpreadCells = 0,
            int strafingFireOriginOffset = 3)
            : base(vehicle)
        {
            this.mapParent = mapParent;
            this.bombCells = bombCells;
            this.flightDir = flightDir;
            this.pattern = pattern;
            this.returnCell = returnCell;
            this.returnRot = returnRot;
            this.bombingSkyfallerDef = bombingSkyfallerDef;
            this.ordinance = ordinance;
            this.scatter = scatter;
            this.originMapParent = originMapParent;
            this.flyAltitude = flyAltitude;
            this.buzzSpeedMultiplier = buzzSpeedMultiplier;
            this.buzzSpeedRampCells = buzzSpeedRampCells;
            this.strafingProjectileDef = strafingProjectileDef;
            this.strafingLeadCells = strafingLeadCells;
            this.strafingFireSound = strafingFireSound;
            this.strafingBulletsPerRound = strafingBulletsPerRound;
            this.strafingSpreadCells = strafingSpreadCells;
            this.strafingFireOriginOffset = strafingFireOriginOffset;
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

            if (bombCells == null || bombCells.Count == 0)
            {
                Log.Error("[Rockets.Airstrike] BombMap arrival: no bomb cells.");
                aerialVehicle?.SwitchToCaravan();
                return;
            }

            ChooseFlightLine(map, bombCells[0], flightDir, out var start, out var end);

            var skyfaller = (VehicleSkyfaller_Bombing)
                VehicleSkyfallerMaker.MakeSkyfaller(bombingSkyfallerDef, vehicle);

            if (skyfaller == null)
            {
                Log.Error("[Rockets.Airstrike] Failed to create bombing skyfaller. Check thingClass on " +
                          bombingSkyfallerDef?.defName);
                aerialVehicle?.SwitchToCaravan();
                return;
            }

            skyfaller.startCell = start;
            skyfaller.endCell = end;
            skyfaller.bombCells = bombCells;
            skyfaller.pattern = pattern;
            skyfaller.ordinance = ordinance;
            skyfaller.returnCell = returnCell;
            skyfaller.returnRot = returnRot;
            skyfaller.originMapParent = originMapParent;
            skyfaller.totalTicks = ComputeBuzzTicks(start, end, vehicle);
            skyfaller.scatter = scatter;
            skyfaller.visualAltitude = flyAltitude;
            skyfaller.buzzSpeedMultiplier = buzzSpeedMultiplier;
            skyfaller.buzzSpeedRampCells = buzzSpeedRampCells;
            skyfaller.strafingProjectileDef = strafingProjectileDef;
            skyfaller.leadCells = strafingLeadCells;
            skyfaller.strafingFireSound = strafingFireSound;
            skyfaller.strafingBulletsPerRound = strafingBulletsPerRound;
            skyfaller.strafingSpreadCells = strafingSpreadCells;
            skyfaller.strafingFireOriginOffset = strafingFireOriginOffset;

            GenSpawn.Spawn(skyfaller, start, map, flightDir);

            aerialVehicle?.ClearAndDestroy();
        }

        /// <summary>
        /// Larger constant = slower buzz. Bounded so pathological FlightSpeed values
        /// don't produce a one-frame blur or a 60-second camera lock.
        /// </summary>
        private const float BUZZ_TIME_CONSTANT = 30f;

        private static int ComputeBuzzTicks(IntVec3 start, IntVec3 end, VehiclePawn vehicle)
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
        private static void ChooseFlightLine(Map map, IntVec3 anchor, Rot4 dir,
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
            Scribe_Collections.Look(ref bombCells, nameof(bombCells), LookMode.Value);
            Scribe_Values.Look(ref flightDir, nameof(flightDir));
            Scribe_Values.Look(ref pattern, nameof(pattern));
            Scribe_Values.Look(ref returnCell, nameof(returnCell));
            Scribe_Values.Look(ref returnRot, nameof(returnRot));
            Scribe_Defs.Look(ref bombingSkyfallerDef, nameof(bombingSkyfallerDef));
            Scribe_Defs.Look(ref ordinance, nameof(ordinance));
            Scribe_Values.Look(ref scatter, nameof(scatter));
            Scribe_References.Look(ref originMapParent, nameof(originMapParent));
            Scribe_Values.Look(ref flyAltitude, nameof(flyAltitude), 6f);
            Scribe_Values.Look(ref buzzSpeedMultiplier, nameof(buzzSpeedMultiplier), 1f);
            Scribe_Values.Look(ref buzzSpeedRampCells, nameof(buzzSpeedRampCells), 3);
            Scribe_Defs.Look(ref strafingProjectileDef, nameof(strafingProjectileDef));
            Scribe_Values.Look(ref strafingLeadCells, nameof(strafingLeadCells));
            Scribe_Defs.Look(ref strafingFireSound, nameof(strafingFireSound));
            Scribe_Values.Look(ref strafingBulletsPerRound, nameof(strafingBulletsPerRound), 1);
            Scribe_Values.Look(ref strafingSpreadCells, nameof(strafingSpreadCells));
            Scribe_Values.Look(ref strafingFireOriginOffset, nameof(strafingFireOriginOffset), 3);
        }
    }
}
