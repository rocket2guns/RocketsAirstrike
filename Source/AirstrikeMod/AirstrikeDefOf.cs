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

        static AirstrikeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AirstrikeDefOf));
        }
    }
}
