using System.Collections.Generic;
using Vehicles;

namespace AirstrikeMod
{
    /// <summary>
    /// Tracks which vehicles have a "fast takeoff/landing" flag set, used by
    /// <see cref="Patches.LaunchProtocol_TickPatches"/> to advance an extra tick per real
    /// tick. Lifted from the Local Flight mod.
    ///
    /// The HashSet is process-global and not save-persisted: it's only valid for the
    /// duration of a single airstrike run. <see cref="VehicleSkyfaller_Bombing.ExitMap"/>
    /// removes the flag on the return leg so landing plays at normal speed (unless the
    /// global setting opts back in).
    /// </summary>
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
