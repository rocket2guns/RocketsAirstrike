using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace AirstrikeMod.Patches
{
    [HarmonyPatch(typeof(VehicleDef), nameof(VehicleDef.SpecialDisplayStats))]
    internal static class VehicleDef_PreparedRunwayStats_Patch
    {
        private static StatCategoryDef _runwayCategory;
        private static StatCategoryDef RunwayCategory =>
            _runwayCategory ??= DefDatabase<StatCategoryDef>.GetNamedSilentFail("ROCKET_Runway");

        [HarmonyPostfix]
        private static IEnumerable<VehicleStatDrawEntry> Postfix(
            IEnumerable<VehicleStatDrawEntry> values, VehicleDef __instance)
        {
            foreach (var entry in values) yield return entry;

            CompProperties_PreparedRunway props = null;
            if (__instance.comps != null)
            {
                foreach (var t in __instance.comps)
                {
                    if (t is not CompProperties_PreparedRunway p) continue;
                    props = p;
                    break;
                }
            }
            if (props == null) yield break;

            var category = RunwayCategory;
            if (category == null) yield break;

            yield return new VehicleStatDrawEntry(
                category,
                "ROCKET_RunwayInfo_RequiresHeavy_Label".Translate(),
                props.requiresHeavy ? "Yes".Translate() : "No".Translate(),
                "ROCKET_RunwayInfo_RequiresHeavy_Desc".Translate(),
                50);

            if (!props.requiredTerrain.NullOrEmpty())
            {
                yield return new VehicleStatDrawEntry(
                    category,
                    "ROCKET_RunwayInfo_RequiredTerrain_Label".Translate(),
                    JoinTerrainLabels(props.requiredTerrain),
                    "ROCKET_RunwayInfo_RequiredTerrain_Desc".Translate(),
                    49);
            }

            var excludedJoined = JoinExcluded(props.excludedTerrain, props.excludedDesignators);
            if (excludedJoined != null)
            {
                yield return new VehicleStatDrawEntry(
                    category,
                    "ROCKET_RunwayInfo_ExcludedTerrain_Label".Translate(),
                    excludedJoined,
                    "ROCKET_RunwayInfo_ExcludedTerrain_Desc".Translate(),
                    48);
            }
        }

        private static string JoinExcluded(
            List<TerrainDef> terrains, List<DesignatorDropdownGroupDef> designators)
        {
            var hasTerrains = !terrains.NullOrEmpty();
            var hasDesignators = !designators.NullOrEmpty();
            if (!hasTerrains && !hasDesignators) return null;

            var sb = new StringBuilder();
            var first = true;
            if (hasDesignators)
            {
                for (var i = 0; i < designators.Count; i++)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(designators[i].LabelCap);
                    first = false;
                }
            }
            if (hasTerrains)
            {
                for (var i = 0; i < terrains.Count; i++)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(terrains[i].LabelCap);
                    first = false;
                }
            }
            return sb.ToString();
        }

        private static string JoinTerrainLabels(List<TerrainDef> defs)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < defs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(defs[i].LabelCap);
            }
            return sb.ToString();
        }
    }
}
