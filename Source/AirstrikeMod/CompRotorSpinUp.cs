using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompRotorSpinUp : VehicleComp
    {
        public CompProperties_RotorSpinUp Props => (CompProperties_RotorSpinUp)props;

        private float _currentRate;
        private bool _wasSpinning;

        public float TargetRate => Props.targetRotationRate;

        // Per-tick rate / TickRateMultiplier keeps per-real-time rotation constant,
        // so fast-forward doesn't alias to a fixed multiple of rotor symmetry.
        public static float ScaleForGameSpeed(float baseRate)
        {
            var mult = Find.TickManager?.TickRateMultiplier ?? 1f;
            return mult > 0f ? baseRate / mult : 0f;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Vehicle == null || !Vehicle.Spawned) return;

            var drafted = Vehicle.ignition?.Drafted == true;
            if (!drafted && _currentRate <= 0f && !_wasSpinning) return;

            var targetRate = drafted ? Props.targetRotationRate : 0f;
            var step = Props.targetRotationRate / Mathf.Max(1, Props.spinUpTicks);

            if (Mathf.Abs(_currentRate - targetRate) <= step)
                _currentRate = targetRate;
            else if (_currentRate < targetRate)
                _currentRate += step;
            else
                _currentRate -= step;

            var spinning = _currentRate > 0f;
            if (spinning)
                Vehicle.DrawTracker?.overlayRenderer?.SetAcceleration(ScaleForGameSpeed(_currentRate));
            else if (_wasSpinning)
                LockRotorsToDefault();
            _wasSpinning = spinning;
        }

        // VF's Transform.rotation accumulates and is never reset; snap to 0 once on
        // spin-down so asymmetric overlays (e.g. Apache mast) rest in their default pose.
        private void LockRotorsToDefault()
        {
            var overlays = Vehicle?.DrawTracker?.overlayRenderer?.AllOverlaysListForReading;
            if (overlays == null) return;
            for (var i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                if (o.Graphic is Graphic_Rotator)
                    o.Transform.rotation = 0f;
            }
        }
    }
}
