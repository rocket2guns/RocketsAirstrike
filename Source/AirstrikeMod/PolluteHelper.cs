using RimWorld;
using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    internal static class PolluteHelper
    {
        public static void PolluteCellsInRadius(Map map, IntVec3 center, float radius, float chance)
        {
            if (!ModsConfig.BiotechActive) return;
            if (map?.pollutionGrid == null) return;
            if (chance <= 0f || radius <= 0f) return;

            var radSq = radius * radius;
            var minX = Mathf.FloorToInt(center.x - radius);
            var maxX = Mathf.CeilToInt(center.x + radius);
            var minZ = Mathf.FloorToInt(center.z - radius);
            var maxZ = Mathf.CeilToInt(center.z + radius);

            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map)) continue;
                    var dx = c.x - center.x;
                    var dz = c.z - center.z;
                    var distSq = dx * dx + dz * dz;
                    if (distSq > radSq) continue;
                    if (map.pollutionGrid.IsPolluted(c)) continue;
                    var t = Mathf.Sqrt(distSq) / radius;
                    var effectiveChance = chance * (1f - t);
                    if (!Rand.Chance(effectiveChance)) continue;
                    map.pollutionGrid.SetPolluted(c, true);
                }
            }
        }
    }
}
