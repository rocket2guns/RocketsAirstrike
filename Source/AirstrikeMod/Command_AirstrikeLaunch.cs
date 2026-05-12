using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    // Layered icon: draws iconUnderlay first using the same offset/scale logic the base
    // uses for the main icon, then chains to base for the overlay. Used to draw the
    // selected ordnance's uiIcon beneath the strike-mode icon.
    public class Command_AirstrikeLaunch : Vehicles.Command_ActionHighlighter
    {
        public Texture2D iconUnderlay;

        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            if (iconUnderlay != null)
            {
                var iconRect = rect;
                iconRect.position += new Vector2(iconOffset.x * rect.size.x, iconOffset.y * rect.size.y);
                iconRect.y -= rect.size.y * 0.15f;

                if (!disabled || parms.lowLight)
                    GUI.color = IconDrawColor;
                else
                    GUI.color = IconDrawColor.SaturationChanged(0f);
                if (parms.lowLight)
                    GUI.color = GUI.color.ToTransparent(0.6f);

                Widgets.DrawTextureFitted(iconRect, iconUnderlay, iconDrawScale * 0.64f,
                    iconProportions, iconTexCoords, iconAngle, overrideMaterial ?? buttonMat);
                GUI.color = Color.white;
            }

            base.DrawIcon(rect, buttonMat, parms);
        }
    }
}
