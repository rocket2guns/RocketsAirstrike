using System;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompAirstrikeLocalFlight : CompAirstrikeBase
    {
        private static Texture2D _icon;
        private static Texture2D Icon =>
            _icon ??= ContentFinder<Texture2D>.Get("UI/ButtonLocalFlight", reportFailure: false)
                      ?? BaseContent.BadTex;

        // Consumed by Patches.LandingTargeter_TargeterOnGUI_Patch.
        public static string PendingMouseLabel;

        private Action _startDelegate;

        public override bool RequiresOrdinance => false;
        protected override int RequiredShells => 0;
        protected override OrdinancePattern Pattern => OrdinancePattern.Single;

        protected override string BuildTargetingAccuracyDescLine() => "";

        protected override Gizmo BuildStrikeGizmo()
        {
            return BuildLaunchGizmo(
                label: "ROCKET_LocalFlight".Translate(),
                desc: "ROCKET_LocalFlightDesc".Translate(),
                topIcon: Icon,
                requiredShells: 0,
                onClick: _startDelegate ??= () => StartTargeting(Vehicle.Map));
        }

        protected override void StartTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting
                || StrafingRunTargeter.Instance.IsTargeting
                || SingleStrikeTargeter.Instance.IsTargeting)
                return;

            PendingMouseLabel = "ROCKET_SelectLandingCell".Translate();

            LandingTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: destMap,
                action: (target, rot) =>
                {
                    PendingMouseLabel = null;
                    LaunchLocalFlight(destMap, target, rot);
                },
                targetValidator: t => t.Cell.InBounds(destMap),
                actionWhenFinished: () => PendingMouseLabel = null,
                mouseAttachment: null,
                allowRotating: true);
        }

        private void LaunchLocalFlight(Map destMap, LocalTargetInfo target, Rot4 rot)
        {
            if (Vehicle?.Map == null || destMap == null) return;

            Vehicle.CompFueledTravel?.ConsumeFuel(ComputeFuelCost(destMap));

            var (chosenPilot, _) = BestPilotAndAbility();

            var arrival = new ArrivalAction_LocalFlight(
                vehicle: Vehicle,
                transitSkyfallerDef: BaseProps.skyfallerBombing
                                     ?? Vehicle.CompVehicleLauncher.Props.skyfallerStrafing,
                originCell: Vehicle.Position,
                originForward: Vehicle.Rotation,
                destCell: target.Cell,
                destForward: rot,
                flyAltitude: BaseProps.flyAltitude,
                hoverTakeoffTicks: BaseProps.hoverTakeoffTicks,
                hoverLandingTicks: BaseProps.hoverLandingTicks,
                hoverApproachCells: BaseProps.hoverApproachCells,
                sortieSpeedMultiplier: BaseProps.sortieSpeedMultiplier,
                chosenPilot: chosenPilot);

            LaunchHover(arrival);
        }
    }
}
