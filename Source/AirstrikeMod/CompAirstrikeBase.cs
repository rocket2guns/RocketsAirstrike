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

        private OrdinanceDef selectedOrdinance;

        private static Texture2D _emptySlotIcon;
        protected static Texture2D EmptySlotIcon =>
            _emptySlotIcon ??= ContentFinder<Texture2D>.Get("UI/ButtonEmpty", reportFailure: false)
                               ?? BaseContent.BadTex;

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

        private Action _showOrdinanceMenuDelegate;

        protected OrdinanceDef SelectedOrdinance
        {
            get => Primary.selectedOrdinance;
            set => Primary.selectedOrdinance = value;
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

        private bool IsPrimary => Primary == this;

        protected float ResolvedFuelCost => ComputeFuelCost(Vehicle.Map);

        protected abstract Gizmo BuildStrikeGizmo();
        protected abstract void StartTargeting(Map destMap);
        protected abstract int RequiredShells { get; }
        protected abstract OrdinancePattern Pattern { get; }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;
            if (Vehicle.Faction != Faction.OfPlayer)
                yield break;
            if (IsPrimary)
                yield return BuildOrdinanceGizmo();
            if (SelectedOrdinance != null)
                yield return BuildStrikeGizmo();
        }

        // Same-map: just the buzz overhead (one tile of fuel scaled by fuelScale).
        // Cross-map: round-trip world flight + buzz overhead.
        protected float ComputeFuelCost(Map destMap)
        {
            var launcher = Vehicle.CompVehicleLauncher;
            if (launcher == null || Vehicle.CompFueledTravel == null) return 0f;
            var buzzCost = launcher.FuelNeededToLaunchAtDist(Find.WorldGrid.AverageTileSize)
                           * AirstrikeMod.Settings.fuelScale;
            if (destMap == null || destMap == Vehicle.Map) return buzzCost;
            return launcher.FuelNeededToLaunchAtDist(destMap.Tile) * 2f + buzzCost;
        }

        private Gizmo BuildOrdinanceGizmo()
        {
            string label;
            Texture2D icon;
            var sel = SelectedOrdinance;
            if (sel?.thingDef != null)
            {
                label = sel.thingDef.LabelCap;
                icon = sel.thingDef.uiIcon ?? EmptySlotIcon;
            }
            else
            {
                label = "RocketsAirstrike_SelectOrdinance".Translate();
                icon = EmptySlotIcon;
            }

            return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = "RocketsAirstrike_OrdinanceGizmoDesc".Translate(),
                icon = icon,
                action = _showOrdinanceMenuDelegate ??= ShowOrdinanceMenu,
            };
        }

        protected Gizmo BuildLaunchGizmo(string label, string desc, Texture2D topIcon,
            int requiredShells, Action onClick, bool useSingleVariantIcon = false)
        {
            var launcherComp = Vehicle.CompVehicleLauncher;
            var fuelComp = Vehicle.CompFueledTravel;
            var fuel = fuelComp?.Fuel ?? 0f;
            var cost = ResolvedFuelCost;
            var notEnoughFuel = fuelComp != null && fuel < cost;
            var sel = SelectedOrdinance;
            var countInCargo = sel != null ? CountInCargo(sel.thingDef) : 0;

            var cmd = new Command_AirstrikeLaunch
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = topIcon,
                iconUnderlay = useSingleVariantIcon
                    ? GetSingleVariantIcon(sel?.thingDef)
                    : sel?.thingDef?.uiIcon,
                action = onClick,
            };

            if (launcherComp == null)
            {
                cmd.Disable("RocketsAirstrike_NoLauncherComp".Translate());
                return cmd;
            }

            var restriction = launcherComp.launchProtocol?.LaunchProperties?.restriction;
            if (Vehicle.Spawned && restriction != null)
            {
                cmd.mouseOver = () => restriction.DrawRestrictionsTargeter(
                    Vehicle, Vehicle.Map, Vehicle.Position, Vehicle.Rotation);
            }

            if (!launcherComp.CanLaunchWithCargoCapacity(out var launchReason))
            {
                cmd.Disable(launchReason);
            }
            else if (sel == null)
            {
                cmd.Disable("RocketsAirstrike_SelectOrdinanceFirst".Translate());
            }
            else if (countInCargo < requiredShells)
            {
                cmd.Disable("RocketsAirstrike_NeedShellsHave".Translate(
                    requiredShells, sel.thingDef.label, countInCargo));
            }
            else if (notEnoughFuel)
            {
                cmd.Disable("RocketsAirstrike_NotEnoughFuel".Translate(cost.ToString("0")));
            }

            return cmd;
        }

        private void ShowOrdinanceMenu()
        {
            var defs = DefDatabase<OrdinanceDef>.AllDefsListForReading;
            var options = new List<FloatMenuOption>(defs.Count + 1);

            for (var i = 0; i < defs.Count; i++)
            {
                var ord = defs[i];
                if (ord.thingDef == null) continue;

                var count = CountInCargo(ord.thingDef);
                var empty = count <= 0;
                if (empty && AirstrikeMod.Settings.hideEmptyOrdinance) continue;
                var captured = ord;

                var baseLabel = "RocketsAirstrike_OrdinanceCount".Translate(
                    ord.thingDef.LabelCap, count).Resolve();
                var label = empty ? $"<color=#888888>{baseLabel}</color>" : baseLabel;
                var iconColor = empty ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
                var iconTex = ord.thingDef.uiIcon ?? EmptySlotIcon;

                options.Add(new FloatMenuOption(
                    label: label,
                    action: () => SelectedOrdinance = captured,
                    iconTex: iconTex,
                    iconColor: iconColor));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    "RocketsAirstrike_NoOrdinanceLoaded".Translate(), null));
            }
            else if (SelectedOrdinance != null)
            {
                options.Add(new FloatMenuOption(
                    "RocketsAirstrike_ClearSelection".Translate(),
                    () => SelectedOrdinance = null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Single-map: skip the menu and target the vehicle's map directly. Multi-map:
        // present every loaded map, greyed-with-reason when unreachable.
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
                ? "RocketsAirstrike_ThisMap".Translate().Resolve()
                : "RocketsAirstrike_MapDistanceFuel".Translate(
                    tileDist, fuelCost.ToString("0")).Resolve();
            var label = "RocketsAirstrike_MapOptionLabel".Translate(
                destMap.Parent.LabelCap, detail).Resolve();
            string disableReason = null;
            if (outOfRange)
                disableReason = "RocketsAirstrike_OutOfRange".Translate();
            else if (notEnoughFuel)
                disableReason = "RocketsAirstrike_NeedsFuel".Translate(fuelCost.ToString("0"));
            if (disableReason != null)
                label = "RocketsAirstrike_MapOptionDisabled".Translate(label, disableReason).Resolve();

            Action action = disableReason == null ? (() => onPicked(destMap)) : null;
            return new FloatMenuOption(label, action);
        }

        protected void LaunchStrike(Map destMap, List<IntVec3> bombCells, Rot4 flightDir,
            Map originalMap)
        {
            OnTargetsChosen(destMap, bombCells, flightDir, Vehicle.Position, Vehicle.Rotation.Opposite);
            RestoreCurrentMap(originalMap);
        }

        protected static void RestoreCurrentMap(Map originalMap)
        {
            if (originalMap != null && Current.Game.CurrentMap != originalMap)
                Current.Game.CurrentMap = originalMap;
        }

        private void OnTargetsChosen(Map destMap, List<IntVec3> bombCells, Rot4 flightDir,
            LocalTargetInfo landingTarget, Rot4 landingRot)
        {
            var sel = SelectedOrdinance;
            if (sel == null)
            {
                Messages.Message("RocketsAirstrike_NoOrdinanceSelected".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var needed = bombCells.Count;
            if (!ConsumeFromCargo(sel.thingDef, needed))
            {
                Messages.Message(
                    "RocketsAirstrike_NeedShells".Translate(needed, sel.thingDef.label),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var crossMap = destMap != Vehicle.Map;
            var originMapParent = Vehicle.Map.Parent;
            var destMapParent = destMap.Parent;

            Vehicle.CompFueledTravel?.ConsumeFuel(ComputeFuelCost(destMap));

            var pilots = Vehicle.PawnsByHandlingType[HandlingType.Movement];
            for (var i = 0; i < pilots.Count; i++)
            {
                var records = pilots[i].records;
                records.Increment(AirstrikeDefOf.RocketsAirstrike_SortiesFlown);
                records.AddTo(AirstrikeDefOf.RocketsAirstrike_OrdinanceDropped, bombCells.Count);
            }

            BombingSpeedManager.MarkFast(Vehicle);

            var arrival = new ArrivalAction_BombMap(
                Vehicle,
                destMapParent,
                bombCells: bombCells,
                flightDir: flightDir,
                pattern: Pattern,
                returnCell: landingTarget.Cell,
                returnRot: landingRot,
                bombingSkyfallerDef: BaseProps.skyfallerBombing
                                     ?? Vehicle.CompVehicleLauncher.Props.skyfallerStrafing,
                ordinance: sel,
                scatter: BaseProps.scatter,
                originMapParent: crossMap ? originMapParent : null);

            var targetData = new TargetData<GlobalTargetInfo>();
            targetData.targets.Add(new GlobalTargetInfo(destMap.Tile));

            Vehicle.CompVehicleLauncher.Launch(targetData, arrival);
        }

        private int CountInCargo(ThingDef def)
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
            var n = container.Count;
            for (var i = 0; i < n && remaining > 0; i++)
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
        }
    }
}
