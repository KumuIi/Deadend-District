using UnityEngine;

/// <summary>
/// Single funnel for every gameplay noise the owner produces. Applies the owner's
/// NoiseMultiplier stat (encumbrance, augments, crouch perks…) to a profile's
/// BaseRadius, then broadcasts one <see cref="Stimulus"/> through the StimulusSystem.
///
/// Why route everything through here instead of calling Broadcast directly:
///   - One place applies the noise multiplier, so encumbrance affects ALL sounds.
///   - Instigator is set consistently (the player root), so AI can ignore its own
///     noise and correctly attribute the player's footsteps/gunshots.
///
/// Used by: FootstepAudio, GunController, InventoryUI (drop), LootItemWorld (pickup).
/// Wave 5 DistractionMechanic calls Emit(profile, landingPosition) — the position is
/// per-call, so a thrown object makes noise where it lands, not on the player.
/// </summary>
public class NoiseEmitter : MonoBehaviour
{
    [Tooltip("Motor whose NoiseMultiplier stat scales every emitted radius. " +
             "Leave null on non-player emitters (mult stays 1).")]
    [SerializeField] private PlayerMotor _motor;

    [Tooltip("Actor credited as the cause of the noise (so AI can ignore its own). " +
             "Defaults to this GameObject when left null — set to the player root " +
             "for child emitters such as the gun.")]
    [SerializeField] private GameObject _instigatorOverride;

    private GameObject Instigator => _instigatorOverride != null ? _instigatorOverride : gameObject;

    private float NoiseMult =>
        _motor != null ? Mathf.Max(0f, _motor.StatModifiers.Net(StatType.NoiseMultiplier)) : 1f;

    /// <summary>Emit a profiled noise at this emitter's position.</summary>
    public void Emit(NoiseProfileSO profile) => Emit(profile, transform.position);

    /// <summary>Emit a profiled noise at an arbitrary world position (thrown items, impacts).</summary>
    public void Emit(NoiseProfileSO profile, Vector3 position)
    {
        if (profile == null) return;
        Emit(profile.StimulusType, profile.BaseRadius, profile.Intensity, position);
    }

    /// <summary>
    /// Explicit overload for callers that compute radius/intensity in code
    /// (e.g. FootstepAudio choosing walk vs sprint). Still applies the noise multiplier.
    /// </summary>
    public void Emit(StimulusType type, float baseRadius, float intensity, Vector3 position)
    {
        float radius = baseRadius * NoiseMult;
        if (radius <= 0f) return; // silent (e.g. crouch profile with BaseRadius 0)

        StimulusSystem.Instance?.Broadcast(new Stimulus(
            type,
            position,
            radius:     radius,
            intensity:  intensity,
            source:     gameObject,
            instigator: Instigator));
    }
}
