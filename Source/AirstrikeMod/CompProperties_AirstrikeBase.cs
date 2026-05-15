using System.Collections.Generic;
using RimWorld;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// Shared properties for all airstrike-mode comps. Not referenced directly from
    /// XML; concrete subclasses set <c>compClass</c>.
    /// </summary>
    public abstract class CompProperties_AirstrikeBase : VehicleCompProperties
    {
        public ThingDef skyfallerBombing;

        /// <summary>
        /// ThingDefs this comp can drop as ordinance.
        /// </summary>
        public List<ThingDef> ordinance;

        /// <summary>
        /// Per-bomb random offset radius in cells. 0 = perfectly accurate.
        /// </summary>
        public float scatter = 0f;

        /// <summary>
        /// Additional scatter potential applied based on pilot targeting ability.
        /// </summary>
        public float skillScatter = 0f;

        /// <summary>
        /// Skyfaller draw altitude during the buzz.
        /// </summary>
        public float flyAltitude = 6f;

        /// <summary>
        /// Per-sortie flight-speed multiplier. 1 = full speed (as derived from
        /// the vehicle's FlightSpeed). &lt;1 slows the whole sortie down (entire
        /// spline traversal and legacy single-pass progress)
        /// </summary>
        public float sortieSpeedMultiplier = 1f;

        /// <summary>
        /// Optional one-shot sound played at the aircraft's current map position
        /// each time a munition is released. Only consumed by bombing/single-strike
        /// patterns (strafing routes through its own fireSound). Null = silent.
        /// </summary>
        public SoundDef bombFireSound;

        /// <summary>
        /// Skill required of at least one pilot for this comp to be operable. Also
        /// drives accuracy (chosen pilot's level → 0..1 ability) and where per-drop
        /// XP is deposited. Null = no skill requirement (anyone can use it, full
        /// ability, no XP awarded).
        /// </summary>
        public SkillDef requiredSkill;

        /// <summary>
        /// Minimum level the best pilot must have in <see cref="requiredSkill"/>
        /// for launch to be allowed. Ignored when <see cref="requiredSkill"/> is null.
        /// </summary>
        public int requiredSkillLevel = 0;

        /// <summary>
        /// Same-map VTOL hover-launch/landing. Cross-map sorties unaffected.
        /// </summary>
        public bool inPlaceSortie = false;

        /// <summary>
        /// In-place spline forward/behind anchor distance. Larger = wider arcs.
        /// </summary>
        public int hoverApproachCells = 5;
    }
}
