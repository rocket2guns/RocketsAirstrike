using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using SmashTools.Targeting;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    public abstract class CompAirstrikeBase : VehicleComp
    {
        public CompProperties_AirstrikeBase BaseProps => (CompProperties_AirstrikeBase)props;

        private ThingDef selectedOrdinance;
        private bool showAllOrdinance = true;

        private static readonly FieldInfo SubGraphicsField = typeof(Graphic_Collection)
            .GetField("subGraphics", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<ThingDef, Texture2D> _singleVariantCache = new();
        protected static Texture2D GetSingleVariantIcon(ThingDef def)
        {
            if (def == null) return null;
            if (_singleVariantCache.TryGetValue(def, out var cached))
                return cached ?? def.uiIcon;
            Texture2D tex = null;
            if (def.graphic is Graphic_Collection coll
                && SubGraphicsField?.GetValue(coll) is Graphic[] { Length: > 0 } subs)
            {
                tex = subs[0].MatSingle?.mainTexture as Texture2D;
            }
            _singleVariantCache[def] = tex;
            return tex ?? def.uiIcon;
        }

        public static bool BombTargetingActive;
        public static Map BombTargetingMap;
        public static float BombTargetingRadius;

        private const float ABSOLUTE_MAX_SCATTER = 16f;
        public const float XP_PER_MUNITION = 50f;

        private static readonly (float minAbility, string labelKey, string hex)[] RatingBuckets =
        {
            (1.00f, "ROCKET_Rating_Excellent", "#4CFF4C"),
            (0.75f, "ROCKET_Rating_Good",      "#2E9E2E"),
            (0.50f, "ROCKET_Rating_Average",   "#E0E040"),
            (0.25f, "ROCKET_Rating_Poor",      "#E08040"),
            (0.00f, "ROCKET_Rating_VeryPoor",  "#E04040"),
        };

        public (Pawn pilot, float ability) BestPilotAndAbility()
        {
            var pilots = Vehicle.PawnsByHandlingType[HandlingType.Movement];
            if (pilots == null || pilots.Count == 0) return (null, 0f);

            var skill = BaseProps.requiredSkill;
            if (skill == null) return (pilots[0], 1f);

            Pawn best = null;
            var bestLevel = -1;
            for (var i = 0; i < pilots.Count; i++)
            {
                var p = pilots[i];
                var record = p.skills?.GetSkill(skill);
                if (record == null || record.TotallyDisabled) continue;
                if (record.Level > bestLevel)
                {
                    best = p;
                    bestLevel = record.Level;
                }
            }
            if (best == null) return (null, 0f);
            return (best, Mathf.Clamp01(bestLevel / 20f));
        }

        protected float BestTargetingAbility() => BestPilotAndAbility().ability;

        private static float ResolveScatterFloat(float baseScatter, float skillScatter, float ability,
            float flyAltitude)
        {
            var floor = Mathf.Max(0f, baseScatter);
            var ceiling = Mathf.Min(flyAltitude, ABSOLUTE_MAX_SCATTER);
            if (skillScatter <= 0f) return Mathf.Min(floor, ceiling);
            var deficit = 1f - Mathf.Clamp01(ability);
            return Mathf.Min(floor + skillScatter * deficit, ceiling);
        }

        protected float ResolveBombScatter()
        {
            return ResolveScatterFloat(
                BaseProps.scatter, BaseProps.skillScatter,
                BestTargetingAbility(), BaseProps.flyAltitude);
        }

        protected int ResolveStrafingSpread(int baseSpread)
        {
            return Mathf.RoundToInt(ResolveScatterFloat(
                baseSpread, BaseProps.skillScatter,
                BestTargetingAbility(), BaseProps.flyAltitude));
        }

        protected void SetTargetingCursor(string firstLine)
        {
            CursorLabel.Current = firstLine;
            var (rating, color) = TargetingRating();
            var hex = ColorUtility.ToHtmlStringRGB(color);
            CursorLabel.SecondLine =
                $"{"ROCKET_TargetingAccuracy".Translate()}: <color=#{hex}>{rating}</color>";
        }

        private const string SKILL_HIGHLIGHT_HEX = "#bb8f04";

        protected string BuildRequiredSkillDescLine()
        {
            var skill = BaseProps.requiredSkill;
            if (skill == null) return "";
            var coloredSkill = $"<color={SKILL_HIGHLIGHT_HEX}>{skill.LabelCap}</color>";
            return $"\n\n{"ROCKET_RequiredSkillLine".Translate(coloredSkill)}";
        }

        protected virtual string BuildTargetingAccuracyDescLine()
        {
            string rating;
            Color color;
            if (PilotsBlockLaunch(out _))
            {
                rating = "ROCKET_Rating_None".Translate();
                color = Color.red;
            }
            else
            {
                (rating, color) = TargetingRating();
            }
            var hex = ColorUtility.ToHtmlStringRGB(color);
            return $"\n\n{"ROCKET_TargetingAccuracy".Translate()}: <color=#{hex}>{rating}</color>";
        }

        protected (string label, Color color) TargetingRating()
        {
            var ability = BestTargetingAbility();
            for (var i = 0; i < RatingBuckets.Length; i++)
            {
                if (ability >= RatingBuckets[i].minAbility)
                {
                    var b = RatingBuckets[i];
                    ColorUtility.TryParseHtmlString(b.hex, out var col);
                    return (b.labelKey.Translate(), col);
                }
            }
            return (RatingBuckets[RatingBuckets.Length - 1].labelKey.Translate(), Color.red);
        }

        protected bool PilotsBlockLaunch(out string reason)
        {
            reason = null;
            var pilots = Vehicle.PawnsByHandlingType[HandlingType.Movement];
            if (pilots == null || pilots.Count == 0)
            {
                reason = "ROCKET_NoPilots".Translate();
                return true;
            }

            var allZeroManip = true;
            for (var i = 0; i < pilots.Count; i++)
            {
                var manip = pilots[i].health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 0f;
                if (manip > 0f) { allZeroManip = false; break; }
            }
            if (allZeroManip)
            {
                reason = "ROCKET_PilotZeroManipulation".Translate();
                return true;
            }

            var skill = BaseProps.requiredSkill;
            if (skill != null && BaseProps.requiredSkillLevel > 0)
            {
                var bestLevel = -1;
                for (var i = 0; i < pilots.Count; i++)
                {
                    var record = pilots[i].skills?.GetSkill(skill);
                    if (record == null || record.TotallyDisabled) continue;
                    if (record.Level > bestLevel) bestLevel = record.Level;
                }
                if (bestLevel < BaseProps.requiredSkillLevel)
                {
                    reason = "ROCKET_PilotSkillRequired".Translate(
                        BaseProps.requiredSkillLevel, skill.LabelCap, Mathf.Max(0, bestLevel));
                    return true;
                }
            }
            return false;
        }

        public ThingDef SelectedOrdinance
        {
            get => Primary.selectedOrdinance;
            set => Primary.selectedOrdinance = value;
        }

        public bool ShowAllOrdinance
        {
            get => Primary.showAllOrdinance;
            set => Primary.showAllOrdinance = value;
        }

        /// <summary>
        /// Effective ordinance list for the whole vehicle
        /// </summary>
        public IEnumerable<ThingDef> AvailableOrdinance()
        {
            var comps = Vehicle.AllComps;
            HashSet<ThingDef> seen = null;
            for (var i = 0; i < comps.Count; i++)
            {
                if (comps[i] is not CompAirstrikeBase b) continue;
                var list = b.BaseProps.ordinance;
                if (list == null) continue;
                for (var j = 0; j < list.Count; j++)
                {
                    var def = list[j];
                    if (def == null) continue;
                    seen ??= new HashSet<ThingDef>();
                    if (seen.Add(def)) yield return def;
                }
            }
        }

        public bool HasAvailableOrdinance()
        {
            var comps = Vehicle.AllComps;
            for (var i = 0; i < comps.Count; i++)
            {
                if (comps[i] is CompAirstrikeBase b
                    && b.BaseProps.ordinance is { Count: > 0 })
                    return true;
            }
            return false;
        }

        private CompAirstrikeBase Primary
        {
            get
            {
                if (field != null) return field;
                var comps = Vehicle.AllComps;
                for (var i = 0; i < comps.Count; i++)
                    if (comps[i] is CompAirstrikeBase b)
                        return field = b;
                return field = this;
            }
        }

        protected float ResolvedFuelCost => ComputeFuelCost(Vehicle.Map);

        protected abstract Gizmo BuildStrikeGizmo();
        protected abstract void StartTargeting(Map destMap);
        protected abstract int RequiredShells { get; }
        protected abstract OrdinancePattern Pattern { get; }

        /// <summary>
        /// Strike modes that pick from the OrdinanceDef cargo list.
        /// </summary>
        public virtual bool RequiresOrdinance => true;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;
            if (Vehicle.Faction != Faction.OfPlayer)
                yield break;
            if (!RequiresOrdinance)
            {
                yield return BuildStrikeGizmo();
                yield break;
            }
            var sel = SelectedOrdinance;
            if (sel == null) yield break;
            var allowed = BaseProps.ordinance;
            if (allowed != null && allowed.Contains(sel))
                yield return BuildStrikeGizmo();
        }

        protected float ComputeFuelCost(Map destMap, int passCount = 1)
        {
            var launcher = Vehicle.CompVehicleLauncher;
            if (launcher == null || Vehicle.CompFueledTravel == null) return 0f;
            var n = Mathf.Max(1, passCount);
            var buzzCost = launcher.FuelNeededToLaunchAtDist(Find.WorldGrid.AverageTileSize)
                           * AirstrikeMod.Settings.fuelScale;
            if (destMap == null || destMap == Vehicle.Map) return buzzCost * n;
            return launcher.FuelNeededToLaunchAtDist(destMap.Tile) * 2f + buzzCost * n;
        }

        protected Gizmo BuildLaunchGizmo(string label, string desc, Texture2D topIcon,
            int requiredShells, Action onClick, bool useSingleVariantIcon = false,
            ThingDef ammoOverrideDef = null, int ammoOverrideCount = 0,
            Texture2D iconUnderlayOverride = null)
        {
            var launcherComp = Vehicle.CompVehicleLauncher;
            var fuelComp = Vehicle.CompFueledTravel;
            var fuel = fuelComp?.Fuel ?? 0f;
            var cost = ResolvedFuelCost;
            var notEnoughFuel = fuelComp != null && fuel < cost;
            var sel = SelectedOrdinance;
            var ordnanceMode = RequiresOrdinance;
            var countInCargo = ordnanceMode && sel != null ? CountInCargo(sel) : 0;
            var ammoInCargo = ammoOverrideDef != null ? CountInCargo(ammoOverrideDef) : 0;

            var cmd = new Command_AirstrikeLaunch
            {
                defaultLabel = label,
                defaultDesc = desc + BuildRequiredSkillDescLine() + BuildTargetingAccuracyDescLine(),
                icon = topIcon,
                iconUnderlay = iconUnderlayOverride ?? (useSingleVariantIcon
                    ? GetSingleVariantIcon(sel)
                    : sel?.uiIcon),
                action = onClick,
            };

            if (launcherComp == null)
            {
                cmd.Disable("ROCKET_NoLauncherComp".Translate());
                return cmd;
            }

            var restriction = launcherComp.launchProtocol?.LaunchProperties?.restriction;
            if (Vehicle.Spawned && restriction != null)
            {
                cmd.mouseOver = () => restriction.DrawRestrictionsTargeter(
                    Vehicle, Vehicle.Map, Vehicle.Position, Vehicle.Rotation);
            }

            if (!Vehicle.Drafted)
            {
                cmd.Disable("ROCKET_VehicleNotStarted".Translate());
            }
            else if (PilotsBlockLaunch(out var pilotReason))
            {
                cmd.Disable(pilotReason);
            }
            else if (!launcherComp.CanLaunchWithCargoCapacity(out var launchReason))
            {
                cmd.Disable(launchReason);
            }
            else switch (ordnanceMode)
            {
                case true when sel == null:
                    cmd.Disable("ROCKET_SelectOrdinanceFirst".Translate());
                    break;
                case true when countInCargo < requiredShells:
                    cmd.Disable("ROCKET_NeedShellsHave".Translate(
                        requiredShells, sel.label, countInCargo));
                    break;
                default:
                {
                    if (ammoOverrideDef != null && ammoInCargo < ammoOverrideCount)
                    {
                        cmd.Disable("ROCKET_NeedShellsHave".Translate(
                            ammoOverrideCount, ammoOverrideDef.label, ammoInCargo));
                    }
                    else if (notEnoughFuel)
                    {
                        cmd.Disable("ROCKET_NotEnoughFuel".Translate(cost.ToString("0")));
                    }

                    break;
                }
            }

            return cmd;
        }

        protected void PickDestinationMap(Action<Map> onPicked)
        {
            var maps = Find.Maps;
            if (maps == null || maps.Count <= 1)
            {
                onPicked(Vehicle.Map);
                return;
            }

            var options = new List<FloatMenuOption>(maps.Count);
            for (var i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                if (map?.Parent == null) continue;
                options.Add(BuildMapOption(map, onPicked));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private FloatMenuOption BuildMapOption(Map destMap, Action<Map> onPicked)
        {
            var sameMap = destMap == Vehicle.Map;
            var launcher = Vehicle.CompVehicleLauncher;
            var fuelComp = Vehicle.CompFueledTravel;
            var fuel = fuelComp?.Fuel ?? 0f;
            var fuelCost = ComputeFuelCost(destMap);
            var notEnoughFuel = fuelComp != null && fuel < fuelCost;

            var tileDist = 0;
            var outOfRange = false;
            if (!sameMap && launcher != null)
            {
                tileDist = Mathf.RoundToInt(
                    Find.WorldGrid.ApproxDistanceInTiles(Vehicle.Map.Tile, destMap.Tile));
                outOfRange = launcher.MaxLaunchDistance > 0 && tileDist > launcher.MaxLaunchDistance;
            }

            var detail = sameMap
                ? "ROCKET_ThisMap".Translate().Resolve()
                : "ROCKET_MapDistanceFuel".Translate(
                    tileDist, fuelCost.ToString("0")).Resolve();
            var label = "ROCKET_MapOptionLabel".Translate(
                destMap.Parent.LabelCap, detail).Resolve();
            string disableReason = null;
            if (outOfRange)
                disableReason = "ROCKET_OutOfRange".Translate();
            else if (notEnoughFuel)
                disableReason = "ROCKET_NeedsFuel".Translate(fuelCost.ToString("0"));
            if (disableReason != null)
                label = "ROCKET_MapOptionDisabled".Translate(label, disableReason).Resolve();

            Action action = disableReason == null ? (() => onPicked(destMap)) : null;
            return new FloatMenuOption(label, action);
        }

        protected void LaunchStrike(Map destMap, List<BombingSegment> segments,
            Map originalMap, StrafingPayload strafing = null)
        {
            OnTargetsChosen(destMap, segments, Vehicle.Position,
                Vehicle.Rotation, strafing);
            RestoreCurrentMap(originalMap);
        }

        protected static void RestoreCurrentMap(Map originalMap)
        {
            if (originalMap != null && Current.Game.CurrentMap != originalMap)
                Current.Game.CurrentMap = originalMap;
        }

        private void OnTargetsChosen(Map destMap, List<BombingSegment> segments,
            LocalTargetInfo landingTarget, Rot4 landingRot, StrafingPayload strafing)
        {
            if (segments == null || segments.Count == 0) return;

            var totalBombs = 0;
            for (var i = 0; i < segments.Count; i++)
                totalBombs += segments[i].bombCells?.Count ?? 0;
            if (totalBombs == 0) return;

            var sel = SelectedOrdinance;
            if (RequiresOrdinance)
            {
                if (sel == null)
                {
                    Messages.Message("ROCKET_NoOrdinanceSelected".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (!ConsumeFromCargo(sel, totalBombs))
                {
                    Messages.Message(
                        "ROCKET_NeedShells".Translate(totalBombs, sel.label),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }
            else if (strafing != null && strafing.ammoDef != null && strafing.ammoCount > 0)
            {
                var totalAmmo = strafing.ammoCount * segments.Count;
                if (!ConsumeFromCargo(strafing.ammoDef, totalAmmo))
                {
                    Messages.Message(
                        "ROCKET_NeedShells".Translate(totalAmmo, strafing.ammoDef.label),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }

            var crossMap = destMap != Vehicle.Map;
            var originMapParent = Vehicle.Map.Parent;
            var destMapParent = destMap.Parent;
            var inPlace = BaseProps.inPlaceSortie && !crossMap;

            Vehicle.CompFueledTravel?.ConsumeFuel(ComputeFuelCost(destMap, segments.Count));

            var pilots = Vehicle.PawnsByHandlingType[HandlingType.Movement];
            for (var i = 0; i < pilots.Count; i++)
                pilots[i].records.Increment(AirstrikeDefOf.RocketsAirstrike_SortiesFlown);
            var (chosenPilot, _) = BestPilotAndAbility();

            BombingSpeedManager.MarkFast(Vehicle, Vehicle.Rotation);

            var resolvedScatter = ResolveBombScatter();
            var resolvedStrafingSpread = strafing != null
                ? ResolveStrafingSpread(strafing.spreadCells)
                : 0;

            var arrival = new ArrivalAction_BombMap(
                Vehicle,
                destMapParent,
                segments: segments,
                pattern: Pattern,
                returnCell: landingTarget.Cell,
                returnRot: landingRot,
                bombingSkyfallerDef: BaseProps.skyfallerBombing
                                     ?? Vehicle.CompVehicleLauncher.Props.skyfallerStrafing,
                ordinance: sel,
                scatter: resolvedScatter,
                originMapParent: crossMap ? originMapParent : null,
                flyAltitude: BaseProps.flyAltitude,
                sortieSpeedMultiplier: BaseProps.sortieSpeedMultiplier,
                bombFireSound: BaseProps.bombFireSound,
                chosenPilot: chosenPilot,
                xpSkill: BaseProps.requiredSkill,
                strafingProjectileDef: strafing?.projectileDef,
                strafingLeadCells: strafing?.leadCells ?? 0,
                strafingFireSound: strafing?.fireSound,
                strafingBulletsPerRound: strafing?.bulletsPerRound ?? 1,
                strafingSpreadCells: resolvedStrafingSpread,
                strafingFireOriginOffset: strafing?.fireOriginOffset ?? 3,
                strafingRunWidth: strafing?.runWidth ?? 1,
                inPlaceAnchor: inPlace ? Vehicle.Position : (IntVec3?)null,
                inPlaceForward: inPlace ? Vehicle.Rotation : (Rot4?)null,
                hoverApproachCells: BaseProps.hoverApproachCells,
                hoverTakeoffTicks: BaseProps.hoverTakeoffTicks,
                hoverLandingTicks: BaseProps.hoverLandingTicks);

            if (inPlace)
            {
                LaunchHover(arrival);
                return;
            }

            var targetData = new TargetData<GlobalTargetInfo>();
            targetData.targets.Add(new GlobalTargetInfo(destMap.Tile));

            Vehicle.CompVehicleLauncher.Launch(targetData, arrival);
        }

        protected void LaunchHover(IHoverArrival arrival)
        {
            var launcher = Vehicle.CompVehicleLauncher;
            launcher.inFlight = true;

            var skyfaller = (VehicleSkyfaller_HoverLaunch)
                VehicleSkyfallerMaker.MakeSkyfaller(AirstrikeDefOf.ROCKET_HoverLaunch, Vehicle);
            skyfaller.arrivalAction = (VehicleArrivalAction)arrival;
            skyfaller.createWorldObject = false;

            GenSpawn.Spawn(skyfaller, Vehicle.Position, Vehicle.Map, Vehicle.Rotation);
        }

        public int CountInCargo(ThingDef def)
        {
            if (def == null) return 0;
            var container = Vehicle.inventory?.innerContainer;
            if (container == null) return 0;
            var total = 0;
            var n = container.Count;
            for (var i = 0; i < n; i++)
            {
                var t = container[i];
                if (t.def == def) total += t.stackCount;
            }
            return total;
        }

        private bool ConsumeFromCargo(ThingDef def, int count)
        {
            if (def == null || count <= 0) return false;
            if (CountInCargo(def) < count) return false;

            var container = Vehicle.inventory?.innerContainer;
            if (container == null) return false;

            var remaining = count;
            for (var i = container.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var t = container[i];
                if (t.def != def || t.stackCount <= 0) continue;

                var take = Math.Min(remaining, t.stackCount);
                var piece = t.SplitOff(take);
                if (piece != null && !piece.Destroyed)
                    piece.Destroy(DestroyMode.Vanish);
                remaining -= take;
            }
            return remaining == 0;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Only the primary's value is read at runtime; secondaries scribe null.
            Scribe_Defs.Look(ref selectedOrdinance, nameof(selectedOrdinance));
            Scribe_Values.Look(ref showAllOrdinance, nameof(showAllOrdinance), true);
        }
    }
}
