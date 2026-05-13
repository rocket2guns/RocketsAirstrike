using System;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompAirstrikeBombingRun : CompAirstrikeBase
    {
        public CompProperties_AirstrikeBombingRun Props => (CompProperties_AirstrikeBombingRun)props;

        private static Texture2D _icon;
        private static Texture2D Icon =>
            _icon ??= ContentFinder<Texture2D>.Get("UI/ButtonBombingRun", reportFailure: false)
                      ?? ContentFinder<Texture2D>.Get("UI/ButtonPrecisionStrike", reportFailure: false)
                      ?? BaseContent.BadTex;

        private Action _startDelegate;

        protected override int RequiredShells => Props.dropCount;
        protected override OrdinancePattern Pattern => OrdinancePattern.Line;

        protected override Gizmo BuildStrikeGizmo()
        {
            return BuildLaunchGizmo(
                label: "ROCKET_BombingRun".Translate(),
                desc: "ROCKET_BombingRunDesc".Translate(Props.dropCount),
                topIcon: Icon,
                requiredShells: RequiredShells,
                onClick: _startDelegate ??= () => PickDestinationMap(StartTargeting));
        }

        protected override void StartTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting)
                return;
            var sel = SelectedOrdinance;
            if (sel == null) return;

            var originalMap = Current.Game.CurrentMap;
            if (Current.Game.CurrentMap != destMap)
                Current.Game.CurrentMap = destMap;

            var cursorIcon = sel.uiIcon ?? Icon;
            SetTargetingCursor("ROCKET_SelectTargetRun".Translate());

            var maxChain = Mathf.Max(1, CountInCargo(sel) / Mathf.Max(1, Props.dropCount));

            BombingRunTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: destMap,
                ordinance: sel,
                dropCount: Props.dropCount,
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
