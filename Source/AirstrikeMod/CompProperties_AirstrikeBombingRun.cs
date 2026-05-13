namespace AirstrikeMod
{
    public class CompProperties_AirstrikeBombingRun : CompProperties_AirstrikeBase
    {
        public const float DEFAULT_SPACING_MULTIPLIER = 1.8f;

        /// <summary>
        /// Number of bombs dropped along the run. Drives footprint length and the
        /// required-shells check.
        /// </summary>
        public int dropCount = 5;

        /// <summary>
        /// Bomb-to-bomb spacing as a multiple of the projectile's explosion radius.
        /// </summary>
        public float spacingMultiplier = DEFAULT_SPACING_MULTIPLIER;

        public CompProperties_AirstrikeBombingRun()
        {
            compClass = typeof(CompAirstrikeBombingRun);
        }
    }
}
