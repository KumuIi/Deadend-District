using UnityEngine;

/// <summary>
/// Drives a hand-held light: on/off/dim modes, battery drain, and toggle audio.
/// Implements IBatteryDrainer — self-registers with BatterySystem in OnEnable.
/// F key toggles between Off and Bright while gameplay is not blocked.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightSource : MonoBehaviour, ILightSource, IBatteryDrainer
{
    [SerializeField] private float     _dimDrainRate    = 3f;
    [SerializeField] private float     _brightDrainRate = 8f;
    [SerializeField] private float     _dimIntensity    = 0.6f;
    [SerializeField] private float     _brightIntensity = 1.4f;
    [SerializeField] private AudioClip _toggleClip;

    private Light     _light;
    private LightMode _mode = LightMode.Off;

    // ── ILightSource ───────────────────────────────────────────────────────

    public bool      IsOn        => _mode != LightMode.Off;
    public float     Intensity   => _light != null ? _light.intensity : 0f;
    public LightMode CurrentMode => _mode;

    // ── IBatteryDrainer ────────────────────────────────────────────────────

    public string DrainerName => "Flashlight";
    public float  DrainRate   => _mode switch
    {
        LightMode.Dim    => _dimDrainRate,
        LightMode.Bright => _brightDrainRate,
        _                => 0f,
    };

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake() => _light = GetComponent<Light>();

    private void OnEnable()
    {
        BatterySystem.Instance?.RegisterDrainer(this);
        if (BatterySystem.Instance != null)
            BatterySystem.Instance.OnBatteryDepleted += HandleBatteryDepleted;
    }

    private void OnDisable()
    {
        BatterySystem.Instance?.UnregisterDrainer(this);
        if (BatterySystem.Instance != null)
            BatterySystem.Instance.OnBatteryDepleted -= HandleBatteryDepleted;
    }

    private void Update()
    {
        if (!GameInputState.GameplayBlocked && Input.GetKeyDown(KeyCode.F))
            Toggle();
    }

    // ── ILightSource impl ──────────────────────────────────────────────────

    public void Toggle()
    {
        SetMode(_mode == LightMode.Off ? LightMode.Bright : LightMode.Off);
        if (_toggleClip != null)
            AudioSource.PlayClipAtPoint(_toggleClip, transform.position);
    }

    public void SetMode(LightMode mode)
    {
        _mode = mode;
        if (_light == null) return;

        _light.enabled = mode != LightMode.Off;
        _light.intensity = mode == LightMode.Dim    ? _dimIntensity
                         : mode == LightMode.Bright ? _brightIntensity
                         : 0f;
    }

    public void FlickerVisual(bool on)
    {
        if (_light != null) _light.enabled = on;
    }

    // ── Battery depletion ──────────────────────────────────────────────────

    private void HandleBatteryDepleted() => SetMode(LightMode.Off);
}
