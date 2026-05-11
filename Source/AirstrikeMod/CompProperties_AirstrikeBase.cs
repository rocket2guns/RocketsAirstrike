using Vehicles;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// Shared properties for all airstrike-mode comps. Not referenced directly from
    /// XML; concrete subclasses set <c>compClass</c>.
    /// </summary>
    public abstract class CompProperties_AirstrikeBase : VehicleCompProperties
    {
        public ThingDef skyfallerBombing;

        /// <summary>
        /// Per-bomb random offset radius in cells. 0 = perfectly accurate.
        /// </summary>
        public float scatter = 0f;
    }
}
