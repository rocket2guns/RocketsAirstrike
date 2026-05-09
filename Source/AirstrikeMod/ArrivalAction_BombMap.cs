using JetBrains.Annotations;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// Arrival action that fires on the destination map and hands off to a
    /// <see cref="VehicleSkyfaller_Bombing"/>. The world-flight leg is intentionally
    /// degenerate (same source and destination tile), so this fires almost immediately
    /// after takeoff. We synchronously spawn the bombing skyfaller and ClearAndDestroy
    /// the AerialVehicleInFlight inside this method — see CLAUDE.md "Same-tile arrival
    /// hazard" for why both must happen before <c>Arrived</c> returns.
    /// </summary>
    [PublicAPI]
    public class ArrivalAction_BombMap : VehicleArrivalAction
    {
        protected MapParent mapParent;
        protected IntVec3 bombCell;

        protected IntVec3 returnCell;
        protected Rot4 returnRot;

        protected ThingDef bombingSkyfallerDef;
        protected float bombRadius;
        protected float bombDamage;

        public override bool DestroyOnArrival => false; // we hand off to the skyfaller

        public ArrivalAction_BombMap()
        {
        }

        public ArrivalAction_BombMap(
            VehiclePawn vehicle,
            MapParent mapParent,
            IntVec3 bombCell,
            IntVec3 returnCell,
            Rot4 returnRot,
            ThingDef bombingSkyfallerDef,
            float bombRadius,
            float bombDamage)
            : base(vehicle)
        {
            this.mapParent = mapParent;
            this.bombCell = bombCell;
            this.returnCell = returnCell;
            this.returnRot = returnRot;
            this.bombingSkyfallerDef = bombingSkyfallerDef;
            this.bombRadius = bombRadius;
            this.bombDamage = bombDamage;
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

            AerialVehicleInFlight aerialVehicle = vehicle.GetAerialVehicle();

            Map map = mapParent.Map;
            if (map == null)
            {
                Log.Error("[Rockets.Airstrike] BombMap arrival: mapParent has no Map.");
                aerialVehicle?.SwitchToCaravan();
                return;
            }

            ChooseFlightLine(map, bombCell, out IntVec3 start, out IntVec3 end, out Rot4 facingRot);

            VehicleSkyfaller_Bombing skyfaller = (VehicleSkyfaller_Bombing)
                VehicleSkyfallerMaker.MakeSkyfaller(bombingSkyfallerDef, vehicle);

            if (skyfaller == null)
            {
                Log.Error("[Rockets.Airstrike] Failed to create bombing skyfaller. " +
                          "Check thingClass on " + bombingSkyfallerDef?.defName);
                aerialVehicle?.SwitchToCaravan();
                return;
            }

            skyfaller.startCell = start;
            skyfaller.endCell = end;
            skyfaller.bombCell = bombCell;
            skyfaller.bombRadius = bombRadius;
            skyfaller.bombDamage = bombDamage;
            skyfaller.returnCell = returnCell;
            skyfaller.returnRot = returnRot;

            // Spawn rotation determines which directional sprite the vehicle uses during
            // the buzz. Match it to the direction of motion so the cockpit points forward.
            GenSpawn.Spawn(skyfaller, start, map, facingRot);

            // Must run before Arrived returns. Otherwise the AerialVehicleInFlight ticks
            // once more, MoveForward calls InitializeNextFlight on an empty FlightPath,
            // SetSpeed reads First[] and throws IndexOutOfRange.
            aerialVehicle?.ClearAndDestroy();
        }

        // Enter from the west edge, exit through the east, both at bombCell.z.
        // Helicopter takeoff/landing protocols force East rotation (e.g. PropellerTakeoff
        // for the Mosquito), and rendering reuses launch-protocol forcedRotation, so
        // motion direction matches the rendered facing.
        private static void ChooseFlightLine(Map map, IntVec3 bomb,
            out IntVec3 start, out IntVec3 end, out Rot4 facingRot)
        {
            int z = Mathf.Clamp(bomb.z, 1, map.Size.z - 2);
            start = new IntVec3(0, 0, z);
            end = new IntVec3(map.Size.x - 1, 0, z);
            facingRot = Rot4.East;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mapParent, nameof(mapParent));
            Scribe_Values.Look(ref bombCell, nameof(bombCell));
            Scribe_Values.Look(ref returnCell, nameof(returnCell));
            Scribe_Values.Look(ref returnRot, nameof(returnRot));
            Scribe_Defs.Look(ref bombingSkyfallerDef, nameof(bombingSkyfallerDef));
            Scribe_Values.Look(ref bombRadius, nameof(bombRadius));
            Scribe_Values.Look(ref bombDamage, nameof(bombDamage));
        }
    }
}
