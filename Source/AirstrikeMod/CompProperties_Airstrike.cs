using Vehicles;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// Properties for <see cref="CompAirstrike"/>. Add this to a VehicleDef via Patch
    /// or directly in XML to give the vehicle an "Airstrike" gizmo.
    ///
    /// XML usage:
    ///   <comps>
    ///     <li Class="AirstrikeMod.CompProperties_Airstrike">
    ///       <fuelCost>50</fuelCost>
    ///     </li>
    ///   </comps>
    /// </summary>
    public class CompProperties_Airstrike : VehicleCompProperties
    {
        /// <summary>
        /// Per-vehicle override for chemfuel cost. Falls back to mod setting if 0.
        /// </summary>
        public float fuelCost = 0f;

        /// <summary>
        /// Per-vehicle override for explosion radius. Falls back to mod setting if 0.
        /// </summary>
        public float bombRadius = 0f;

        /// <summary>
        /// Per-vehicle override for bomb damage. Falls back to mod setting if 0.
        /// </summary>
        public float bombDamage = 0f;

        /// <summary>
        /// Optional. ThingDef to use for the bombing skyfaller animation. If null, the
        /// vehicle's existing <c>skyfallerStrafing</c> def is reused.
        /// </summary>
        public ThingDef skyfallerBombing;

        public CompProperties_Airstrike()
        {
            compClass = typeof(CompAirstrike);
        }
    }
}
