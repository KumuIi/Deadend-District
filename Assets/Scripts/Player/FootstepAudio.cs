using UnityEngine;

/// <summary>
/// Plays footstep, jump, and landing sounds on the player.
///
/// Surface detection:
///   A short downward raycast reads the ground collider's tag and picks the
///   matching clip array from _surfaces[]. Falls back to _defaultClips when
///   no tag matches or the raycast misses.
///
/// Step timing:
///   Mirrors GunSway's bob timer using the same formula and the same
///   WeaponWeightMultiplier, so audio steps are always in sync with the
///   visual gun bob — even as weapon weight changes.
///
/// Landing classification:
///   Normal  — airborne for less than _hardLandAirTime seconds.
///   Hard    — airborne for _hardLandAirTime seconds or more.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class FootstepAudio : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("=== References ===")]
    [SerializeField] private PlayerMotor _playerMotor;
    [SerializeField] private WeaponManager _weaponManager;

    [Header("=== Surface Sounds ===")]
    [Tooltip("Each entry maps a Unity tag to footstep audio clips for that surface type.")]
    [SerializeField] private SurfaceAudio[] _surfaces;
    [Tooltip("Fallback clips used when no tag matches or the ground raycast misses.")]
    [SerializeField] private AudioClip[] _defaultClips;

    [Header("=== Action Sounds ===")]
    [SerializeField] private AudioClip[] _jumpClips;
    [SerializeField] private AudioClip[] _landNormalClips;
    [SerializeField] private AudioClip[] _landHardClips;

    [Header("=== Settings ===")]
    [Tooltip("Minimum seconds airborne before any landing sound plays (filters out tiny steps/ledges).")]
    [SerializeField] private float _minLandAirTime  = 0.3f;
    [Tooltip("Seconds airborne before landing is classified as a hard landing.")]
    [SerializeField] private float _hardLandAirTime = 2f;
    [Tooltip("Downward raycast length for surface tag detection.")]
    [SerializeField] private float _groundRayDistance = 1.3f;
    [SerializeField] private LayerMask _groundMask;
    [SerializeField, Range(0f, 1f)] private float _stepVolume  = 0.6f;
    [SerializeField, Range(0f, 1f)] private float _jumpVolume  = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _landVolume  = 0.9f;

    // ── Private state ──────────────────────────────────────────────────────

    private AudioSource _audio;
    private float  _bobTimer;
    private int    _lastStep;
    private bool   _wasGrounded;
    private float  _airTime;


    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _audio = GetComponent<AudioSource>();
    }

    private void Start()
    {
        _wasGrounded = _playerMotor && _playerMotor.IsGrounded;
    }

    private void OnEnable()
    {
        if (_playerMotor != null)
            _playerMotor.OnJumped += HandleJump;
    }

    private void OnDisable()
    {
        if (_playerMotor != null)
            _playerMotor.OnJumped -= HandleJump;
    }

    private void Update()
    {
        if (_playerMotor == null) return;

        bool  grounded   = _playerMotor.IsGrounded;
        bool  sprinting  = _playerMotor.IsSprinting;
        float horizSpeed = _playerMotor.HorizontalVelocity.magnitude;
        float weightMult = _playerMotor.WeaponWeightMultiplier;

        // ── Pull bob frequencies from the equipped weapon ───────────────
        WeaponFeelData feel    = _weaponManager?.CurrentWeapon?.weaponData?.feel;
        float walkThresh  = feel != null ? feel.walkBobSpeedThreshold : 0.5f;
        float walkFreq    = feel != null ? feel.walkBobFrequency      : 2.2f;
        float sprintFreq  = feel != null ? feel.sprintBobFrequency    : 3.2f;

        bool isWalking   = horizSpeed >= walkThresh && grounded && !sprinting;
        bool isSprinting = sprinting && grounded;
        bool shouldStep  = isWalking || isSprinting;

        // ── Advance bob timer (mirrors GunSway exactly) ─────────────────
        float bobFreq = (isSprinting ? sprintFreq : walkFreq) * weightMult;
        if (shouldStep)
            _bobTimer += Time.deltaTime * bobFreq * Mathf.PI * 2f;
        else
            _bobTimer = Mathf.MoveTowards(
                _bobTimer,
                Mathf.Round(_bobTimer / Mathf.PI) * Mathf.PI,
                Time.deltaTime * 8f);

        // ── Air time accumulation ───────────────────────────────────────
        if (!grounded)
            _airTime += Time.deltaTime;

        // ── Landing detection ───────────────────────────────────────────
        bool justLanded = grounded && !_wasGrounded;
        if (justLanded)
        {
            if (_airTime >= _minLandAirTime)
            {
                AudioClip[] clips = _airTime >= _hardLandAirTime ? _landHardClips : _landNormalClips;
                PlayRandom(clips, _landVolume);
                PlayRandom(GetSurfaceClips(), _stepVolume);
            }
            _airTime = 0f;
        }

        // ── Step (skipped on landing frame to avoid doubling) ───────────
        int step = Mathf.FloorToInt(_bobTimer / Mathf.PI);
        if (!justLanded && shouldStep && step != _lastStep)
        {
            _lastStep = step;
            PlayRandom(GetSurfaceClips(), _stepVolume);

            float stepRadius = sprinting ? 8f : 4f;
            StimulusSystem.Instance?.Broadcast(new Stimulus(
                StimulusType.Sound,
                transform.position,
                radius:    stepRadius,
                intensity: sprinting ? 0.6f : 0.3f,
                source:    gameObject,
                instigator: gameObject));
        }
        else if (justLanded)
            _lastStep = step;

        _wasGrounded = grounded;
    }

    // ── Handlers ───────────────────────────────────────────────────────────

    private void HandleJump()
    {
        PlayRandom(_jumpClips, _jumpVolume);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private AudioClip[] GetSurfaceClips()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundRayDistance, _groundMask))
        {
            string hitTag = hit.collider.tag;
            foreach (SurfaceAudio surface in _surfaces)
                if (surface.tag == hitTag && surface.clips is { Length: > 0 })
                    return surface.clips;
        }
        return _defaultClips;
    }

    private void PlayRandom(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0 || _audio == null) return;
        _audio.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
    }
}

/// <summary>Maps a Unity tag to an array of footstep clips for that surface.</summary>
[System.Serializable]
public struct SurfaceAudio
{
    [Tooltip("The Unity tag assigned to the ground collider (e.g. \"Concrete\", \"Metal\", \"Grass\").")]
    public string tag;
    [Tooltip("One of these clips is picked at random on each footstep.")]
    public AudioClip[] clips;
}
