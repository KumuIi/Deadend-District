using UnityEngine;

/// <summary>
/// Designer-tunable encumbrance config. Assign one asset to EncumbranceSystem.
/// All curves use X = currentWeight / maxCarryWeightKg (0 = empty, 1 = at cap, 1+ = overloaded).
/// Y axis is always a multiplier applied to the base value.
/// </summary>
[CreateAssetMenu(fileName = "EncumbranceConfig", menuName = "Deadend District/Encumbrance Config")]
public class EncumbranceSO : ScriptableObject
{
    [Header("=== Carry Capacity ===")]
    [Tooltip("Maximum carry weight in kilograms before the player is considered overloaded.")]
    public float maxCarryWeightKg = 40f;

    [Tooltip("Weight ratio at which sprinting is blocked. 1.0 = exactly at max capacity.")]
    [Range(0.5f, 1.5f)]
    public float sprintBlockThreshold = 1.0f;

    [Header("=== Speed Penalty ===")]
    [Tooltip("Speed multiplier by weight ratio. X=ratio (0-1.5+), Y=speed multiplier (0-1). " +
             "Default: 1.0 at 0%, ~0.65 at 100%, ~0.4 at 150%.")]
    public AnimationCurve speedPenaltyCurve = DefaultSpeedCurve();

    [Header("=== Stamina Drain ===")]
    [Tooltip("Stamina drain multiplier by weight ratio. Y>1 = drains faster. " +
             "Default: 1.0 at 0%, ~1.6 at 100%, ~2.5 at 150%.")]
    public AnimationCurve staminaDrainCurve = DefaultStaminaCurve();

    [Header("=== Noise ===")]
    [Tooltip("Noise radius multiplier by weight ratio. Heavier = louder footsteps. " +
             "Default: 1.0 at 0%, ~1.4 at 100%, ~1.8 at 150%.")]
    public AnimationCurve noiseCurve = DefaultNoiseCurve();

    [Header("=== Feel / Bob ===")]
    [Tooltip("Bob frequency multiplier by weight ratio. Lower = slower, heavier-feeling steps. " +
             "Default: 1.0 at 0%, ~0.75 at 100%, ~0.55 at 150%.")]
    public AnimationCurve bobFrequencyCurve = DefaultBobCurve();

    [Header("=== Stamina Regen ===")]
    [Tooltip("Regen rate multiplier by weight ratio. Y<1 = slower regen. Default: 1.0 at 0%, 0.5 at 100%, 0.35 at 150%.")]
    public AnimationCurve regenPenaltyCurve = DefaultRegenCurve();

    [Tooltip("Flat stamina drain per second while walking (scales with StaminaDrain modifiers). 0 = no walk drain.")]
    public float walkDrainRate = 2f;

    [Tooltip("EnergyRegen multiplier when crouching. Stacks multiplicatively with weight regen penalty.")]
    [Range(1f, 3f)]
    public float crouchRegenMultiplier = 1.5f;

    [Header("=== Jump ===")]
    [Tooltip("Jump force multiplier by weight ratio. Y<1 = lower jump. Default: 1.0 at 0%, 0.65 at 100%.")]
    public AnimationCurve jumpForceCurve = DefaultJumpForceCurve();

    [Tooltip("Jump delay in seconds by weight ratio. Player must hold jump before takeoff. Default: 0 at 0%, 0.25 at 100%.")]
    public AnimationCurve jumpDelayCurve = DefaultJumpDelayCurve();

    // ── Default curves ─────────────────────────────────────────────────────

    private static AnimationCurve DefaultSpeedCurve() => new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.60f, 0.85f),
        new Keyframe(0.85f, 0.65f),
        new Keyframe(1.00f, 0.55f),
        new Keyframe(1.50f, 0.40f));

    private static AnimationCurve DefaultStaminaCurve() => new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.60f, 1.20f),
        new Keyframe(0.85f, 1.60f),
        new Keyframe(1.00f, 2.00f),
        new Keyframe(1.50f, 2.50f));

    private static AnimationCurve DefaultNoiseCurve() => new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.60f, 1.15f),
        new Keyframe(0.85f, 1.40f),
        new Keyframe(1.00f, 1.60f),
        new Keyframe(1.50f, 1.80f));

    private static AnimationCurve DefaultBobCurve() => new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.60f, 0.88f),
        new Keyframe(0.85f, 0.75f),
        new Keyframe(1.00f, 0.65f),
        new Keyframe(1.50f, 0.55f));

    private static AnimationCurve DefaultRegenCurve() => new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.60f, 0.85f),
        new Keyframe(0.85f, 0.65f),
        new Keyframe(1.00f, 0.50f),
        new Keyframe(1.50f, 0.35f));

    private static AnimationCurve DefaultJumpForceCurve() => new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.60f, 0.90f),
        new Keyframe(0.85f, 0.75f),
        new Keyframe(1.00f, 0.65f),
        new Keyframe(1.50f, 0.55f));

    private static AnimationCurve DefaultJumpDelayCurve() => new AnimationCurve(
        new Keyframe(0f,    0.00f),
        new Keyframe(0.60f, 0.00f),
        new Keyframe(0.85f, 0.10f),
        new Keyframe(1.00f, 0.25f),
        new Keyframe(1.50f, 0.50f));

    // ── Validation ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxCarryWeightKg    = Mathf.Max(1f,    maxCarryWeightKg);
        sprintBlockThreshold = Mathf.Max(0.01f, sprintBlockThreshold);
    }

    private void Reset()
    {
        speedPenaltyCurve  = DefaultSpeedCurve();
        staminaDrainCurve  = DefaultStaminaCurve();
        noiseCurve         = DefaultNoiseCurve();
        bobFrequencyCurve  = DefaultBobCurve();
        regenPenaltyCurve  = DefaultRegenCurve();
        jumpForceCurve     = DefaultJumpForceCurve();
        jumpDelayCurve     = DefaultJumpDelayCurve();
    }
#endif
}
