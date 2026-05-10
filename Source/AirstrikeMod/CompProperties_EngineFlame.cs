using System.Collections.Generic;
using UnityEngine;
using Vehicles;

namespace AirstrikeMod
{
    public class CompProperties_EngineFlame : VehicleCompProperties
    {
        // for east/west and diagonal facings
        public List<Vector2> enginePoints = new();

        // north/south sprites are typically a different view, so allow an override list
        public List<Vector2> enginePointsVertical;

        public Vector2 flameSize = new(1f, 1f);

        public string texturePath = "UI/ButtonTarget";

        // per-frame random scale jitter, 0..1.
        public float flicker = 0.15f;

        // extra rotation applied to the flame quad on top of vehicle facing.
        public float rotationDeg = 0f;

        // texture-space pivot in [0..1]; (0.5, 0.5) = centre, (1, 0.5) = right-middle.
        public Vector2 pivot = new(0.5f, 0.5f);

        public CompProperties_EngineFlame()
        {
            compClass = typeof(CompEngineFlame);
        }
    }
}
