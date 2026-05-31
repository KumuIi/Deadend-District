using System.Collections.Generic;
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

    /// <summary>Current Light.intensity (0 while off). Read by visibility contributors.</summary>
    public float Intensity => _light != null && _light.enabled ? _light.intensity : 0f;
    /// <summary>World position of the emitting light.</summary>
    public Vector3 Position => transform.position;

    // ── Active-light registry ───────────────────────────────────────────────
    // Lights register themselves while enabled so LightIntensityContributor can
    // iterate only the handful that are live, without scene-wide FindObjectsOfType.

    private static readonly List<LightSource> _active = new List<LightSource>();
    public static IReadOnlyList<LightSource> Active => _active;

    private void Awake() => _light = GetComponent<Light>();

    private void OnEnable()
    {
        if (!_active.Contains(this)) _active.Add(this);
    }

    private void OnDisable() => _active.Remove(this);

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
