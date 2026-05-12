using RimWorld;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    // True while pawn is a Movement-handler in a vehicle currently marked as in-sortie.
    // Drives the Time-type RocketsAirstrike_TimeFlown record via the vanilla
    // Pawn_RecordsTracker.RecordsTickInterval path (pumped from SortieTimeComponent,
    // since pawns inside a VehiclePawn do not tick on their own).
    public class RecordWorker_OnSortie : RecordWorker
    {
        public override bool ShouldMeasureTimeNow(Pawn pawn)
        {
            foreach (var vehicle in BombingSpeedManager.Active)
            {
                if (vehicle.PawnsByHandlingType[HandlingType.Movement].Contains(pawn))
                    return true;
            }
            return false;
        }
    }
}
