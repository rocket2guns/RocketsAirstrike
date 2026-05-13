using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// One pass of a (possibly chained) bombing sortie
    /// </summary>
    public class BombingSegment : IExposable
    {
        public List<IntVec3> bombCells;
        public Rot4 flightDir;

        public BombingSegment()
        {
        }

        public BombingSegment(List<IntVec3> bombCells, Rot4 flightDir)
        {
            this.bombCells = bombCells;
            this.flightDir = flightDir;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref bombCells, nameof(bombCells), LookMode.Value);
            Scribe_Values.Look(ref flightDir, nameof(flightDir));
        }
    }
}
