using System;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompAirstrikePrecision : CompAirstrikeBase
    {
        private static Texture2D _icon;
        private static Texture2D Icon =>
            _icon ??= ContentFinder<Texture2D>.Get("UI/ButtonPrecisionStrike", reportFailure: false)
                      ?? BaseContent.BadTex;

        private Action _startDelegate;

        protected override int RequiredShells => 1;
        protected override OrdinancePattern Pattern => OrdinancePattern.Single;

        protected override Gizmo BuildStrikeGizmo()
        {
            return BuildLaunchGizmo(
                label: "ROCKET_SingleStrike".Translate(),
                desc: "ROCKET_SingleStrikeDesc".Translate(),
                topIcon: Icon,
                requiredShells: RequiredShells,
                onClick: _startDelegate ??= () => PickDestinationMap(StartTargeting),
                useSingleVariantIcon: true);
        }

        protected override void StartTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting
                || SingleStrikeTargeter.Instance.IsTargeting)
                return;
            var sel = SelectedOrdinance;
            if (sel == null) return;

            var originalMap = Current.Game.CurrentMap;
            if (Current.Game.CurrentMap != destMap)
                Current.Game.CurrentMap = destMap;

            var cursorIcon = sel.uiIcon ?? Icon;
            SetTargetingCursor("ROCKET_SelectTargetLocation".Translate());
            CursorLabel.ThirdLine = "ROCKET_HoldShiftMultiTarget".Translate();

            var maxChain = Mathf.Max(1, CountInCargo(sel));

            SingleStrikeTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: destMap,
                ordinance: sel,
                maxChain: maxChain,
                action: segments =>
                {
                    CursorLabel.Clear();
                    LaunchStrike(destMap, segments, originalMap);
                },
                targetValidator: t => t.Cell.InBounds(destMap)
                                      && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, t.Cell, destMap),
                actionWhenFinished: () =>
                {
                    CursorLabel.Clear();
                    RestoreCurrentMap(originalMap);
                },
                mouseAttachment: cursorIcon);
        }
    }
}
