using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompPreparedRunway : VehicleComp
    {
        public CompProperties_PreparedRunway Props => (CompProperties_PreparedRunway)props;

        private static readonly List<IntVec3> _invalidCellBuffer = new();

        private string _lastFailureReason;

        public string LastFailureReason => _lastFailureReason;

        public bool ValidateRunway(Map map, CellRect rect)
        {
            if (map == null)
            {
                _lastFailureReason = null;
                return true;
            }

            string firstFailLabel = null;
            var failCount = 0;
            var outOfBounds = false;

            foreach (var cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    outOfBounds = true;
                    continue;
                }
                var terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain == null) continue;
                if (!IsTerrainAllowed(terrain))
                {
                    failCount++;
                    if (firstFailLabel == null) firstFailLabel = terrain.label;
                }
            }

            if (outOfBounds)
            {
                _lastFailureReason = "ROCKET_PreparedRunwayFailure_OutOfBounds".Translate();
                return false;
            }
            if (failCount > 0)
            {
                _lastFailureReason = "ROCKET_PreparedRunwayFailure".Translate(failCount, firstFailLabel);
                return false;
            }
            _lastFailureReason = null;
            return true;
        }

        public void DrawTerrainFailures(Map map, CellRect rect, Color colorInvalid)
        {
            if (map == null) return;
            _invalidCellBuffer.Clear();
            foreach (var cell in rect)
            {
                if (!cell.InBounds(map)) continue;
                var terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain == null) continue;
                if (!IsTerrainAllowed(terrain)) _invalidCellBuffer.Add(cell);
            }
            if (_invalidCellBuffer.Count > 0)
                GenDraw.DrawFieldEdges(_invalidCellBuffer, colorInvalid);
        }

        public bool IsTerrainAllowed(TerrainDef terrain)
        {
            var props = Props;
            if (props.excludedTerrain != null && props.excludedTerrain.Contains(terrain)) return false;
            if (props.excludedDesignators != null && terrain.designatorDropdown != null
                && props.excludedDesignators.Contains(terrain.designatorDropdown)) return false;
            if (props.requiredTerrain != null && props.requiredTerrain.Contains(terrain)) return true;
            if (props.requiresHeavy && HasHeavyAffordance(terrain)) return true;
            return false;
        }

        private static bool HasHeavyAffordance(TerrainDef terrain)
        {
            if (terrain.affordances == null) return false;
            return terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy);
        }

        public override string CompInspectStringExtra()
        {
            if (Vehicle is not { Spawned: true }) return null;
            return string.IsNullOrEmpty(_lastFailureReason) ? null : _lastFailureReason;
        }
    }
}
