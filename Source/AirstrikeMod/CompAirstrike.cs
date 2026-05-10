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

        // lazy-loaded on first gizmo enumeration (main thread)
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

        private float ResolvedFuelCost
        {
            get
            {
                var launcher = Vehicle.CompVehicleLauncher;
                if (launcher == null || Vehicle.CompFueledTravel == null) return 0f;
                var oneTileFuel = launcher.FuelNeededToLaunchAtDist(Find.WorldGrid.AverageTileSize);
                return oneTileFuel * AirstrikeMod.Settings.fuelScale;
            }
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
                label = "Select ordinance";
                icon = EmptySlotIcon;
            }

            return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = "Pick which loaded shell type the airstrike will drop. Shells are consumed from cargo.",
                icon = icon,
                action = _showOrdinanceMenuDelegate ??= ShowOrdinanceMenu,
            };
        }

        private Gizmo BuildPrecisionStrikeGizmo()
        {
            return BuildLaunchGizmo(
                label: "Precision Strike",
                desc: "Make a single bombing pass over a chosen cell, dropping one round.",
                topIcon: PrecisionIcon,
                requiredShells: 1,
                onClick: _startPrecisionDelegate ??= StartPrecisionTargeting);
        }

        private Gizmo BuildBombingRunGizmo()
        {
            return BuildLaunchGizmo(
                label: "Bombing Run",
                desc: $"Make a bombing pass dropping {BombingRunTargeter.DropCount} rounds along a chosen line.",
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
                cmd.Disable("Vehicle has no launcher comp.");
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
                cmd.Disable("Select an ordinance type first.");
            }
            else if (countInCargo < requiredShells)
            {
                cmd.Disable($"Need {requiredShells} {selectedOrdinance.thingDef.label} in cargo (have {countInCargo}).");
            }
            else if (notEnoughFuel)
            {
                cmd.Disable($"Not enough chemfuel (needs {cost:0}).");
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

                var baseLabel = $"{ord.thingDef.LabelCap} (×{count})";
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
                options.Add(new FloatMenuOption("(no ordinance defs loaded)", null));
            }
            else if (selectedOrdinance != null)
            {
                options.Add(new FloatMenuOption("Clear selection", () => selectedOrdinance = null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void StartPrecisionTargeting()
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting)
                return;

            var targetingParameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetItems = false,
                canTargetBuildings = false,
                validator = t => t.Cell.InBounds(Vehicle.Map)
                                 && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, t.Cell, Vehicle.Map),
            };

            BombTargetingActive = true;
            BombTargetingMap = Vehicle.Map;
            BombTargetingRadius = selectedOrdinance != null ? selectedOrdinance.radius : 3f;
            CursorLabel.Current = "Select Target Location";

            var cursorIcon = selectedOrdinance?.thingDef?.uiIcon ?? PrecisionIcon;

            Find.Targeter.BeginTargeting(
                targetParams: targetingParameters,
                action: bombTarget =>
                {
                    BombTargetingActive = false;
                    CursorLabel.Current = null;
                    var cells = new List<IntVec3>(1) { bombTarget.Cell };
                    StartLandingTargeting(cells, Rot4.East, OrdinancePattern.Single);
                },
                actionWhenFinished: () =>
                {
                    BombTargetingActive = false;
                    // Keep the label set if we're chaining into stage B; clear on cancel.
                    if (!LandingTargeter.Instance.IsTargeting)
                        CursorLabel.Current = null;
                },
                mouseAttachment: cursorIcon);
        }

        private void StartBombingRunTargeting()
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting)
                return;
            if (selectedOrdinance == null) return;

            var cursorIcon = selectedOrdinance.thingDef?.uiIcon ?? BombingRunIcon;
            CursorLabel.Current = "Select Target Run";

            BombingRunTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: Vehicle.Map,
                ordinance: selectedOrdinance,
                action: (cells, dir) =>
                {
                    CursorLabel.Current = null;
                    StartLandingTargeting(cells, dir, OrdinancePattern.Line);
                },
                targetValidator: t => t.Cell.InBounds(Vehicle.Map)
                                      && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, t.Cell, Vehicle.Map),
                actionWhenFinished: () =>
                {
                    if (!LandingTargeter.Instance.IsTargeting)
                        CursorLabel.Current = null;
                },
                mouseAttachment: cursorIcon);
        }

        private void StartLandingTargeting(List<IntVec3> bombCells, Rot4 flightDir, OrdinancePattern pattern)
        {
            if (LandingTargeter.Instance.IsTargeting) return;

            // VTOLs (no LandingProperties.restriction) skip the landing prompt and land
            // back at takeoff.
            if (!LandingNeedsTargeting())
            {
                OnTargetsChosen(bombCells, flightDir, pattern, Vehicle.Position, Vehicle.Rotation);
                return;
            }

            CursorLabel.Current = "Select Landing Location";

            LandingTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: Vehicle.Map,
                action: (landingTarget, landingRot)
                    => OnTargetsChosen(bombCells, flightDir, pattern, landingTarget, landingRot),
                targetValidator: _isLandingValidDelegate ??= IsLandingValid,
                actionOnStart: null,
                actionWhenFinished: () => CursorLabel.Current = null,
                mouseAttachment: null,
                allowRotating: true,
                forcedTargeting: true);
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

        private void OnTargetsChosen(List<IntVec3> bombCells, Rot4 flightDir, OrdinancePattern pattern,
            LocalTargetInfo landingTarget, Rot4 landingRot)
        {
            if (selectedOrdinance == null)
            {
                Messages.Message("No ordinance selected.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var needed = bombCells.Count;
            if (!ConsumeFromCargo(selectedOrdinance.thingDef, needed))
            {
                Messages.Message($"Need {needed} {selectedOrdinance.thingDef.label} in cargo.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var mapParent = Vehicle.Map.Parent;

            Vehicle.CompFueledTravel?.ConsumeFuel(ResolvedFuelCost);

            if (AirstrikeMod.Settings.fastTakeoffLanding)
                BombingSpeedManager.MarkFast(Vehicle);

            var arrival = new ArrivalAction_BombMap(
                Vehicle,
                mapParent,
                bombCells: bombCells,
                flightDir: flightDir,
                pattern: pattern,
                returnCell: landingTarget.Cell,
                returnRot: landingRot,
                bombingSkyfallerDef: Props.skyfallerBombing
                                     ?? Vehicle.CompVehicleLauncher.Props.skyfallerStrafing,
                ordinance: selectedOrdinance);

            var targetData = new TargetData<GlobalTargetInfo>();
            targetData.targets.Add(new GlobalTargetInfo(Vehicle.Map.Tile));

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
