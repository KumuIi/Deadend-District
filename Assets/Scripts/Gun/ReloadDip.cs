using System.Collections;
using UnityEngine;

/// <summary>
/// Dips the gun pivot off-screen during a player-initiated reload (R key),
/// plays the weapon's reload audio clips in order once the dip crosses the
/// configured threshold, then smoothly returns the pivot to rest when the
/// reload finishes.
///
/// Runs after GunController.LateUpdate (order 10000) so the additive dip offset
/// is always applied on top of the fully composed pivot position.
///
/// Does NOT fire for inventory drag-drop swaps or Start() auto-loads.
/// </summary>
[DefaultExecutionOrder(10010)]
[RequireComponent(typeof(GunController))]
public sealed class ReloadDip : MonoBehaviour
{
    [SerializeField] private GunController _gun;
    [SerializeField] private AudioSource   _audio;

    public GunController Gun => _gun;
    public float   CurrentDipOffset          { get; private set; }
    /// <summary>World-Y dip plus Perlin XZ jitter — written to FlashlightSway.DipPositionOffset.</summary>
    public Vector3 FlashlightPositionOffset  { get; private set; }
    /// <summary>Downward pitch plus noisy yaw/roll — written to FlashlightSway.DipRotationOffset.</summary>
    public Vector3 FlashlightRotationOffset  { get; private set; }

    private float     _dipTarget;
    private float     _dipVelocity;
    private bool      _audioStarted;
    private Coroutine _audioCoroutine;

    private void OnEnable()
    {
        _gun.OnReloadStarted  += HandleReloadStarted;
        _gun.OnReloadFinished += HandleReloadFinished;
    }

    private void OnDisable()
    {
        _gun.OnReloadStarted  -= HandleReloadStarted;
        _gun.OnReloadFinished -= HandleReloadFinished;
        StopAudio();
        CurrentDipOffset         = 0f;
        FlashlightPositionOffset = Vector3.zero;
        FlashlightRotationOffset = Vector3.zero;
        _dipTarget               = 0f;
        _dipVelocity             = 0f;
    }

    private void HandleReloadStarted(GunController _)
    {
        _dipTarget    = _gun.weaponData != null ? _gun.weaponData.reloadDipDepth : -0.8f;
        _audioStarted = false;
        StopAudio();
    }

    private void HandleReloadFinished(GunController _)
    {
        _dipTarget = 0f;
    }

    private void LateUpdate()
    {
        if (_gun.weaponData == null) return;

        var data = _gun.weaponData;
        float smoothTime = _dipTarget < 0f ? data.reloadDipDownTime : data.reloadDipReturnTime;

        CurrentDipOffset = Mathf.SmoothDamp(
            CurrentDipOffset, _dipTarget, ref _dipVelocity, smoothTime);

        if (!_audioStarted && CurrentDipOffset <= data.reloadDipAudioThreshold
            && data.reloadClips != null && data.reloadClips.Length > 0 && _audio != null)
        {
            _audioCoroutine = StartCoroutine(PlayClipsSequentially(data.reloadClips));
            _audioStarted   = true;
        }

        if (_gun.gunPivot != null)
            _gun.gunPivot.localPosition += Vector3.up * CurrentDipOffset;

        // Flashlight dip outputs — 0 at rest, full effect when fully dipped.
        float dipProgress = data.reloadDipDepth != 0f
            ? Mathf.Clamp01(CurrentDipOffset / data.reloadDipDepth)
            : 0f;

        float t = Time.time;
        float jx = (Mathf.PerlinNoise(t * 2.3f, 0f)        * 2f - 1f) * data.reloadFlashlightJitterPos * dipProgress;
        float jz = (Mathf.PerlinNoise(0f, t * 1.9f + 1.7f) * 2f - 1f) * data.reloadFlashlightJitterPos * dipProgress;
        FlashlightPositionOffset = new Vector3(jx, CurrentDipOffset, jz);

        float pitch = Mathf.Lerp(0f, data.reloadFlashlightPitchDown, dipProgress);
        float ry    = (Mathf.PerlinNoise(t * 1.5f + 3.0f, 0f)        * 2f - 1f) * data.reloadFlashlightJitterRot * dipProgress;
        float rz    = (Mathf.PerlinNoise(0f, t * 1.7f + 5.3f)        * 2f - 1f) * data.reloadFlashlightJitterRot * dipProgress;
        FlashlightRotationOffset = new Vector3(pitch, ry, rz);
    }

    private IEnumerator PlayClipsSequentially(AudioClip[] clips)
    {
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            _audio.PlayOneShot(clip);
            yield return new UnityEngine.WaitForSeconds(clip.length);
        }
        _audioCoroutine = null;
    }

    private void StopAudio()
    {
        if (_audioCoroutine != null)
        {
            StopCoroutine(_audioCoroutine);
            _audioCoroutine = null;
        }
    }
}
