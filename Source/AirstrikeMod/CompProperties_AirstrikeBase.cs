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

        /// <summary>
        /// Additional scatter potential applied based on pilot targeting ability.
        /// </summary>
        public float skillScatter = 0f;

        /// <summary>
        /// Skyfaller draw altitude during the buzz.
        /// </summary>
        public float flyAltitude = 6f;

        /// <summary>
        /// Scales the plane's traversal speed during the buzz.
        /// </summary>
        public float buzzSpeedMultiplier = 1f;

        /// <summary>
        /// Cells over which the plane smoothly accelerates/decelerates
        /// </summary>
        public int buzzSpeedRampCells = 3;
    }
}
