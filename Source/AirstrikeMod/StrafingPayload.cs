using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// Per-launch payload for strafing runs. Carries the strafing-specific data from
    /// the comp through <see cref="CompAirstrikeBase.LaunchStrike"/> into
    /// <see cref="ArrivalAction_BombMap"/> and onward to the skyfaller, without
    /// leaking those fields onto the bombing/precision paths.
    /// </summary>
    public class StrafingPayload
    {
        public ThingDef projectileDef;
        public int leadCells;
        public ThingDef ammoDef;
        public int ammoCount;
        public SoundDef fireSound;
        public int bulletsPerRound = 1;
        public int spreadCells;
        public int fireOriginOffset = 3;
        public int runWidth = 1;
    }
}
