using System;
using JetBrains.Annotations;
using RimWorld;
using RimWorld.Planet;
using SmashTools;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// In-map bombing skyfaller. Animates the vehicle from <see cref="startCell"/> to
    /// <see cref="endCell"/> over <see cref="totalTicks"/>, drops a bomb when the moving
    /// position passes <see cref="bombCell"/>, then spawns a vanilla
    /// <see cref="VehicleSkyfaller_Arriving"/> at <see cref="returnCell"/> for landing.
    ///
    /// Rendering: we call <c>vehicle.DrawAt</c> directly instead of going through
    /// <c>launchProtocol.Draw</c>. The protocol path applies takeoff/landing animation
    /// curves on top of our position, which is fine for vehicles whose end-of-takeoff
    /// curves are small (Mosquito's PropellerTakeoff: x+1, z+15) but disastrous for
    /// vehicles whose curves are large (Warbird's DirectionalTakeoff: x+250 — plane
    /// renders 250 cells east of where we put it, off the map).
    ///
    /// We still call <c>base.Tick</c> every tick so <c>launchProtocol.Tick</c> fires —
    /// <c>PropellerTakeoff.TickTakeoff</c> calls <c>vehicle.DrawTracker.overlayRenderer
    /// .SetAcceleration(...)</c> per tick, and that one call both sets and advances the
    /// rotor's rotation. Skipping it freezes the rotor.
    /// </summary>
    public class VehicleSkyfaller_Bombing : VehicleSkyfaller
    {
        public IntVec3 startCell;
        public IntVec3 endCell;
        public IntVec3 bombCell;
        public float bombRadius = 4.5f;
        public float bombDamage = 120f;

        public IntVec3 returnCell;
        public Rot4 returnRot;

        public int totalTicks = 240;
        public float visualAltitude = 6f;

        protected int ticksRunning;
        protected bool bombDropped;

        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.")]
        [UsedImplicitly]
        public VehicleSkyfaller_Bombing()
        {
        }

        protected float Progress => Mathf.Clamp01((float)ticksRunning / Math.Max(1, totalTicks));

        // Position directly under the vehicle (shadow target). Lerped along the buzz line
        // at the actual map ground altitude — no visual-altitude offset.
        private Vector3 GroundPos
        {
            get
            {
                Vector3 a = startCell.ToVector3Shifted();
                Vector3 b = endCell.ToVector3Shifted();
                return Vector3.Lerp(a, b, Progress);
            }
        }

        // Where the vehicle sprite is drawn — GroundPos plus a +z offset for the iso
        // "altitude" illusion (in RimWorld's iso projection, +z shifts the sprite up the
        // screen). The shadow stays at GroundPos so the vehicle appears to hover.
        public override Vector3 DrawPos
        {
            get
            {
                Vector3 pos = GroundPos;
                pos.y = AltitudeLayer.Skyfaller.AltitudeFor();
                pos.z += visualAltitude;
                return pos;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Vector3 pos = DrawPos;
            // Always render East since the buzz line is always west→east. Some launch
            // protocols (notably DirectionalTakeoff) leave forcedRotation pointing at
            // whichever runway direction was used for takeoff — using that here gives a
            // backwards-facing plane half the time.
            vehicle.DrawAt(in pos, Rot8.East, 0f);

            DrawShadow();
        }

        private void DrawShadow()
        {
            if (cachedShadowMaterial == null && !string.IsNullOrEmpty(def.skyfaller.shadow))
            {
                cachedShadowMaterial = MaterialPool.MatFrom(def.skyfaller.shadow, ShaderDatabase.Transparent);
            }
            if (cachedShadowMaterial == null) return;

            // Shadow rendered at the ground position (no altitude offset). Pass a
            // ticksToLand value proportional to visualAltitude so the shadow scales
            // appropriately — formula in DrawDropSpotShadow is scale = 1 + ticks/100.
            int shadowTicks = Mathf.RoundToInt(visualAltitude * 10f);
            DrawDropSpotShadow(GroundPos, Rotation, cachedShadowMaterial, def.skyfaller.shadowSize, shadowTicks);
        }

        protected override void Tick()
        {
            // PropellerTakeoff.TickTakeoff calls overlayRenderer.SetAcceleration each
            // tick, which both sets speed AND advances the rotor's rotation. Skipping
            // base.Tick freezes the rotor.
            base.Tick();

            ticksRunning++;

            if (!bombDropped && Spawned && Map != null)
            {
                if (PastBombCell(GroundPos.ToIntVec3()))
                {
                    DropBomb();
                    bombDropped = true;
                }
            }

            if (ticksRunning >= totalTicks)
            {
                ExitMap();
            }
        }

        // Side-of-line cross product. Trips the moment the moving position crosses the
        // perpendicular through bombCell along the start→end vector.
        private bool PastBombCell(IntVec3 currentCell)
        {
            int dx = endCell.x - startCell.x;
            int dz = endCell.z - startCell.z;
            if (dx == 0 && dz == 0) return true;

            long bombDot = (long)(bombCell.x - startCell.x) * dx + (long)(bombCell.z - startCell.z) * dz;
            long currentDot = (long)(currentCell.x - startCell.x) * dx + (long)(currentCell.z - startCell.z) * dz;
            return currentDot >= bombDot;
        }

        protected virtual void DropBomb()
        {
            GenExplosion.DoExplosion(
                center: bombCell,
                map: Map,
                radius: bombRadius,
                damType: DamageDefOf.Bomb,
                instigator: vehicle,
                damAmount: Mathf.RoundToInt(bombDamage),
                armorPenetration: -1f,
                explosionSound: null,
                weapon: null,
                projectile: null,
                intendedTarget: null);
        }

        protected virtual void ExitMap()
        {
            Map map = Map;
            if (map == null)
            {
                Destroy();
                return;
            }

            ThingDef arrivingDef = vehicle.CompVehicleLauncher.Props.skyfallerIncoming;
            if (arrivingDef == null)
            {
                Log.Warning("[Rockets.Airstrike] Vehicle has no skyfallerIncoming def; landing vehicle directly.");
                GenSpawn.Spawn(vehicle, returnCell, map, returnRot);
                Destroy();
                return;
            }

            VehicleSkyfaller_Arriving arriving = (VehicleSkyfaller_Arriving)
                VehicleSkyfallerMaker.MakeSkyfaller(arrivingDef, vehicle);
            arriving.rotatePostLanding = returnRot;
            Rot4 landingRot = vehicle.CompVehicleLauncher.launchProtocol.LandingProperties?.forcedRotation
                              ?? returnRot;
            GenSpawn.Spawn(arriving, returnCell, map, landingRot);

            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startCell, nameof(startCell));
            Scribe_Values.Look(ref endCell, nameof(endCell));
            Scribe_Values.Look(ref bombCell, nameof(bombCell));
            Scribe_Values.Look(ref bombRadius, nameof(bombRadius));
            Scribe_Values.Look(ref bombDamage, nameof(bombDamage));
            Scribe_Values.Look(ref returnCell, nameof(returnCell));
            Scribe_Values.Look(ref returnRot, nameof(returnRot));
            Scribe_Values.Look(ref totalTicks, nameof(totalTicks), 240);
            Scribe_Values.Look(ref ticksRunning, nameof(ticksRunning));
            Scribe_Values.Look(ref bombDropped, nameof(bombDropped));
        }
    }

    /// <summary>
    /// Fallback used when we somehow lose the origin map reference at world-flight arrival.
    /// </summary>
    internal class ArrivalAction_LandInMap_NoOp : VehicleArrivalAction
    {
        public override bool DestroyOnArrival => true;

        public override void Arrived(GlobalTargetInfo target)
        {
            base.Arrived(target);
            AerialVehicle?.SwitchToCaravan();
        }
    }
}
