using RimWorld;
using Verse;

namespace AirstrikeMod
{
    /// <summary>
    /// Mirrors Verse.Projectile_Explosive.Explode for code paths that drop a bomb
    /// without ever instantiating a real projectile (our skyfaller-based airstrike
    /// pipeline). Triggers explosionEffect, then routes everything else through the
    /// projectile's ProjectileProperties so XML changes on the bullet def behave the
    /// same way they would for a mortar shell.
    /// </summary>
    internal static class ExplosionFx
    {
        public static void Trigger(ThingDef projectileDef, ProjectileProperties pp,
            IntVec3 pos, Map map, Thing instigator)
        {
            if (pp == null || map == null || !pos.InBounds(map)) return;

            if (pp.explosionEffect != null)
            {
                var effecter = pp.explosionEffect.Spawn();
                effecter.Trigger(new TargetInfo(pos, map), new TargetInfo(pos, map));
                if (pp.explosionEffectLifetimeTicks > 0)
                    map.effecterMaintainer.AddEffecterToMaintain(effecter, pos,
                        pp.explosionEffectLifetimeTicks);
                else
                    effecter.Cleanup();
            }

            GenExplosion.DoExplosion(
                center: pos,
                map: map,
                radius: pp.explosionRadius,
                damType: pp.damageDef ?? DamageDefOf.Bomb,
                instigator: instigator,
                damAmount: pp.GetDamageAmount((Thing)null),
                armorPenetration: -1f,
                explosionSound: pp.soundExplode,
                weapon: null,
                projectile: projectileDef,
                intendedTarget: null,
                postExplosionSpawnThingDef: pp.postExplosionSpawnThingDef,
                postExplosionSpawnChance: pp.postExplosionSpawnChance,
                postExplosionSpawnThingCount: pp.postExplosionSpawnThingCount,
                postExplosionGasType: pp.postExplosionGasType,
                applyDamageToExplosionCellsNeighbors: pp.applyDamageToExplosionCellsNeighbors,
                preExplosionSpawnThingDef: pp.preExplosionSpawnThingDef,
                preExplosionSpawnChance: pp.preExplosionSpawnChance,
                preExplosionSpawnThingCount: pp.preExplosionSpawnThingCount,
                chanceToStartFire: pp.explosionChanceToStartFire,
                damageFalloff: pp.explosionDamageFalloff,
                postExplosionSpawnSingleThingDef: pp.postExplosionSpawnSingleThingDef,
                preExplosionSpawnSingleThingDef: pp.preExplosionSpawnSingleThingDef,
                screenShakeFactor: pp.screenShakeFactor);
        }
    }
}
