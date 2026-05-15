using System;
using JetBrains.Annotations;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class VehicleSkyfaller_HoverLaunch : VehicleSkyfaller_Leaving
    {
        private int _hoverPhaseTicks = -1;
        private int _ownTicks;
        private int _delayRemaining = -1;
        private float _launchCurveEndZ = -1f;
        private float _sortieAltCache = -1f;

        [UsedImplicitly]
        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.", error: true)]
        public VehicleSkyfaller_HoverLaunch()
        {
        }

        protected override void Tick()
        {
            EnsureTimingCached();

            if (_delayRemaining > 0)
            {
                _delayRemaining--;
                return;
            }

            vehicle.CompVehicleLauncher.launchProtocol.Tick();
            _ownTicks++;

            if (_ownTicks >= _hoverPhaseTicks)
                LeaveMap();
        }

        private void EnsureTimingCached()
        {
            if (_hoverPhaseTicks >= 0) return;

            var protocol = vehicle.CompVehicleLauncher?.launchProtocol;
            if (_delayRemaining < 0)
                _delayRemaining = protocol?.CurAnimationProperties?.delayByTicks ?? 0;

            if (protocol is PropellerTakeoff prop)
            {
                var pp = prop.LaunchProperties_Propeller;
                _hoverPhaseTicks = (pp?.maxTicksPropeller ?? 0) + (pp?.maxTicksVertical ?? 0);
            }
            else
            {
                Log.Warning("[Rockets.Airstrike] HoverLaunch on a non-PropellerTakeoff vehicle; " +
                            "transitioning to bombing skyfaller after a short default delay.");
                _hoverPhaseTicks = 120;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            launchProtocolDrawPos = ScaleAltitudeToSortie(launchProtocolDrawPos);
        }

        private Vector3 ScaleAltitudeToSortie(Vector3 pos)
        {
            if (_sortieAltCache < 0f)
                _sortieAltCache = (arrivalAction as ArrivalAction_BombMap)?.flyAltitude ?? 0f;
            if (_sortieAltCache <= 0f) return pos;
            var endZ = GetLaunchCurveEndZ();
            if (endZ <= 0.0001f) return pos;
            var rootZ = RootPos.z;
            pos.z = rootZ + (pos.z - rootZ) * (_sortieAltCache / endZ);
            return pos;
        }

        private float GetLaunchCurveEndZ()
        {
            if (_launchCurveEndZ >= 0f) return _launchCurveEndZ;
            _launchCurveEndZ = 0f;
            if (vehicle?.CompVehicleLauncher?.launchProtocol is PropellerTakeoff prop)
            {
                var props = prop.LaunchProperties_Propeller;
                if (props != null)
                {
                    if (props.zPositionPropellerCurve != null)
                        _launchCurveEndZ += props.zPositionPropellerCurve.Evaluate(1f);
                    if (props.zPositionVerticalCurve != null)
                        _launchCurveEndZ += props.zPositionVerticalCurve.Evaluate(1f);
                }
            }
            return _launchCurveEndZ;
        }

        protected override void LeaveMap()
        {
            vehicle.CompVehicleLauncher.launchProtocol.Release();
            vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLeftMap].ExecuteEvents();

            var map = Map;
            var bombArrival = arrivalAction as ArrivalAction_BombMap;
            if (bombArrival != null && map != null && bombArrival.SpawnBombingSkyfaller(map))
            {
                Destroy();
                return;
            }

            // Recovery: drop the vehicle back on the map so it isn't orphaned in
            // a destroyed skyfaller (createWorldObject=false means base.LeaveMap
            // would just Destroy with no respawn).
            Log.Error("[Rockets.Airstrike] HoverLaunch handoff failed; respawning vehicle in place.");
            if (map != null)
            {
                GenSpawn.Spawn(vehicle, Position, map, Rotation);
                vehicle.CompVehicleLauncher.inFlight = false;
            }
            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _ownTicks, nameof(_ownTicks));
            Scribe_Values.Look(ref _delayRemaining, nameof(_delayRemaining), -1);
        }
    }
}
