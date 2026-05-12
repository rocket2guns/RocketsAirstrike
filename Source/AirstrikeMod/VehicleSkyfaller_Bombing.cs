using System;
using System.Collections.Generic;
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
    // In-map bombing skyfaller. Lerps the vehicle from startCell to endCell over
    // totalTicks, drops ordnance at each bombCells entry as the moving position crosses
    // it, then routes through ExitMap (same-map: spawn arriving skyfaller; cross-map:
    // build a return AerialVehicleInFlight back to originMapParent).
    public class VehicleSkyfaller_Bombing : VehicleSkyfaller
    {
        public IntVec3 startCell;
        public IntVec3 endCell;
        public List<IntVec3> bombCells;
        public OrdinancePattern pattern = OrdinancePattern.Single;
        public OrdinanceDef ordinance;

        public IntVec3 returnCell;
        public Rot4 returnRot;

        // Null = same-map. Non-null = return-flight target for cross-map.
        public MapParent originMapParent;

        public int totalTicks = 240;
        public float visualAltitude = 6f;
        public float scatter = 0f;

        protected int ticksRunning;
        protected List<bool> bombsDropped;

        // Resolved lazily on first DrawAt and reused — DrawAt fires every frame, and
        // VehiclePawn.GetComp<T> iterates AllComps. Not serialized; rebuilt on load.
        private CompEngineFlame _engineFlame;
        private bool _engineFlameLookedUp;

        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.")]
        [UsedImplicitly]
        public VehicleSkyfaller_Bombing()
        {
        }

        protected float Progress => Mathf.Clamp01((float)ticksRunning / Math.Max(1, totalTicks));

        /// <summary>
        /// Lerped along startCell -> endCell at ground altitude. Used for the shadow and
        /// the bomb-trip cross product.
        /// </summary>
        private Vector3 GroundPos =>
            Vector3.Lerp(startCell.ToVector3Shifted(), endCell.ToVector3Shifted(), Progress);

        public override Vector3 DrawPos
        {
            get
            {
                var pos = GroundPos;
                pos.y = AltitudeLayer.Skyfaller.AltitudeFor();
                pos.z += visualAltitude;
                return pos;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var pos = DrawPos;
            vehicle.DrawAt(in pos, Rotation, 0f);
            DrawShadow();
            if (!_engineFlameLookedUp)
            {
                _engineFlame = vehicle?.GetComp<CompEngineFlame>();
                _engineFlameLookedUp = true;
            }
            _engineFlame?.DrawFlames(pos, Rotation, 0f);
        }

        private void DrawShadow()
        {
            if (cachedShadowMaterial == null && !string.IsNullOrEmpty(def.skyfaller.shadow))
                cachedShadowMaterial = MaterialPool.MatFrom(def.skyfaller.shadow, ShaderDatabase.Transparent);
            if (cachedShadowMaterial == null) return;
            var shadowSize = vehicle.VehicleGraphic?.data?.drawSize ?? def.skyfaller.shadowSize;
            var shadowTicks = Mathf.RoundToInt(visualAltitude * 10f);
            DrawDropSpotShadow(GroundPos, Rotation, cachedShadowMaterial, shadowSize, shadowTicks);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad) 
                vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLanding].ExecuteEvents();
        }

        protected override void Tick()
        {
            base.Tick();
            ticksRunning++;
            if (Spawned && Map != null && bombCells is { Count: > 0 })
            {
                EnsureDropTracker();
                var currentCell = GroundPos.ToIntVec3();
                var n = bombCells.Count;
                for (var i = 0; i < n; i++)
                {
                    if (bombsDropped[i]) continue;
                    if (PastBombCell(currentCell, bombCells[i]))
                    {
                        Detonate(bombCells[i]);
                        bombsDropped[i] = true;
                    }
                }
            }
            if (ticksRunning >= totalTicks) 
                ExitMap();
        }

        private void EnsureDropTracker()
        {
            if (bombsDropped == null || bombsDropped.Count != bombCells.Count)
            {
                bombsDropped = new List<bool>(bombCells.Count);
                for (var i = 0; i < bombCells.Count; i++)
                    bombsDropped.Add(false);
            }
        }

        /// <summary>
        /// Side-of-line cross product. Resilient to fast skyfallers skipping past in a
        /// single tick (which a simple distance check would miss).
        /// </summary>
        private bool PastBombCell(IntVec3 currentCell, IntVec3 cell)
        {
            var dx = endCell.x - startCell.x;
            var dz = endCell.z - startCell.z;
            if (dx == 0 && dz == 0) return true;

            var bombDot = (long)(cell.x - startCell.x) * dx + (long)(cell.z - startCell.z) * dz;
            var currentDot = (long)(currentCell.x - startCell.x) * dx + (long)(currentCell.z - startCell.z) * dz;
            return currentDot >= bombDot;
        }

        private static ThingDef _fallingBombDef;
        private static ThingDef FallingBombDef =>
            _fallingBombDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("RocketsAirstrike_FallingBomb");

        private void Detonate(IntVec3 cell)
        {
            if (ordinance == null) return;
            var impactCell = ApplyScatter(cell);

            var def = FallingBombDef;
            var projectileDef = ordinance.projectileDef;
            if (def == null || projectileDef == null)
            {
                DetonateInline(impactCell);
                return;
            }

            var bomb = (ProjectileSkyfaller_AirstrikeBomb)ThingMaker.MakeThing(def);
            bomb.caster = vehicle;
            bomb.ordinance = ordinance;
            bomb.projectileDef = projectileDef;
            bomb.origin = DrawPos;
            bomb.destination = impactCell.ToVector3Shifted();
            GenSpawn.Spawn(bomb, impactCell, Map);
        }

        /// <summary>
        /// Defensive fallback when the falling-bomb visual can't be set up.
        /// </summary>
        private void DetonateInline(IntVec3 cell)
        {
            var damDef = ordinance.damageDef ?? DamageDefOf.Bomb;
            GenExplosion.DoExplosion(
                center: cell,
                map: Map,
                radius: ordinance.radius,
                damType: damDef,
                instigator: vehicle,
                damAmount: Mathf.RoundToInt(ordinance.damage),
                armorPenetration: -1f,
                explosionSound: null,
                weapon: null,
                projectile: null,
                intendedTarget: null,
                postExplosionSpawnThingDef: ordinance.postExplosionSpawnThingDef,
                postExplosionSpawnChance: ordinance.postExplosionSpawnChance,
                postExplosionSpawnThingCount: ordinance.postExplosionSpawnThingCount,
                preExplosionSpawnThingDef: ordinance.preExplosionSpawnThingDef,
                preExplosionSpawnChance: ordinance.preExplosionSpawnChance,
                preExplosionSpawnThingCount: ordinance.preExplosionSpawnThingCount);
        }

        /// <summary>
        /// Linear distance distribution biases toward the target.
        /// </summary>
        private IntVec3 ApplyScatter(IntVec3 cell)
        {
            if (scatter <= 0f) return cell;
            var angle = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
            var distance = Rand.Range(0f, scatter);
            var dx = Mathf.RoundToInt(Mathf.Cos(angle) * distance);
            var dz = Mathf.RoundToInt(Mathf.Sin(angle) * distance);
            var offset = new IntVec3(cell.x + dx, cell.y, cell.z + dz);
            return offset.InBounds(Map) ? offset : cell;
        }

        protected virtual void ExitMap()
        {
            var map = Map;
            if (map == null)
            {
                Destroy();
                return;
            }
            var crossMap = originMapParent != null && originMapParent != map.Parent;
            if (crossMap)
            {
                ExitMapToWorldFlight(map);
                return;
            }
            ExitMapSameMap(map);
        }

        // Same-map: spawn a VehicleSkyfaller_Arriving directly. No return world-flight.
        private void ExitMapSameMap(Map map)
        {
            var arrivingDef = vehicle.CompVehicleLauncher.Props.skyfallerIncoming;
            if (arrivingDef == null)
            {
                Log.Warning("[Rockets.Airstrike] Vehicle has no skyfallerIncoming def; landing vehicle directly.");
                GenSpawn.Spawn(vehicle, returnCell, map, returnRot);
                Destroy();
                return;
            }
            var arriving = (VehicleSkyfaller_Arriving)
                VehicleSkyfallerMaker.MakeSkyfaller(arrivingDef, vehicle);
            arriving.rotatePostLanding = returnRot;
            var landingRot = vehicle.CompVehicleLauncher.launchProtocol.LandingProperties?.forcedRotation
                              ?? returnRot;
            GenSpawn.Spawn(arriving, returnCell, map, landingRot);
            Destroy();
            vehicle.SetSustainerTarget(arriving);
            vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLanding].ExecuteEvents();
        }

        // Cross-map: build a fresh AerialVehicleInFlight bound for the origin tile.
        private void ExitMapToWorldFlight(Map map)
        {
            if (originMapParent == null)
            {
                Log.Error("[Rockets.Airstrike] Cross-map exit requested but originMapParent is null.");
                ExitMapSameMap(map);
                return;
            }

            var aerial = AerialVehicleInFlight.Create(vehicle, map.Tile);
            var arrival = new ArrivalAction_LandToCell(vehicle, originMapParent, returnCell, returnRot);
            aerial.OrderFlyToTiles(
                new List<FlightNode> { new FlightNode(originMapParent.Tile) },
                arrival);

            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startCell, nameof(startCell));
            Scribe_Values.Look(ref endCell, nameof(endCell));
            Scribe_Collections.Look(ref bombCells, nameof(bombCells), LookMode.Value);
            Scribe_Values.Look(ref pattern, nameof(pattern), OrdinancePattern.Single);
            Scribe_Defs.Look(ref ordinance, nameof(ordinance));
            Scribe_Values.Look(ref returnCell, nameof(returnCell));
            Scribe_Values.Look(ref returnRot, nameof(returnRot));
            Scribe_References.Look(ref originMapParent, nameof(originMapParent));
            Scribe_Values.Look(ref totalTicks, nameof(totalTicks), 240);
            Scribe_Values.Look(ref scatter, nameof(scatter));
            Scribe_Values.Look(ref ticksRunning, nameof(ticksRunning));
            Scribe_Collections.Look(ref bombsDropped, nameof(bombsDropped), LookMode.Value);
        }
    }

    // Caravan fallback if the destination map disappears between launch and arrival.
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
