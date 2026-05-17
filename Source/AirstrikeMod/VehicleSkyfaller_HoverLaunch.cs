using System;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class VehicleSkyfaller_HoverLaunch : VehicleSkyfaller_Leaving
    {
        private int _ticks;
        public int riseTicks = 90;

        private float _sortieAltCache = -1f;
        private CompRotorSpinUp _rotorSpinUp;
        private bool _rotorSpinUpLookedUp;

        [UsedImplicitly]
        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.", error: true)]
        public VehicleSkyfaller_HoverLaunch()
        {
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad) return;
            if (arrivalAction is IHoverArrival hover)
            {
                riseTicks = Math.Max(1, hover.HoverTakeoffTicks);
                _sortieAltCache = hover.FlyAltitude;
            }
        }

        protected override void Tick()
        {
            _ticks++;
            TickRotor();
            if (_ticks >= riseTicks)
                LeaveMap();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var altitude = CurrentAltitude();
            var pos = RootPos;
            pos.y = AltitudeLayer.Skyfaller.AltitudeFor();
            pos.z += altitude;
            launchProtocolDrawPos = pos;
            vehicle.DrawAt(in pos, Rotation, 0f);
            DrawShadow(altitude);
        }

        private void DrawShadow(float altitude)
        {
            if (cachedShadowMaterial == null && !string.IsNullOrEmpty(def.skyfaller.shadow))
                cachedShadowMaterial = MaterialPool.MatFrom(def.skyfaller.shadow,
                    ShaderDatabase.Transparent);
            if (cachedShadowMaterial == null) return;
            var size = vehicle.VehicleGraphic?.data?.drawSize ?? def.skyfaller.shadowSize;
            AirstrikeShadow.Draw(RootPos, size, Rotation.AsAngle, altitude,
                cachedShadowMaterial);
        }

        private float CurrentAltitude()
        {
            if (_sortieAltCache < 0f)
                _sortieAltCache = (arrivalAction as IHoverArrival)?.FlyAltitude ?? 0f;
            var t = Mathf.Clamp01((float)_ticks / Mathf.Max(1, riseTicks));
            return _sortieAltCache * Mathf.SmoothStep(0f, 1f, t);
        }

        private void TickRotor()
        {
            if (!_rotorSpinUpLookedUp)
            {
                _rotorSpinUp = vehicle?.GetComp<CompRotorSpinUp>();
                _rotorSpinUpLookedUp = true;
            }
            if (_rotorSpinUp == null) return;
            vehicle.DrawTracker?.overlayRenderer?.SetAcceleration(
                CompRotorSpinUp.ScaleForGameSpeed(_rotorSpinUp.TargetRate));
        }

        protected override void LeaveMap()
        {
            vehicle.CompVehicleLauncher.launchProtocol.Release();
            vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLeftMap].ExecuteEvents();

            var map = Map;
            var hover = arrivalAction as IHoverArrival;
            if (hover != null && map != null && hover.SpawnNextSkyfaller(map))
            {
                Destroy();
                return;
            }

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
            Scribe_Values.Look(ref _ticks, nameof(_ticks));
            Scribe_Values.Look(ref riseTicks, nameof(riseTicks), 90);
        }
    }
}
