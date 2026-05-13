using UnityEngine;

[CreateAssetMenu(fileName = "NewAmmo", menuName = "Deadend District/Ammunition")]
public class AmmunitionSO : ScriptableObject
{
    [Header("=== Identity ===")]
    public string ammoName = "9x19 FMJ";
    public string caliber  = "9x19";

    [Header("=== Ballistics ===")]
    public float damage   = 25f;
    [Tooltip("Muzzle velocity in m/s. Higher velocity = flatter damage falloff curve.")]
    public float velocity = 375f;

    [Tooltip("Damage multiplier over normalized distance (0 = muzzle, 1 = max weapon range). " +
             "High-velocity rounds should have a flatter curve; subsonic rounds drop off faster.")]
    public AnimationCurve damageFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.6f);

    [Header("=== Explosive ===")]
    public bool  isExplosive     = false;
    public float explosionRadius = 2f;
    [Tooltip("Force applied to Rigidbodies inside the blast radius")]
    public float explosionForce  = 500f;

    [Header("=== Penetration ===")]
    [Tooltip("Armor penetration value — reserved for the future armor/damage system")]
    public float armorPenetration = 20f;

    /// <summary>Returns damage after applying the falloff curve at the given distance.</summary>
    public float GetDamageAtDistance(float distance, float weaponRange)
    {
        float t          = weaponRange > 0f ? Mathf.Clamp01(distance / weaponRange) : 0f;
        float multiplier = damageFalloff != null ? damageFalloff.Evaluate(t) : 1f;
        return damage * multiplier;
    }
}
