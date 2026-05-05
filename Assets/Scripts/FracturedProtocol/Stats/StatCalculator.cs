#nullable enable
using FracturedProtocol.Combat.Instances;
using FracturedProtocol.Combat.Items;

namespace FracturedProtocol.Combat.Stats
{
    /// <summary>
    /// Recalculates WeaponInstance.effectiveStats from its WeaponSO base values
    /// and all active modifiers. Called on equip, attach, detach, and reload only.
    /// </summary>
    public static class StatCalculator
    {
        /// <summary>
        /// Walk all attachments, apply their modifiers to the weapon's base stats,
        /// and write the result to weapon.effectiveStats.
        /// </summary>
        public static void Recalculate(WeaponInstance weapon)
        {
            if (weapon.def is not WeaponSO weaponDef) return;

            WeaponStats stats = new WeaponStats
            {
                spread   = weaponDef.baseSpread,
                fireRate = weaponDef.fireRate,
                recoilX  = weaponDef.recoilPattern.x,
                recoilY  = weaponDef.recoilPattern.y,
            };

            foreach (AttachmentInstance att in weapon.attachments)
            {
                if (att.def is not AttachmentSO attDef) continue;
                foreach (StatModifier mod in attDef.modifiers)
                    Apply(ref stats, mod);
            }

            weapon.effectiveStats = stats;
        }

        private static void Apply(ref WeaponStats stats, StatModifier mod)
        {
            switch (mod.statType)
            {
                case StatType.Spread:
                    stats.spread = mod.operation == ModifierOperation.Additive
                        ? stats.spread + mod.value
                        : stats.spread * mod.value;
                    break;
                case StatType.FireRate:
                    stats.fireRate = mod.operation == ModifierOperation.Additive
                        ? stats.fireRate + mod.value
                        : stats.fireRate * mod.value;
                    break;
                case StatType.Damage:
                    stats.damage = mod.operation == ModifierOperation.Additive
                        ? stats.damage + mod.value
                        : stats.damage * mod.value;
                    break;
                case StatType.Penetration:
                    stats.penetration = mod.operation == ModifierOperation.Additive
                        ? stats.penetration + mod.value
                        : stats.penetration * mod.value;
                    break;
                case StatType.MuzzleVelocity:
                    stats.muzzleVelocity = mod.operation == ModifierOperation.Additive
                        ? stats.muzzleVelocity + mod.value
                        : stats.muzzleVelocity * mod.value;
                    break;
            }
        }
    }
}
