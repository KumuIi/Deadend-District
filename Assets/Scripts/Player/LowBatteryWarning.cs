using System.Collections;
using UnityEngine;

/// <summary>
/// Flickers the light and plays a looping audio warning when battery charge is low.
/// Hysteresis: warn below _warnThreshold (20%), clear above _clearThreshold (25%).
///
/// Assign _flashlightSlot in the Inspector — LightSource is read from it at runtime
/// so there is no cross-prefab drag-in required.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class LowBatteryWarning : MonoBehaviour
{
    [SerializeField] private FlashlightSlot _flashlightSlot;
    [SerializeField] private AudioClip      _warningClip;
    [SerializeField] private float          _warnThreshold      = 0.20f;
    [SerializeField] private float          _clearThreshold     = 0.25f;
    [SerializeField] private float          _flickerMinInterval = 0.05f;
    [SerializeField] private float          _flickerMaxInterval = 0.15f;

    private AudioSource _audioSource;
    private Coroutine   _flickerRoutine;
    private bool        _warning;

    // Grabbed from FlashlightSlot at runtime — null when no flashlight is equipped.
    private LightSource Light => _flashlightSlot?.LightSource;

    private void Awake()
    {
        _audioSource           = GetComponent<AudioSource>();
        _audioSource.clip      = _warningClip;
        _audioSource.loop      = true;
        _audioSource.playOnAwake = false;
    }

    private void OnEnable()
    {
        var bs = BatterySystem.Instance;
        if (bs == null) return;
        bs.OnChargeChanged   += HandleChargeChanged;
        bs.OnBatteryDepleted += HandleDepleted;
    }

    private void OnDisable()
    {
        StopWarning();
        var bs = BatterySystem.Instance;
        if (bs == null) return;
        bs.OnChargeChanged   -= HandleChargeChanged;
        bs.OnBatteryDepleted -= HandleDepleted;
    }

    private void HandleChargeChanged(float normalized)
    {
        if (normalized <= 0f) { StopWarning(); return; }
        if (!_warning && normalized <= _warnThreshold) StartWarning();
        if (_warning  && normalized >  _clearThreshold) StopWarning();
    }

    private void HandleDepleted() => StopWarning();

    private void StartWarning()
    {
        if (_warning) return;
        _warning = true;
        if (_flickerRoutine != null) StopCoroutine(_flickerRoutine);
        _flickerRoutine = StartCoroutine(FlickerRoutine());
        if (!_audioSource.isPlaying) _audioSource.Play();
    }

    private void StopWarning()
    {
        if (!_warning && _flickerRoutine == null) return;
        _warning = false;
        if (_flickerRoutine != null) { StopCoroutine(_flickerRoutine); _flickerRoutine = null; }
        _audioSource.Stop();
        var l = Light;
        if (l != null) l.FlickerVisual(l.IsOn);
    }

    private IEnumerator FlickerRoutine()
    {
        while (true)
        {
            var l = Light;
            if (l != null && l.IsOn) l.FlickerVisual(true);
            yield return new WaitForSeconds(Random.Range(_flickerMinInterval, _flickerMaxInterval));
            l = Light;
            if (l != null && l.IsOn) l.FlickerVisual(false);
            yield return new WaitForSeconds(Random.Range(_flickerMinInterval, _flickerMaxInterval));
        }
    }
}
