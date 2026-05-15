using UnityEngine;
using Vehicles;

namespace AirstrikeMod
{
    public class CompRotorSpinUp : VehicleComp
    {
        public CompProperties_RotorSpinUp Props => (CompProperties_RotorSpinUp)props;

        private float _currentRate;

        public override void CompTick()
        {
            base.CompTick();
            if (Vehicle == null || !Vehicle.Spawned) return;

            var drafted = Vehicle.ignition?.Drafted == true;
            var targetRate = drafted ? Props.targetRotationRate : 0f;
            var step = Props.targetRotationRate / Mathf.Max(1, Props.spinUpTicks);

            if (Mathf.Abs(_currentRate - targetRate) <= step)
                _currentRate = targetRate;
            else if (_currentRate < targetRate)
                _currentRate += step;
            else
                _currentRate -= step;

            if (_currentRate > 0f)
                Vehicle.DrawTracker?.overlayRenderer?.SetAcceleration(_currentRate);
        }
    }
}
