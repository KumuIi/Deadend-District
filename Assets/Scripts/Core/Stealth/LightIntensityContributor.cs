using UnityEngine;

/// <summary>
/// Visibility contributor for how brightly lit the player is. In this game light only
/// makes the player *more* visible — it never hides them. With no light on the player,
/// this returns <see cref="_baseVisibility"/> (the player is normally visible); active
/// lights push the factor up toward 1.0.
///
/// Unity has no "light intensity at point" API, so this approximates incident light on
/// the CPU:
///   contribution = intensity / (distance² + softening)
/// summed over active lights and normalised by <see cref="_fullVisibilityAt"/>, then
/// used to lerp from the base up to full visibility.
///
/// The player's own held flashlight is the main contributor — carrying a bright light
/// makes you easier to spot, which is the intended stealth trade-off.
/// </summary>
public class LightIntensityContributor : MonoBehaviour, IVisibilityContributor
{
    [Tooltip("PlayerVisibility this contributor registers with. Falls back to the singleton.")]
    [SerializeField] private PlayerVisibility _visibility;
    [Tooltip("Point sampled for incident light — usually the player's torso/head.")]
    [SerializeField] private Transform _samplePoint;

    [Tooltip("Lights farther than this are ignored.")]
    [SerializeField] private float _maxLightDistance = 25f;
    [Tooltip("Added to distance² so a light exactly on the player doesn't divide by ~0.")]
    [SerializeField] private float _softening = 1f;
    [Tooltip("Accumulated light contribution that pushes the player to full visibility (1.0).")]
    [SerializeField] private float _fullVisibilityAt = 1.2f;
    [Tooltip("Visibility with NO light on the player. This is the normal exposed level — " +
             "light only raises it toward 1.0, never below this. Keep high so guards still " +
             "see an unlit player; the flashlight just makes you spotted from farther.")]
    [Range(0f, 1f)]
    [SerializeField] private float _baseVisibility = 0.8f;

    public string ContributorName => "Light";

    // Register in BOTH OnEnable and Start: OnEnable only sees a non-null singleton if
    // PlayerVisibility.Awake already ran (guaranteed only on the same GameObject). Start
    // always runs after every Awake, so it backstops a missed OnEnable registration.
    // Register dedups, so calling twice is harmless.
    private void OnEnable() => Resolve()?.Register(this);
    private void Start()    => Resolve()?.Register(this);
    private void OnDisable() => Resolve()?.Unregister(this);

    private PlayerVisibility Resolve() =>
        _visibility != null ? _visibility : PlayerVisibility.Instance;

    public float GetVisibilityFactor()
    {
        Vector3 p = _samplePoint != null ? _samplePoint.position : transform.position;
        float maxSqr = _maxLightDistance * _maxLightDistance;

        float accum = 0f;
        var lights = LightSource.Active;
        for (int i = 0; i < lights.Count; i++)
        {
            var ls = lights[i];
            if (ls == null || !ls.IsOn) continue;

            float sqr = (ls.Position - p).sqrMagnitude;
            if (sqr > maxSqr) continue;

            accum += ls.Intensity / (sqr + Mathf.Max(0.01f, _softening));
        }

        // Normalised light term in [0..1], then lerp from the base up to full visibility.
        // No light → _baseVisibility (normal exposure); strong light → 1.0.
        float lit = _fullVisibilityAt > 0f ? Mathf.Clamp01(accum / _fullVisibilityAt) : Mathf.Clamp01(accum);
        return Mathf.Clamp01(_baseVisibility + lit * (1f - _baseVisibility));
    }
}
