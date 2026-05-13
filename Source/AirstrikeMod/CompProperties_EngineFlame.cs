using System.Collections.Generic;
using UnityEngine;
using Vehicles;

namespace AirstrikeMod
{
    public class CompProperties_EngineFlame : VehicleCompProperties
    {
        public List<Vector2> exhaustOffsetsEast = new();
        public List<Vector2> exhaustOffsetsNorth;
        public List<Vector2> exhaustOffsetsSouth;
        public List<Vector2> exhaustOffsetsWest;

        public Vector2 flameSize = new(1f, 1f);

        public string texturePath = "Things/Vehicles/ExhaustFlame";

        // per-frame random scale jitter, 0..1.
        public float flicker = 0.15f;

        // extra rotation applied to the flame quad on top of vehicle facing.
        public float rotationDeg = 0f;

        // texture-space pivot in [0..1]; (0.5, 0.5) = centre, (1, 0.5) = right-middle.
        public Vector2 pivot = new(0.5f, 0.5f);

        // 0 = snap immediately. 60 = ~1 in-game second at normal speed
        public int rampTicks = 60;

        public CompProperties_EngineFlame()
        {
            compClass = typeof(CompEngineFlame);
        }
    }
}
