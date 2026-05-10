using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using SmashTools.Targeting;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;

namespace AirstrikeMod
{
    public class CompAirstrike : VehicleComp
    {
        public CompProperties_Airstrike Props => (CompProperties_Airstrike)props;

        private OrdinanceDef selectedOrdinance;

        // Lazy on first gizmo enumeration; main thread only (cctor threading hazard).
        private static Texture2D _precisionIcon;
        private static Texture2D PrecisionIcon =>
            _precisionIcon ??= ContentFinder<Texture2D>.Get("UI/ButtonPrecisionStrike", reportFailure: false)
                               ?? BaseContent.BadTex;

        private static Texture2D _bombingRunIcon;
        private static Texture2D BombingRunIcon =>
            _bombingRunIcon ??= ContentFinder<Texture2D>.Get("UI/ButtonBombingRun", reportFailure: false)
                                ?? PrecisionIcon;

        private static Texture2D _emptySlotIcon;
        private static Texture2D EmptySlotIcon =>
            _emptySlotIcon ??= ContentFinder<Texture2D>.Get("UI/ButtonEmpty", reportFailure: false)
                               ?? BaseContent.BadTex;

        private Action _showOrdinanceMenuDelegate;
        private Action _startPrecisionDelegate;
        private Action _startBombingRunDelegate;
        private Func<LocalTargetInfo, bool> _isLandingValidDelegate;

        public static bool BombTargetingActive;
        public static Map BombTargetingMap;
        public static float BombTargetingRadius;

        private float ResolvedFuelCost => ComputeFuelCost(Vehicle.Map);

        // Same-map: just the buzz overhead (one tile of fuel scaled by fuelScale).
        // Cross-map: round-trip world flight + buzz overhead.
        private float ComputeFuelCost(Map destMap)
        {
            var launcher = Vehicle.CompVehicleLauncher;
            if (launcher == null || Vehicle.CompFueledTravel == null) return 0f;
            var buzzCost = launcher.FuelNeededToLaunchAtDist(Find.WorldGrid.AverageTileSize)
                           * AirstrikeMod.Settings.fuelScale;
            if (destMap == null || destMap == Vehicle.Map) return buzzCost;
            return launcher.FuelNeededToLaunchAtDist(destMap.Tile) * 2f + buzzCost;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;
            if (Vehicle.Faction != Faction.OfPlayer)
                yield break;
            yield return BuildOrdinanceGizmo();
            if (PatternAllowed(OrdinancePattern.Single))
                yield return BuildPrecisionStrikeGizmo();
            if (PatternAllowed(OrdinancePattern.Line))
                yield return BuildBombingRunGizmo();
        }

        private bool PatternAllowed(OrdinancePattern p)
        {
            return Props.allowedPatterns == null || Props.allowedPatterns.Contains(p);
        }

