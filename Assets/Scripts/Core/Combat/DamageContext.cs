using UnityEngine;

public enum DamageType { Bullet, Melee, Explosive, Fall, Hazard }

/// <summary>
/// Describes a single damage event. Passed to IDamageable.ApplyDamage.
/// Source = the weapon/hazard GameObject. Instigator = who pulled the trigger.
/// StimulusLoudness feeds StimulusSystem — gunfire ~1.0, melee ~0.1, hazard 0.
/// </summary>
public struct DamageContext
{
    public GameObject Source;
    public GameObject Instigator;
    public Vector3    HitPoint;
    public Vector3    HitNormal;
    /// <summary>"head", "torso", "limb", or "" for no zone.</summary>
    public string     HitZoneId;
    public DamageType Type;
    public float      BaseDamage;
    /// <summary>Physics push force magnitude applied to the hit Rigidbody.</summary>
    public float      Impulse;
    /// <summary>[0..1]. Damage handlers can auto-broadcast a noise stimulus from this.</summary>
    public float      StimulusLoudness;
}
