using System.Collections.Generic;
using Vehicles;

namespace AirstrikeMod
{
    // Process-global, not save-persisted. Marked at airstrike launch, unmarked by the
    // LaunchProtocol_TickPatches FinalizeLanding postfix; without that the flag would
    // bleed into every subsequent vanilla launch by the same vehicle.
    public static class BombingSpeedManager
    {
        private static readonly HashSet<VehiclePawn> FastVehicles = new();

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
