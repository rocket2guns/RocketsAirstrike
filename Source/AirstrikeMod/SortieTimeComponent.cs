using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class SortieTimeComponent : GameComponent
    {
        public SortieTimeComponent(Game game) { }

        public override void GameComponentTick()
        {
            // pumps the vanilla record-tick path for pilots whose vehicle is in a sortie.
            foreach (var vehicle in BombingSpeedManager.Active)
            {
                var pilots = vehicle.PawnsByHandlingType[HandlingType.Movement];
                for (var i = 0; i < pilots.Count; i++)
                    pilots[i].records.RecordsTickInterval(1);
            }
        }
    }
}
