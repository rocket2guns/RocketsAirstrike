using Vehicles;

namespace AirstrikeMod
{
    public class CompProperties_RotorSpinUp : VehicleCompProperties
    {
        public float targetRotationRate = 30f;
        public int spinUpTicks = 120;

        public CompProperties_RotorSpinUp()
        {
            compClass = typeof(CompRotorSpinUp);
        }
    }
}
