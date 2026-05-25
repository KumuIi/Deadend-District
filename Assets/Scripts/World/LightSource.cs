using UnityEngine;

public enum LightMode { Off, Dim, Bright }

/// <summary>
/// Drives a hand-held light: on/off/dim modes and toggle audio.
/// Drain rate is exposed as a property — FlashlightSlot owns the drain loop.
/// T key toggles between Off and Bright while gameplay is not blocked.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightSource : MonoBehaviour
{
    [SerializeField] private float     _dimDrainRate    = 3f;
    [SerializeField] private float     _brightDrainRate = 8f;
    [SerializeField] private float     _dimIntensity    = 0.6f;
    [SerializeField] private float     _brightIntensity = 1.4f;
    [SerializeField] private AudioClip _toggleClip;

    private Light     _light;
    private LightMode _mode = LightMode.Off;

    public bool      IsOn        => _mode != LightMode.Off;
    public LightMode CurrentMode => _mode;
    public float     DrainRate   => _mode switch
    {
        LightMode.Dim    => _dimDrainRate,
        LightMode.Bright => _brightDrainRate,
        _                => 0f,
    };

    private void Awake() => _light = GetComponent<Light>();

    private void Update()
    {
        if (!GameInputState.GameplayBlocked && Input.GetKeyDown(KeyCode.T))
            Toggle();
    }

    public void Toggle()
    {
        SetMode(_mode == LightMode.Off ? LightMode.Bright : LightMode.Off);
        if (_toggleClip != null)
            AudioSource.PlayClipAtPoint(_toggleClip, transform.position);
    }

    public void SetMode(LightMode mode)
    {
        _mode            = mode;
        if (_light == null) return;
        _light.enabled   = mode != LightMode.Off;
        _light.intensity = mode == LightMode.Dim    ? _dimIntensity
                         : mode == LightMode.Bright ? _brightIntensity
                         : 0f;
    }

    /// <summary>Turns off the light without playing toggle audio. Called by FlashlightSlot on depletion.</summary>
    public void ForceOff() => SetMode(LightMode.Off);

    /// <summary>Toggles the light mesh on/off without changing LightMode or drain rate.</summary>
    public void FlickerVisual(bool on)
    {
        if (_light != null) _light.enabled = on;
    }
}
