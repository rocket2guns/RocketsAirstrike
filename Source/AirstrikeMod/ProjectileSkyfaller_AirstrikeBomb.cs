using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace AirstrikeMod
{
    // Subclass of VF's ProjectileSkyfaller. Animates a bomb falling from origin to
    // destination, then runs OUR explosion at impact rather than the wrapped
    // projectile's default Launch path. This preserves OrdinanceDef overrides (e.g.
    // bumped incendiary chemfuel splatter) which the vanilla projectile would bypass.
    public class ProjectileSkyfaller_AirstrikeBomb : ProjectileSkyfaller
    {
        public OrdinanceDef ordinance;
        public int fallTicks = 30;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
                ticksToImpact = fallTicks;
        }

        protected override void Impact()
        {
            if (ordinance == null || !Position.InBounds(Map))
            {
                Destroy();
                return;
            }

            var damDef = ordinance.damageDef ?? DamageDefOf.Bomb;
            GenExplosion.DoExplosion(
                center: Position,
                map: Map,
                radius: ordinance.radius,
                damType: damDef,
                instigator: caster,
                damAmount: Mathf.RoundToInt(ordinance.damage),
                armorPenetration: -1f,
                explosionSound: null,
                weapon: null,
                projectile: null,
                intendedTarget: null,
                postExplosionSpawnThingDef: ordinance.postExplosionSpawnThingDef,
                postExplosionSpawnChance: ordinance.postExplosionSpawnChance,
                postExplosionSpawnThingCount: ordinance.postExplosionSpawnThingCount,
                preExplosionSpawnThingDef: ordinance.preExplosionSpawnThingDef,
                preExplosionSpawnChance: ordinance.preExplosionSpawnChance,
                preExplosionSpawnThingCount: ordinance.preExplosionSpawnThingCount);

            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref ordinance, nameof(ordinance));
            Scribe_Values.Look(ref fallTicks, nameof(fallTicks), 30);
        }
    }
}
