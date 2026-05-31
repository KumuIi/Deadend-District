using UnityEngine;

/// <summary>
/// Visibility contributor for how brightly lit the player is. Sums the inverse-square
/// contribution of every active <see cref="LightSource"/> in range and clamps to [0..1].
///
/// Unity has no "light intensity at point" API, so this approximates it on the CPU:
///   contribution = intensity / (distance² + softening)
/// summed over active lights, then mapped to 0..1. A bright light right on the player
/// reads ~1; standing in the dark with all lights off reads near the ambient floor.
///
/// The player's own held flashlight counts — carrying a bright light makes you easier
/// to spot, which is the intended stealth trade-off.
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
    [Tooltip("Accumulated contribution that maps to full visibility (1.0). Tune to taste.")]
    [SerializeField] private float _fullVisibilityAt = 1.2f;
    [Tooltip("Minimum visibility even in total darkness (silhouette, ambient bounce).")]
    [Range(0f, 1f)]
    [SerializeField] private float _ambientFloor = 0.05f;

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

        float lit = _fullVisibilityAt > 0f ? accum / _fullVisibilityAt : accum;
        return Mathf.Clamp01(Mathf.Max(_ambientFloor, lit));
    }
}
