using Verse;

namespace AirstrikeMod
{
    public interface IHoverArrival
    {
        int HoverTakeoffTicks { get; }
        float FlyAltitude { get; }

        /// <summary>
        /// Spawns the follow-up skyfaller after hover-launch rise completes.
        /// Returns false on validation/creation failure so the caller can fall
        /// back to a safe in-place respawn.
        /// </summary>
        bool SpawnNextSkyfaller(Map map);
    }
}
