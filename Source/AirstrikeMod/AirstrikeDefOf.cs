using RimWorld;
using Verse;

namespace AirstrikeMod
{
    [DefOf]
    public static class AirstrikeDefOf
    {
        public static RecordDef RocketsAirstrike_SortiesFlown;
        public static RecordDef RocketsAirstrike_OrdinanceDropped;
        public static RecordDef RocketsAirstrike_TimeFlown;

        public static StatDef ROCKET_TargetingAbility;

        public static SoundDef ROCKET_InterfaceBeep1;

        public static ThingDef ROCKET_HoverLaunch;
        public static ThingDef ROCKET_HoverLanding;

        static AirstrikeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AirstrikeDefOf));
        }
    }
}
