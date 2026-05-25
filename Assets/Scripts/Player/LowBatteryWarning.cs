using System.Collections;
using UnityEngine;

/// <summary>
/// Low battery behavior:
///   1. Charge drops below _warnThreshold (20%) → play warning sound ONCE + flicker for 1 second.
///   2. After 1 second of flicker → switch to LightMode.Dim (light stays on but lower).
///   3. Charge hits 0 → FlashlightSlot handles ForceOff (not this component).
///
/// Hysteresis: clears above _clearThreshold (25%) if battery is swapped back up.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class LowBatteryWarning : MonoBehaviour
{
    [SerializeField] private FlashlightSlot _flashlightSlot;
    [SerializeField] private AudioClip      _warningClip;
    [SerializeField] private float          _warnThreshold      = 0.20f;
    [SerializeField] private float          _clearThreshold     = 0.25f;
    [SerializeField] private float          _flickerDuration    = 1f;
    [SerializeField] private float          _flickerMinInterval = 0.05f;
    [SerializeField] private float          _flickerMaxInterval = 0.15f;

    private AudioSource _audioSource;
    private Coroutine   _flickerRoutine;
    private bool        _warning;

    private LightSource Light => _flashlightSlot?.LightSource;

    private void Awake()
    {
        _audioSource             = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop        = false;
    }

    private void OnEnable()
    {
        if (_flashlightSlot == null) return;
        _flashlightSlot.OnChargeChanged += HandleChargeChanged;
        _flashlightSlot.OnDepleted      += HandleDepleted;
        _flashlightSlot.OnRestored      += HandleRestored;
    }

    private void OnDisable()
    {
        StopWarning();
        if (_flashlightSlot == null) return;
        _flashlightSlot.OnChargeChanged -= HandleChargeChanged;
        _flashlightSlot.OnDepleted      -= HandleDepleted;
        _flashlightSlot.OnRestored      -= HandleRestored;
    }

    private void HandleChargeChanged(float normalized)
    {
        if (!_warning && normalized <= _warnThreshold) StartWarning();
        if (_warning  && normalized >  _clearThreshold) StopWarning();
    }

    private void HandleDepleted() => StopWarning();

    private void HandleRestored()
    {
        // OnChargeChanged fires before this — if the restored battery is still below the
        // clear threshold, leave the warning active rather than blindly cancelling it.
        if (_flashlightSlot != null && _flashlightSlot.ChargeNormalized <= _warnThreshold)
            return;
        StopWarning();
    }

    private void StartWarning()
    {
        if (_warning) return;
        var l = Light;
        if (l == null || !l.IsOn) return; // only warn if light is actually on

        _warning = true;

        // Play once — not looping
        if (_warningClip != null)
            _audioSource.PlayOneShot(_warningClip);

        if (_flickerRoutine != null) StopCoroutine(_flickerRoutine);
        _flickerRoutine = StartCoroutine(FlickerThenDimRoutine());
    }

    private void StopWarning()
    {
        if (!_warning && _flickerRoutine == null) return;
        _warning = false;
        if (_flickerRoutine != null) { StopCoroutine(_flickerRoutine); _flickerRoutine = null; }
        _audioSource.Stop();

        // Restore light to its natural on/off state (don't force it off)
        var l = Light;
        if (l != null) l.FlickerVisual(l.IsOn);
    }

    private IEnumerator FlickerThenDimRoutine()
    {
        float elapsed = 0f;

        // Phase 1: flicker for _flickerDuration seconds
        while (elapsed < _flickerDuration)
        {
            var l = Light;
            if (l != null && l.IsOn) l.FlickerVisual(true);
            float wait = Random.Range(_flickerMinInterval, _flickerMaxInterval);
            yield return new WaitForSeconds(wait);
            elapsed += wait;

            l = Light;
            if (l != null && l.IsOn) l.FlickerVisual(false);
            wait = Random.Range(_flickerMinInterval, _flickerMaxInterval);
            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }

        // Phase 2: restore visibility then drop to dim
        var light = Light;
        if (light != null && light.IsOn)
        {
            light.FlickerVisual(true);
            light.SetMode(LightMode.Dim);
        }

        _flickerRoutine = null;
    }
}
