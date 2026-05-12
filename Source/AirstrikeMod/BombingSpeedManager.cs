using System.Collections.Generic;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    // Process-global, not save-persisted
    public static class BombingSpeedManager
    {
        private static readonly Dictionary<VehiclePawn, Rot4> ActiveSorties = new();

        internal static Dictionary<VehiclePawn, Rot4>.KeyCollection Active => ActiveSorties.Keys;

        public static void MarkFast(VehiclePawn vehicle, Rot4 originalRotation)
        {
            if (vehicle != null) ActiveSorties[vehicle] = originalRotation;
        }

        public static bool IsFast(VehiclePawn vehicle)
        {
            return vehicle != null && ActiveSorties.ContainsKey(vehicle);
        }

        public static bool TryGetOriginalRotation(VehiclePawn vehicle, out Rot4 rot)
        {
            if (vehicle != null) return ActiveSorties.TryGetValue(vehicle, out rot);
            rot = default;
            return false;
        }

        public static void UnmarkFast(VehiclePawn vehicle)
        {
            if (vehicle != null) ActiveSorties.Remove(vehicle);
        }
    }
}
