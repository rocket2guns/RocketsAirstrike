using System;
using System.Reflection;
using JetBrains.Annotations;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    public class VehicleSkyfaller_HoverLanding : VehicleSkyfaller_Arriving
    {
        private static FieldInfo _ticksPassedField;

        public float visualAltitude = 6f;
        private float _landCurveEndZ = -1f;

        [UsedImplicitly]
        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.", error: true)]
        public VehicleSkyfaller_HoverLanding()
        {
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad) return;
            SkipFlyInPhase();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            launchProtocolDrawPos = ScaleAltitudeToSortie(launchProtocolDrawPos);
        }

        private Vector3 ScaleAltitudeToSortie(Vector3 pos)
        {
            if (visualAltitude <= 0f) return pos;
            var endZ = GetLandingCurveEndZ();
            if (endZ <= 0.0001f) return pos;
            var rootZ = RootPos.z;
            pos.z = rootZ + (pos.z - rootZ) * (visualAltitude / endZ);
            return pos;
        }

        private float GetLandingCurveEndZ()
        {
            if (_landCurveEndZ >= 0f) return _landCurveEndZ;
            _landCurveEndZ = 0f;
            if (vehicle?.CompVehicleLauncher?.launchProtocol is PropellerTakeoff prop)
            {
                var props = prop.LandingProperties_Propeller;
                if (props != null)
                {
                    if (props.zPositionPropellerCurve != null)
                        _landCurveEndZ += props.zPositionPropellerCurve.Evaluate(0f);
                    if (props.zPositionVerticalCurve != null)
                        _landCurveEndZ += props.zPositionVerticalCurve.Evaluate(0f);
                }
            }
            return _landCurveEndZ;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref visualAltitude, nameof(visualAltitude), 6f);
        }

        private void SkipFlyInPhase()
        {
            var protocol = vehicle?.CompVehicleLauncher?.launchProtocol;
            if (protocol == null) return;
            var landingProps = protocol.LandingProperties;
            if (landingProps == null) return;

            _ticksPassedField ??= typeof(LaunchProtocol)
                .GetField("ticksPassed", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_ticksPassedField == null)
            {
                Log.Warning("[Rockets.Airstrike] HoverLanding could not find " +
                            "LaunchProtocol.ticksPassed; landing will include fly-in phase.");
                return;
            }
            _ticksPassedField.SetValue(protocol, landingProps.maxTicks);
        }
    }
}
