using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    // Arrival action for the (degenerate same-tile) world flight. Synchronously hands
    // off to a VehicleSkyfaller_Bombing on the destination map, then ClearAndDestroys
    // the AerialVehicleInFlight before returning. The clear-and-destroy MUST happen
    // before Arrived returns: otherwise MoveForward calls InitializeNextFlight on an
    // empty FlightPath and SetSpeed reads First[0], throwing IndexOutOfRange.
    public class ArrivalAction_BombMap : VehicleArrivalAction
    {
        protected MapParent mapParent;
        protected List<IntVec3> bombCells;
        protected Rot4 flightDir;
        protected OrdinancePattern pattern;

        protected IntVec3 returnCell;
        protected Rot4 returnRot;

        protected ThingDef bombingSkyfallerDef;
        protected OrdinanceDef ordinance;

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
            OrdinanceDef ordinance)
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
            skyfaller.totalTicks = ComputeBuzzTicks(start, end, vehicle);
            skyfaller.scatter = vehicle.GetComp<CompAirstrike>()?.Props?.scatter ?? 0f;

            GenSpawn.Spawn(skyfaller, start, map, flightDir);

            aerialVehicle?.ClearAndDestroy();
        }

        /// <summary>
        /// Larger constant = slower buzz. Bounded so pathological FlightSpeed values don't produce a one-frame blur or a 60-second camera lock.
        /// </summary>
        private const float BuzzTimeConstant = 30f;

        private static int ComputeBuzzTicks(IntVec3 start, IntVec3 end, VehiclePawn vehicle)
        {
            var flightSpeed = vehicle.CompVehicleLauncher?.FlightSpeed ?? 10f;
            if (flightSpeed <= 0f) flightSpeed = 10f;
            var distanceInCells = (end - start).LengthHorizontal;
            var ticks = Mathf.RoundToInt(distanceInCells * BuzzTimeConstant / flightSpeed);
            return Mathf.Clamp(ticks, 60, 1800);
        }
        
        /// <summary>
        /// Full-map edge-to-edge in flightDir. The buzz always enters and exits off-map for visual consistency regardless of where the target is.
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
        }
    }
}
