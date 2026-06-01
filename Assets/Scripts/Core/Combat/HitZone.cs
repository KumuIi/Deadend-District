using UnityEngine;

/// <summary>
/// Locational-damage tag placed on an enemy body-part collider (head, torso, limb).
/// A pure data carrier — it holds no logic. The damage-producing code (GunController,
/// later Melee/HazardZone) reads the zone off the collider it hit and scales BaseDamage.
///
/// Because Deadend's enemies are physics-driven from frame 1 (see EnemyRagdoll), a
/// weapon raycast strikes the bone collider directly, so <see cref="Resolve"/> reads
/// from the hit collider — no parent traversal, and it behaves identically whether the
/// enemy is upright or ragdolling.
///
/// Setup: add one child collider + HitZone per body part. Keep the colliders on a layer
/// the weapon's hitLayers mask includes (e.g. Enemy) but off the NavMesh-blocking path —
/// use trigger colliders or the IgnoreRaycast layer if they interfere with agent pathing.
/// </summary>
public sealed class HitZone : MonoBehaviour
{
    [Tooltip("Identifier surfaced in DamageContext.HitZoneId (e.g. \"head\", \"torso\", \"limb\"). " +
             "Downstream consumers (hitmarkers, headshot SFX) match on this.")]
    [SerializeField] private string _zoneId = "torso";

    [Tooltip("Damage scalar for this zone. Convention: head 2.5, torso 1.0, limb 0.7.")]
    [SerializeField] private float _damageMultiplier = 1f;

    public string ZoneId           => _zoneId;
    public float  DamageMultiplier => _damageMultiplier;

    /// <summary>
    /// Reads the HitZone (if any) off a struck collider. Returns the zone id and damage
    /// multiplier; falls back to ("", 1f) for untagged colliders so callers never special-case.
    /// </summary>
    public static void Resolve(Collider col, out string zoneId, out float multiplier)
    {
        if (col != null && col.TryGetComponent(out HitZone zone))
        {
            zoneId     = zone._zoneId;
            multiplier = zone._damageMultiplier;
        }
        else
        {
            zoneId     = "";
            multiplier = 1f;
        }
    }
}
