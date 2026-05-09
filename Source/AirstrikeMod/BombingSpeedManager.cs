using System.Collections.Generic;
using Vehicles;

namespace AirstrikeMod
{
    // Process-global, not save-persisted. Vehicles get marked at airstrike launch and
    // unmarked when their landing skyfaller's FinalizeLanding fires (via Harmony postfix
    // in LaunchProtocol_TickPatches). Without that postfix, a vehicle that did one strike
    // would have fast takeoff/landing on every subsequent vanilla launch too.
    public static class BombingSpeedManager
    {
        private static readonly HashSet<VehiclePawn> FastVehicles = new HashSet<VehiclePawn>();

        public static void MarkFast(VehiclePawn vehicle)
        {
            if (vehicle != null) FastVehicles.Add(vehicle);
        }

        public static bool IsFast(VehiclePawn vehicle)
        {
            return vehicle != null && FastVehicles.Contains(vehicle);
        }

        public static void UnmarkFast(VehiclePawn vehicle)
        {
            if (vehicle != null) FastVehicles.Remove(vehicle);
        }
    }
}
