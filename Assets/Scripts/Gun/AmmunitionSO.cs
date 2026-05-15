using UnityEngine;

/// <summary>
/// Data asset for a single ammunition type.
/// Extends ItemSO so ammo boxes can be placed in the inventory grid.
/// Assign the same CaliberSO to link it to compatible weapons and magazines.
///
/// Migration note: rename any existing "ammoName" field value to "itemName" in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "NewAmmo", menuName = "Deadend District/Ammunition")]
public class AmmunitionSO : ItemSO
{
    [Header("=== Ammo ===")]
    [Tooltip("Must reference the same CaliberSO as the weapon and magazine.")]
    public CaliberSO caliber;

    [Tooltip("How many rounds are in one inventory box of this type.")]
    public int stackSize = 30;

    [Header("=== Ballistics ===")]
    public float damage = 25f;
    [Tooltip("Muzzle velocity in m/s. Higher velocity = flatter damage falloff curve.")]
    public float velocity = 375f;
    [Tooltip("Damage multiplier over normalised distance (0 = muzzle, 1 = max range).")]
    public AnimationCurve damageFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.6f);

    [Header("=== Explosive ===")]
    public bool isExplosive = false;
    public float explosionRadius = 2f;
    [Tooltip("Force applied to Rigidbodies inside the blast radius.")]
    public float explosionForce = 500f;

    [Header("=== Penetration ===")]
    [Tooltip("Armor penetration value — reserved for the future armor/damage system.")]
    public float armorPenetration = 20f;

    /// <summary>Returns damage after applying the falloff curve at the given distance.</summary>
    public float GetDamageAtDistance(float distance, float weaponRange)
    {
        float t = weaponRange > 0f ? Mathf.Clamp01(distance / weaponRange) : 0f;
        float multiplier = damageFalloff != null ? damageFalloff.Evaluate(t) : 1f;
        return damage * multiplier;
    }
}
