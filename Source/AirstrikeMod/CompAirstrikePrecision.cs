using System;
using System.Collections.Generic;
using RimWorld;
using SmashTools.Targeting;
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
                label: "RocketsAirstrike_PrecisionStrike".Translate(),
                desc: "RocketsAirstrike_PrecisionStrikeDesc".Translate(),
                topIcon: Icon,
                requiredShells: RequiredShells,
                onClick: _startDelegate ??= () => PickDestinationMap(StartTargeting),
                useSingleVariantIcon: true);
        }

        protected override void StartTargeting(Map destMap)
        {
            if (Find.Targeter.IsTargeting || LandingTargeter.Instance.IsTargeting
                || BombingRunTargeter.Instance.IsTargeting)
                return;

            var originalMap = Current.Game.CurrentMap;
            if (Current.Game.CurrentMap != destMap)
                Current.Game.CurrentMap = destMap;

            var sel = SelectedOrdinance;
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
            BombTargetingRadius = sel?.radius ?? 3f;
            CursorLabel.Current = "RocketsAirstrike_SelectTargetLocation".Translate();

            var cursorIcon = sel?.thingDef?.uiIcon ?? Icon;

            Find.Targeter.BeginTargeting(
                targetParams: targetingParameters,
                action: bombTarget =>
                {
                    BombTargetingActive = false;
                    CursorLabel.Current = null;
                    var cells = new List<IntVec3>(1) { bombTarget.Cell };
                    LaunchStrike(destMap, cells, Rot4.East, originalMap);
                },
                actionWhenFinished: () =>
                {
                    BombTargetingActive = false;
                    CursorLabel.Current = null;
                    RestoreCurrentMap(originalMap);
                },
                mouseAttachment: cursorIcon);
        }
    }
}
