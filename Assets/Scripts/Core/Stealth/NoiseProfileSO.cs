using UnityEngine;

/// <summary>
/// Data-driven description of a single noise event (footstep, gunshot, item drop…).
///
/// The <see cref="BaseRadius"/> is the *unencumbered* hearing radius in metres.
/// <see cref="NoiseEmitter"/> scales it by the emitter's NoiseMultiplier stat
/// before broadcasting, so a heavily-loaded player is louder than a light one.
///
/// Suggested radii: Walk = 4, Sprint = 8, Gunshot = 40, Reload = 6, Crouch = 0 (silent).
/// </summary>
[CreateAssetMenu(menuName = "Stealth/Noise Profile", fileName = "NoiseProfile")]
public class NoiseProfileSO : ScriptableObject
{
    [Tooltip("Unencumbered hearing radius in metres. 0 = silent (no broadcast).")]
    public float BaseRadius = 4f;

    [Tooltip("Usually Sound. Explosion/Damage are also valid for special emitters.")]
    public StimulusType StimulusType = StimulusType.Sound;

    [Tooltip("Normalised loudness 0..1. AI uses this to grade its reaction " +
             "(faint = grow suspicious, loud = investigate immediately).")]
    [Range(0f, 1f)]
    public float Intensity = 0.3f;
}
