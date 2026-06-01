using UnityEngine;

/// <summary>
/// Applies fall damage to the player on hard landings (W3-08).
///
/// Reads impact speed from <see cref="PlayerMotor"/> instead of tracking takeoff height,
/// so it works for any fall — ledges, knockback, explosion launches — and is independent
/// of the motor's gravity setting. Damage is routed through <see cref="IDamageable"/> via a
/// <see cref="DamageContext"/> (Type = Fall), the same channel bullets/melee/hazards use.
///
/// Place on the player root alongside PlayerMotor + PlayerHealth and wire both refs.
/// </summary>
public sealed class FallDamage : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMotor  _motor;
    [SerializeField] private PlayerHealth _health;

    [Header("Tuning")]
    [Tooltip("Impact speed (m/s, downward) below which a landing is harmless. " +
             "~10 m/s ≈ a 5 m drop under standard gravity.")]
    [Min(0f)]
    [SerializeField] private float _safeLandingSpeed = 10f;

    [Tooltip("Damage dealt per m/s of impact speed above the safe threshold.")]
    [Min(0f)]
    [SerializeField] private float _damagePerSpeedOverThreshold = 8f;

    [Tooltip("Seconds of energy-regen suppression applied after a damaging landing.")]
    [Min(0f)]
    [SerializeField] private float _regenSuppressOnLanding = 1.5f;

    // ── Runtime ─────────────────────────────────────────────────────────────

    private bool  _wasGrounded = true;
    private float _fallVelocity;   // most-negative vertical velocity seen this airborne window

    private void Awake()
    {
        if (_motor  == null) Debug.LogError($"[FallDamage] {name}: PlayerMotor ref is null — no fall damage.", this);
        if (_health == null) Debug.LogError($"[FallDamage] {name}: PlayerHealth ref is null — no fall damage.", this);
    }

    private void FixedUpdate()
    {
        if (_motor == null || _health == null) return;

        bool  grounded = _motor.IsGrounded;
        float vy       = _motor.VerticalVelocity;

        if (!grounded)
        {
            // Airborne: remember the fastest downward speed (most negative vy).
            if (vy < _fallVelocity) _fallVelocity = vy;
        }
        else
        {
            if (!_wasGrounded) OnLanded(-_fallVelocity); // rising edge: just touched down
            _fallVelocity = 0f;
        }

        _wasGrounded = grounded;
    }

    /// <param name="impactSpeed">Downward landing speed in m/s (positive).</param>
    private void OnLanded(float impactSpeed)
    {
        float over = impactSpeed - _safeLandingSpeed;
        if (over <= 0f) return;

        float damage = over * _damagePerSpeedOverThreshold;

        _health.ApplyDamage(new DamageContext
        {
            Source           = gameObject,
            Instigator       = gameObject,
            HitPoint         = transform.position,
            HitNormal        = Vector3.up,
            HitZoneId        = "",
            Type             = DamageType.Fall,
            BaseDamage       = damage,
            Impulse          = 0f,
            // Hard landings thud — scale loudness with severity for future AI hearing.
            StimulusLoudness = Mathf.Clamp01(over / _safeLandingSpeed) * 0.5f,
        });

        if (_regenSuppressOnLanding > 0f)
            _health.SuppressRegen(_regenSuppressOnLanding);
    }
}
