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
    /// <summary>
    /// Vehicle comp that exposes an "Airstrike" gizmo. On click:
    ///   1. Stage A: a vanilla cell-targeter (crosshair) for the bomb cell.
    ///   2. Stage B: <see cref="LandingTargeter"/> for the landing cell (collision-checked).
    ///   3. Captures both cells, consumes fuel, calls <c>CompVehicleLauncher.Launch</c>
    ///      with a custom <see cref="ArrivalAction_BombMap"/>.
    ///
    /// Same trick as the Local Flight mod: feed the existing takeoff/world-flight/landing
    /// pipeline a same-tile destination so the world-map leg is degenerate, and customize
    /// what happens on arrival via the arrival action.
    /// </summary>
    public class CompAirstrike : VehicleComp
    {
        public CompProperties_Airstrike Props => (CompProperties_Airstrike)props;

        // Lazy-loaded on first gizmo enumeration (main thread). Static field initializer
        // would run on whichever thread first touches the type — for save-load that's the
        // worker thread that deserializes pawns, where ContentFinder.Get throws.
        private static Texture2D _airstrikeIcon;
        private static Texture2D AirstrikeIcon =>
            _airstrikeIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip", reportFailure: false)
                               ?? BaseContent.BadTex;

        private float ResolvedFuelCost =>
            Props.fuelCost > 0f ? Props.fuelCost : AirstrikeMod.Settings.fuelCost;

        private float ResolvedBombRadius =>
            Props.bombRadius > 0f ? Props.bombRadius : AirstrikeMod.Settings.bombRadius;

        private float ResolvedBombDamage =>
            Props.bombDamage > 0f ? Props.bombDamage : AirstrikeMod.Settings.bombDamage;

        // Cross-frame targeting state read by the Targeter render postfix
        // (Patches/Targeter_Render_Patch.cs). Process-global; never two airstrike
        // targetings at once because the gizmo gates on Find.Targeter.IsTargeting.
        public static bool BombTargetingActive;
        public static Map BombTargetingMap;
        public static float BombTargetingRadius;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (Vehicle.Faction != Faction.OfPlayer)
                yield break;

            var launcherComp = Vehicle.CompVehicleLauncher;
            var fuelComp = Vehicle.CompFueledTravel;
            float fuel = fuelComp?.Fuel ?? 0f;
            float cost = ResolvedFuelCost;
            bool notEnoughFuel = fuelComp != null && fuel < cost;

            // Command_ActionHighlighter (SmashTools) is what VF's vanilla Launch gizmo
            // uses — it has the mouseOver hook we need to draw the runway restriction.
            var cmd = new Command_ActionHighlighter
            {
                defaultLabel = "Airstrike".Translate(),
                defaultDesc = "AirstrikeDesc".Translate(),
                icon = AirstrikeIcon,
                action = StartBombTargeting,
            };

            if (launcherComp == null)
            {
                cmd.Disable("Vehicle has no launcher comp.");
            }
            else
            {
                // Mirror the vanilla Launch gizmo: hover draws the runway/clearance
                // restriction (relevant for DirectionalTakeoff planes; null for vertical
                // takeoff like the Mosquito's PropellerTakeoff).
                var restriction = launcherComp.launchProtocol?.LaunchProperties?.restriction;
                if (Vehicle.Spawned && restriction != null)
                {
                    cmd.mouseOver = () => restriction.DrawRestrictionsTargeter(
                        Vehicle, Vehicle.Map, Vehicle.Position, Vehicle.Rotation);
                }

                // Use VF's full launch-viability check (includes runway clearance, fuel
                // empty, immobile, rotated, encumbered, missing operators, etc.). If the
                // regular Launch gizmo would refuse, ours does too — same reason text.
                if (!launcherComp.CanLaunchWithCargoCapacity(out string launchReason))
                {
                    cmd.Disable(launchReason);
                }
                else if (notEnoughFuel)
                {
                    cmd.Disable($"Not enough chemfuel for airstrike (needs {cost:0}).");
                }
            }

            yield return cmd;
        }

        // Stage A — pick the bomb cell with a crosshair. No collision/landing constraints,
        // since this is a target, not a landing site.
        private void StartBombTargeting()
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting)
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
            BombTargetingRadius = ResolvedBombRadius;

            Find.Targeter.BeginTargeting(
                targetParams: targetingParameters,
                action: bombTarget =>
                {
                    BombTargetingActive = false;
                    StartLandingTargeting(bombTarget);
                },
                actionWhenFinished: () =>
                {
                    // Fires whether the player accepts or right-clicks to cancel.
                    BombTargetingActive = false;
                },
                mouseAttachment: AirstrikeIcon);
        }

        // Stage B — pick the landing cell with VF's LandingTargeter. This is where the
        // vehicle actually lands once the bombing pass is done; LandingTargeter enforces
        // the vehicle's collision/clear-area rules.
        private void StartLandingTargeting(LocalTargetInfo bombTarget)
        {
            if (LandingTargeter.Instance.IsTargeting)
                return;

            LandingTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: Vehicle.Map,
                action: (landingTarget, landingRot) => OnTargetsChosen(bombTarget, landingTarget, landingRot),
                targetValidator: IsLandingValid,
                actionOnStart: null,
                actionWhenFinished: null,
                mouseAttachment: null,
                allowRotating: true,
                forcedTargeting: true);
        }

        private bool IsLandingValid(LocalTargetInfo target)
        {
            return target.Cell.InBounds(Vehicle.Map)
                && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, target.Cell, Vehicle.Map);
        }

        private void OnTargetsChosen(LocalTargetInfo bombTarget, LocalTargetInfo landingTarget, Rot4 landingRot)
        {
            MapParent mapParent = Vehicle.Map.Parent;

            // Consume fuel.
            Vehicle.CompFueledTravel?.ConsumeFuel(ResolvedFuelCost);

            // Optional: enable Local Flight–style fast takeoff/landing animation.
            if (AirstrikeMod.Settings.fastTakeoffLanding)
                BombingSpeedManager.MarkFast(Vehicle);

            var arrival = new ArrivalAction_BombMap(
                Vehicle,
                mapParent,
                bombCell: bombTarget.Cell,
                returnCell: landingTarget.Cell,
                returnRot: landingRot,
                bombingSkyfallerDef: Props.skyfallerBombing
                                     ?? Vehicle.CompVehicleLauncher.Props.skyfallerStrafing,
                bombRadius: ResolvedBombRadius,
                bombDamage: ResolvedBombDamage);

            // Same-tile destination — world flight is degenerate, BombMap.Arrived fires
            // almost immediately and spawns the bombing skyfaller.
            var targetData = new TargetData<GlobalTargetInfo>();
            targetData.targets.Add(new GlobalTargetInfo(Vehicle.Map.Tile));

            Vehicle.CompVehicleLauncher.Launch(targetData, arrival);
        }
    }
}
