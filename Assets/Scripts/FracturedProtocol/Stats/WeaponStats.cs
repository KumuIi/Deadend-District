#nullable enable

namespace FracturedProtocol.Combat.Stats
{
    /// <summary>
    /// Fully-resolved weapon stats after all attachments and magazine modifiers are applied.
    /// Recalculated only on equip, attach, detach, and reload — never per frame.
    /// </summary>
    public struct WeaponStats
    {
        public float spread;
        public float fireRate;
        public float damage;
        public float penetration;
        public float muzzleVelocity;
        public float recoilX;
        public float recoilY;
    }
}
