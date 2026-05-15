using System;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class VehicleSkyfaller_HoverLanding : VehicleSkyfaller_Arriving
    {
        private int _ticks;
        public int descentTicks = 90;
        public float visualAltitude = 6f;

        private CompRotorSpinUp _rotorSpinUp;
        private bool _rotorSpinUpLookedUp;

        [UsedImplicitly]
        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.", error: true)]
        public VehicleSkyfaller_HoverLanding()
        {
        }

        protected override void Tick()
        {
            _ticks++;
            TickRotor();
            if (_ticks >= descentTicks && Position.InBounds(Map))
                FinalizeLanding();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var altitude = CurrentAltitude();
            var pos = RootPos;
            pos.y = AltitudeLayer.Skyfaller.AltitudeFor();
            pos.z += altitude;
            launchProtocolDrawPos = pos;
            vehicle.DrawAt(in pos, LandingRotation, 0f);
            DrawShadow(altitude);
        }

        private void DrawShadow(float altitude)
        {
            if (cachedShadowMaterial == null && !string.IsNullOrEmpty(def.skyfaller.shadow))
                cachedShadowMaterial = MaterialPool.MatFrom(def.skyfaller.shadow,
                    ShaderDatabase.Transparent);
            if (cachedShadowMaterial == null) return;
            var size = vehicle.VehicleGraphic?.data?.drawSize ?? def.skyfaller.shadowSize;
            AirstrikeShadow.Draw(RootPos, size, LandingRotation.AsAngle, altitude,
                cachedShadowMaterial);
        }

        private float CurrentAltitude()
        {
            var t = Mathf.Clamp01((float)_ticks / Mathf.Max(1, descentTicks));
            return visualAltitude * Mathf.SmoothStep(1f, 0f, t);
        }

        private void TickRotor()
        {
            if (!_rotorSpinUpLookedUp)
            {
                _rotorSpinUp = vehicle?.GetComp<CompRotorSpinUp>();
                _rotorSpinUpLookedUp = true;
            }
            if (_rotorSpinUp == null) return;
            vehicle.DrawTracker?.overlayRenderer?.SetAcceleration(_rotorSpinUp.TargetRate);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _ticks, nameof(_ticks));
            Scribe_Values.Look(ref descentTicks, nameof(descentTicks), 90);
            Scribe_Values.Look(ref visualAltitude, nameof(visualAltitude), 6f);
        }
    }
}
