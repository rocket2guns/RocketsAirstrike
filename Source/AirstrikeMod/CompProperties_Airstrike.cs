using System.Collections.Generic;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompProperties_Airstrike : VehicleCompProperties
    {
        public ThingDef skyfallerBombing;

        /// <summary>
        /// Null = all patterns allowed.
        /// </summary>
        public List<OrdinancePattern> allowedPatterns;

        /// <summary>
        /// Per-bomb random offset radius in cells. 0 = perfectly accurate.
        /// </summary>
        public float scatter = 0f;

        public CompProperties_Airstrike()
        {
            compClass = typeof(CompAirstrike);
        }
    }
}
