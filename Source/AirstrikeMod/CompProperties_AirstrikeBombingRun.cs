namespace AirstrikeMod
{
    public class CompProperties_AirstrikeBombingRun : CompProperties_AirstrikeBase
    {
        /// <summary>
        /// Number of bombs dropped along the run. Drives footprint length and the
        /// required-shells check.
        /// </summary>
        public int dropCount = 5;

        public CompProperties_AirstrikeBombingRun()
        {
            compClass = typeof(CompAirstrikeBombingRun);
        }
    }
}
