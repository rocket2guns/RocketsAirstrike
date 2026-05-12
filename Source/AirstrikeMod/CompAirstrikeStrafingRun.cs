using System;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class CompAirstrikeStrafingRun : CompAirstrikeBase
    {
        public CompProperties_AirstrikeStrafingRun Props => (CompProperties_AirstrikeStrafingRun)props;

        private static Texture2D _icon;
        private static Texture2D Icon =>
            _icon ??= ContentFinder<Texture2D>.Get("UI/ButtonStrafe", reportFailure: false)
                      ?? BaseContent.BadTex;

        private Action _startDelegate;

        protected override bool RequiresOrdinance => false;

        private int RoundCount => Math.Max(0, Props.runLength);
        private int AmmoCount => RoundCount * Math.Max(1, Props.ammoPerRound);

        protected override int RequiredShells => 0;
        protected override OrdinancePattern Pattern => OrdinancePattern.Strafing;

        protected override Gizmo BuildStrikeGizmo()
        {
            var ammoDef = Props.ammoDef;
            var ammoLabel = ammoDef != null ? ammoDef.label : "ammo";
            return BuildLaunchGizmo(
                label: "RocketsAirstrike_StrafingRun".Translate(),
                desc: "RocketsAirstrike_StrafingRunDesc".Translate(RoundCount, AmmoCount, ammoLabel),
                topIcon: Icon,
                requiredShells: 0,
                onClick: _startDelegate ??= () => PickDestinationMap(StartTargeting),
                ammoOverrideDef: ammoDef,
                ammoOverrideCount: AmmoCount,
                iconUnderlayOverride: ammoDef?.uiIcon);
        }

        protected override void StartTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting
                || StrafingRunTargeter.Instance.IsTargeting)
                return;
            if (Props.projectileDef == null)
            {
                Messages.Message("[Rockets.Airstrike] Strafing comp has no projectileDef configured.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var originalMap = Current.Game.CurrentMap;
            if (Current.Game.CurrentMap != destMap)
                Current.Game.CurrentMap = destMap;

            var cursorIcon = Props.ammoDef?.uiIcon ?? Icon;
            CursorLabel.Current = "RocketsAirstrike_SelectTargetStrafe".Translate();

            var payload = new StrafingPayload
            {
                projectileDef = Props.projectileDef,
                leadCells = Props.leadCells,
                ammoDef = Props.ammoDef,
                ammoCount = AmmoCount,
                fireSound = Props.fireSound,
                bulletsPerRound = Math.Max(1, Props.bulletsPerRound),
                spreadCells = Math.Max(0, Props.spreadCells),
                fireOriginOffset = Math.Max(1, Props.fireOriginOffset),
            };

            StrafingRunTargeter.Instance.BeginTargeting(
                vehicle: Vehicle,
                map: destMap,
                runWidth: Props.runWidth,
                runLength: Props.runLength,
                action: (cells, dir) =>
                {
                    CursorLabel.Current = null;
                    LaunchStrike(destMap, cells, dir, originalMap, payload);
                },
                targetValidator: t => t.Cell.InBounds(destMap)
                                      && !Ext_Vehicles.IsRoofRestricted(Vehicle.VehicleDef, t.Cell, destMap),
                actionWhenFinished: () =>
                {
                    CursorLabel.Current = null;
                    RestoreCurrentMap(originalMap);
                },
                mouseAttachment: cursorIcon);
        }
    }
}
