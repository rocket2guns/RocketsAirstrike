using Verse;

namespace AirstrikeMod
{
    public class CompProperties_AirstrikeStrafingRun : CompProperties_AirstrikeBase
    {
        /// <summary>
        /// Cast sound played at each fired cell. 
        /// </summary>
        public SoundDef fireSound;

        /// <summary>
        /// Targeting rectangle's length along the flight axis.
        /// </summary>
        public int runLength = 40;

        /// <summary>
        /// Targeting rectangle's width perpendicular to the flight axis. 
        /// </summary>
        public int runWidth = 3;

        /// <summary>
        /// How far ahead of the plane (in cells, along the flight axis) firing happens.
        /// </summary>
        public int leadCells = 8;

        /// <summary>
        /// Vanilla bullet projectile to spawn at each cell. Defaults via XML.
        /// </summary>
        public ThingDef projectileDef;

        /// <summary>
        /// Cargo item consumed to load rounds for the strafing run.
        /// </summary>
        public ThingDef ammoDef;

        /// <summary>
        /// Units of <c>ammoDef</c> consumed per round fired.
        /// </summary>
        public int ammoPerRound = 1;

        /// <summary>
        /// Bullets spawned per consumed round (per cell).
        /// </summary>
        public int bulletsPerRound = 4;

        /// <summary>
        /// Half-extent of the random target offset applied to each bullet within a round, in cells.
        /// </summary>
        public int spreadCells = 1;

        /// <summary>
        /// Cells between each bullet's spawn point and its target cell, along the flight axis.
        /// </summary>
        public int fireOriginOffset = 3;

        public CompProperties_AirstrikeStrafingRun()
        {
            compClass = typeof(CompAirstrikeStrafingRun);
        }
    }
}
