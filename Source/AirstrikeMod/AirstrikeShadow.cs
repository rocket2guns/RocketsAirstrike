using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    internal static class AirstrikeShadow
    {
        private static MaterialPropertyBlock _props;

        public static void Draw(Vector3 groundPos, Vector2 size, float headingDeg,
            float altitudeAboveGround, Material material)
        {
            if (material == null) return;

            var shadowTicks = Mathf.Max(0, Mathf.RoundToInt(altitudeAboveGround * 10f));
            var grow = 1f + shadowTicks / 100f;

            var pos = groundPos;
            pos.y = AltitudeLayer.Shadows.AltitudeFor();
            var scale = new Vector3(grow * size.x, 1f, grow * size.y);
            var quat = Quaternion.AngleAxis(headingDeg, Vector3.up);

            var color = Color.white;
            if (shadowTicks > 150) color.a = Mathf.InverseLerp(200f, 150f, shadowTicks);

            _props ??= new MaterialPropertyBlock();
            _props.SetColor(ShaderPropertyIDs.Color, color);
            Matrix4x4 matrix = default;
            matrix.SetTRS(pos, quat, scale);
            Graphics.DrawMesh(MeshPool.plane10Back, matrix, material, 0, null, 0, _props);
        }
    }
}
