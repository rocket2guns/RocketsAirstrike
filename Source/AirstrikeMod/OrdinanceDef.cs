using RimWorld;
using Verse;

namespace AirstrikeMod
{
    public class OrdinanceDef : Def
    {
        public ThingDef thingDef;
        public ThingDef projectileDef;
        public DamageDef damageDef;
        public float radius = 0f;
        public float damage = -1f;
        public ThingDef preExplosionSpawnThingDef;
        public float preExplosionSpawnChance = 0f;
        public int preExplosionSpawnThingCount = 0;
        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance = 0f;
        public int postExplosionSpawnThingCount = 0;

        public override void ResolveReferences()
        {
            base.ResolveReferences();

            projectileDef ??= thingDef?.projectileWhenLoaded;

            var pp = projectileDef?.projectile;
            if (pp != null)
            {
                damageDef ??= pp.damageDef;
                if (radius == 0f) radius = pp.explosionRadius;
                if (damage < 0f) damage = pp.GetDamageAmount((Thing)null);
                preExplosionSpawnThingDef ??= pp.preExplosionSpawnThingDef;
                if (preExplosionSpawnChance == 0f)
                    preExplosionSpawnChance = pp.preExplosionSpawnChance;
                if (preExplosionSpawnThingCount <= 0)
                    preExplosionSpawnThingCount = pp.preExplosionSpawnThingCount;
                postExplosionSpawnThingDef ??= pp.postExplosionSpawnThingDef;
                if (postExplosionSpawnChance == 0f)
                    postExplosionSpawnChance = pp.postExplosionSpawnChance;
                if (postExplosionSpawnThingCount <= 0)
                    postExplosionSpawnThingCount = pp.postExplosionSpawnThingCount;
            }

            if (preExplosionSpawnThingCount <= 0) preExplosionSpawnThingCount = 1;
            if (postExplosionSpawnThingCount <= 0) postExplosionSpawnThingCount = 1;
        }
    }
}
