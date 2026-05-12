using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class SortieTimeComponent : GameComponent
    {
        public SortieTimeComponent(Game game) { }

        public override void GameComponentTick()
        {
            // Pumps the vanilla record-tick path for pilots whose vehicle is in a
            // sortie. RecordsTickInterval iterates Time-type records and consults
            // each RecordDef.Worker — our RecordWorker_OnSortie gates the
            // RocketsAirstrike_TimeFlown record on sortie state.
            foreach (var vehicle in BombingSpeedManager.Active)
            {
                var pilots = vehicle.PawnsByHandlingType[HandlingType.Movement];
                for (var i = 0; i < pilots.Count; i++)
                    pilots[i].records.RecordsTickInterval(1);
            }
        }
    }
}