        private Gizmo BuildOrdinanceGizmo()
        {
            string label;
            Texture2D icon;
            if (selectedOrdinance?.thingDef != null)
            {
                label = selectedOrdinance.thingDef.LabelCap;
                icon = selectedOrdinance.thingDef.uiIcon ?? EmptySlotIcon;
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

        private Gizmo BuildPrecisionStrikeGizmo()
        {
            return BuildLaunchGizmo(
                label: "RocketsAirstrike_PrecisionStrike".Translate(),
                desc: "RocketsAirstrike_PrecisionStrikeDesc".Translate(),
                topIcon: PrecisionIcon,
                requiredShells: 1,
                onClick: _startPrecisionDelegate ??= StartPrecisionTargeting);
        }

        private Gizmo BuildBombingRunGizmo()
        {
            return BuildLaunchGizmo(
                label: "RocketsAirstrike_BombingRun".Translate(),
                desc: "RocketsAirstrike_BombingRunDesc".Translate(BombingRunTargeter.DropCount),
                topIcon: BombingRunIcon,
                requiredShells: BombingRunTargeter.DropCount,
                onClick: _startBombingRunDelegate ??= StartBombingRunTargeting);
        }

        private Gizmo BuildLaunchGizmo(string label, string desc, Texture2D topIcon,
            int requiredShells, Action onClick)
        {
            var launcherComp = Vehicle.CompVehicleLauncher;
            var fuelComp = Vehicle.CompFueledTravel;
            var fuel = fuelComp?.Fuel ?? 0f;
            var cost = ResolvedFuelCost;
            var notEnoughFuel = fuelComp != null && fuel < cost;
            var countInCargo = selectedOrdinance != null ? CountInCargo(selectedOrdinance.thingDef) : 0;

            var cmd = new Command_AirstrikeLaunch
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = topIcon,
                iconUnderlay = selectedOrdinance?.thingDef?.uiIcon,
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
            else if (selectedOrdinance == null)
            {
                cmd.Disable("RocketsAirstrike_SelectOrdinanceFirst".Translate());
            }
            else if (countInCargo < requiredShells)
            {
                cmd.Disable("RocketsAirstrike_NeedShellsHave".Translate(
                    requiredShells, selectedOrdinance.thingDef.label, countInCargo));
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
                var captured = ord;

                var baseLabel = "RocketsAirstrike_OrdinanceCount".Translate(
                    ord.thingDef.LabelCap, count).Resolve();
                var label = empty ? $"<color=#888888>{baseLabel}</color>" : baseLabel;
                var iconColor = empty ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
                var iconTex = ord.thingDef.uiIcon ?? EmptySlotIcon;

                options.Add(new FloatMenuOption(
                    label: label,
                    action: () => selectedOrdinance = captured,
                    iconTex: iconTex,
                    iconColor: iconColor));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption(
                    "RocketsAirstrike_NoOrdinanceLoaded".Translate(), null));
            }
            else if (selectedOrdinance != null)
            {
                options.Add(new FloatMenuOption(
                    "RocketsAirstrike_ClearSelection".Translate(),
                    () => selectedOrdinance = null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void StartPrecisionTargeting()
        {
            PickDestinationMap(destMap => StartPrecisionTargeting(destMap));
        }

        private void StartBombingRunTargeting()
        {
            PickDestinationMap(destMap => StartBombingRunTargeting(destMap));
        }

        // Single-map: skip the menu and target the vehicle's map directly. Multi-map:
        // present every loaded map, greyed-with-reason when unreachable.
        private void PickDestinationMap(Action<Map> onPicked)
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

        private void StartPrecisionTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting)
                return;

            var originalMap = Current.Game.CurrentMap;
            if (Current.Game.CurrentMap != destMap)
                Current.Game.CurrentMap = destMap;

            var targetingParameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetItems = false,
                canTargetBuildings = false,
                validator = t => t.Cell.InBounds(destMap)
                                 && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, t.Cell, destMap),
            };

            BombTargetingActive = true;
            BombTargetingMap = destMap;
            BombTargetingRadius = selectedOrdinance != null ? selectedOrdinance.radius : 3f;
            CursorLabel.Current = "RocketsAirstrike_SelectTargetLocation".Translate();

            var cursorIcon = selectedOrdinance?.thingDef?.uiIcon ?? PrecisionIcon;

            Find.Targeter.BeginTargeting(
                targetParams: targetingParameters,
                action: bombTarget =>
                {
                    BombTargetingActive = false;
                    CursorLabel.Current = null;
                    var cells = new List<IntVec3>(1) { bombTarget.Cell };
                    StartLandingTargeting(destMap, cells, Rot4.East, OrdinancePattern.Single,
                        originalMap);
                },
                actionWhenFinished: () =>
                {
                    BombTargetingActive = false;
                    // Keep state alive if we're chaining into stage B; clear on cancel.
                    if (!LandingTargeter.Instance.IsTargeting)
                    {
                        CursorLabel.Current = null;
                        RestoreCurrentMap(originalMap);
                    }
                },
                mouseAttachment: cursorIcon);
        }

        private void StartBombingRunTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting)
                return;
            if (selectedOrdinance == null) return;

            var originalMap = Current.Game.CurrentMap;
            if (Current.Game.CurrentMap != destMap)
                Current.Game.CurrentMap = destMap;

            var cursorIcon = selectedOrdinance.thingDef?.uiIcon ?? BombingRunIcon;
            CursorLabel.Current = "RocketsAirstrike_SelectTargetRun".Translate();

            BombingRunTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: destMap,
                ordinance: selectedOrdinance,
                action: (cells, dir) =>
                {
                    CursorLabel.Current = null;
                    StartLandingTargeting(destMap, cells, dir, OrdinancePattern.Line, originalMap);
                },
                targetValidator: t => t.Cell.InBounds(destMap)
                                      && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, t.Cell, destMap),
                actionWhenFinished: () =>
                {
                    if (!LandingTargeter.Instance.IsTargeting)
                    {
                        CursorLabel.Current = null;
                        RestoreCurrentMap(originalMap);
                    }
                },
                mouseAttachment: cursorIcon);
        }

        private void StartLandingTargeting(Map destMap, List<IntVec3> bombCells, Rot4 flightDir,
            OrdinancePattern pattern, Map originalMap)
        {
            if (LandingTargeter.Instance.IsTargeting) return;

            // VTOLs lack LandingProperties.restriction; skip the prompt and land back at
            // takeoff.
            if (!LandingNeedsTargeting())
            {
                OnTargetsChosen(destMap, bombCells, flightDir, pattern,
                    Vehicle.Position, Vehicle.Rotation);
                RestoreCurrentMap(originalMap);
                return;
            }

            CursorLabel.Current = "RocketsAirstrike_SelectLandingLocation".Translate();

            // Landing always happens on the vehicle's home map; jump there so the player
            // can see runway clearance.
            LandingTargeter.Instance.BeginTargetingAndFocusMap(
                vehicle: Vehicle,
                map: Vehicle.Map,
                action: (landingTarget, landingRot)
                    => OnTargetsChosen(destMap, bombCells, flightDir, pattern, landingTarget, landingRot),
                targetValidator: _isLandingValidDelegate ??= IsLandingValid,
                actionWhenFinished: () =>
                {
                    CursorLabel.Current = null;
                    RestoreCurrentMap(originalMap);
                },
                mouseAttachment: null,
                allowRotating: true,
                forcedTargeting: true);
        }

        private static void RestoreCurrentMap(Map originalMap)
        {
            if (originalMap != null && Current.Game.CurrentMap != originalMap)
                Current.Game.CurrentMap = originalMap;
        }

        private bool LandingNeedsTargeting()
        {
            return Vehicle.CompVehicleLauncher?.launchProtocol?.LandingProperties?.restriction != null;
        }

        private bool IsLandingValid(LocalTargetInfo target)
        {
            return target.Cell.InBounds(Vehicle.Map)
                && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, target.Cell, Vehicle.Map);
        }

        private void OnTargetsChosen(Map destMap, List<IntVec3> bombCells, Rot4 flightDir,
            OrdinancePattern pattern, LocalTargetInfo landingTarget, Rot4 landingRot)
        {
            if (selectedOrdinance == null)
            {
                Messages.Message("RocketsAirstrike_NoOrdinanceSelected".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var needed = bombCells.Count;
            if (!ConsumeFromCargo(selectedOrdinance.thingDef, needed))
            {
                Messages.Message(
                    "RocketsAirstrike_NeedShells".Translate(needed, selectedOrdinance.thingDef.label),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var crossMap = destMap != Vehicle.Map;
            var originMapParent = Vehicle.Map.Parent;
            var destMapParent = destMap.Parent;

            Vehicle.CompFueledTravel?.ConsumeFuel(ComputeFuelCost(destMap));

            if (AirstrikeMod.Settings.fastTakeoffLanding)
                BombingSpeedManager.MarkFast(Vehicle);

            var arrival = new ArrivalAction_BombMap(
                Vehicle,
                destMapParent,
                bombCells: bombCells,
                flightDir: flightDir,
                pattern: pattern,
                returnCell: landingTarget.Cell,
                returnRot: landingRot,
                bombingSkyfallerDef: Props.skyfallerBombing
                                     ?? Vehicle.CompVehicleLauncher.Props.skyfallerStrafing,
                ordinance: selectedOrdinance,
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
            Scribe_Defs.Look(ref selectedOrdinance, nameof(selectedOrdinance));
        }
    }
}
