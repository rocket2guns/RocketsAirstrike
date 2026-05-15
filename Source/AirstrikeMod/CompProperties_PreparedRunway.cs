using System.Collections.Generic;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompProperties_PreparedRunway : VehicleCompProperties
    {
        public bool requiresHeavy = true;
        public List<TerrainDef> requiredTerrain;
        public List<TerrainDef> excludedTerrain;

        public CompProperties_PreparedRunway()
        {
            compClass = typeof(CompPreparedRunway);
        }
    }
}
