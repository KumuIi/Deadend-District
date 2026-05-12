using UnityEngine;

/// <summary>
/// GunController v4 — both bolt AND trigger use SmoothDamp for smooth cycling.
/// </summary>
[DefaultExecutionOrder(10000)]
public class GunController : MonoBehaviour
{
    [Header("=== Bone References ===")]
    [Tooltip("Drag the 'top' bone (slider/bolt) from the Hierarchy")]
    public Transform topBone;

    [Tooltip("Drag the 'trigger' bone from the Hierarchy")]
    public Transform triggerBone;

    [Header("=== Muzzle ===")]
    [Tooltip("Empty child GameObject at the barrel tip")]
    public Transform muzzlePoint;
    public ParticleSystem muzzleFlashPrefab;
    public AudioClip gunshotClip;

    [Header("=== Gun Stats ===")]
    public float fireRate  = 600f;
    public float damage    = 25f;
    public float range     = 100f;
    public LayerMask hitLayers = ~0;

    [Header("=== Bolt Animation ===")]
    [Tooltip("Max travel distance (metres). Tweak live in Play mode.")]
    public float boltTravelDistance = 0.03f;
    [Tooltip("Seconds to travel fully back from rest")]
    public float boltBackTime    = 0.04f;
    [Tooltip("Seconds to return fully forward to rest")]
    public float boltForwardTime = 0.10f;

    [Header("=== Trigger Animation ===")]
    [Tooltip("Max degrees the trigger rotates when pulled")]
    public float triggerRotationAngle = 15f;
    [Tooltip("Seconds to pull trigger fully")]
    public float triggerPullTime    = 0.03f;
    [Tooltip("Seconds to release trigger fully")]
    public float triggerReleaseTime = 0.08f;

    [Header("=== Debug ===")]
    [Tooltip("Hold to freeze bolt open — verify axis + distance")]
    public KeyCode holdOpenKey = KeyCode.H;

    // ── private state ──────────────────────────────────────────────────────────
    private float          _nextFireTime;
    private AudioSource    _audio;
    private ParticleSystem _muzzleFlash;

    // Bolt (SmoothDamp)
    private float _boltCurrent;
    private float _boltTarget;
    private float _boltVelocity;

    // Trigger (SmoothDamp) — works on angle directly, same pattern as bolt
    private float _triggerCurrent;   // current angle applied
    private float _triggerTarget;    // triggerRotationAngle on fire, 0 at rest
    private float _triggerVelocity;

    // Rest poses
    private Vector3    _boltRestPos;
    private Quaternion _triggerRestRot;

    // ── lifecycle ──────────────────────────────────────────────────────────────

    void Start()
    {
        if (topBone)     _boltRestPos    = topBone.localPosition;
        if (triggerBone) _triggerRestRot = triggerBone.localRotation;

        _audio = GetComponent<AudioSource>();
        if (!_audio)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake  = false;
            _audio.spatialBlend = 1f;
        }

        if (muzzleFlashPrefab && muzzlePoint)
        {
            _muzzleFlash = Instantiate(muzzleFlashPrefab,
                                       muzzlePoint.position,
                                       muzzlePoint.rotation,
                                       muzzlePoint);
            _muzzleFlash.Stop();
        }
    }

    void Update()
    {
        // ── Hold-open debug ───────────────────────────────────────────────────
        if (Input.GetKey(holdOpenKey))
        {
            _boltTarget   = -boltTravelDistance;
            _boltCurrent  = -boltTravelDistance;
            _boltVelocity = 0f;
            return;
        }

        // ── Fire input ────────────────────────────────────────────────────────
        if (Input.GetButton("Fire1") && Time.time >= _nextFireTime)
        {
            Fire();
            _nextFireTime = Time.time + 60f / fireRate;
        }

        // ── Bolt: SmoothDamp toward target ────────────────────────────────────
        float boltSmoothTime = (_boltTarget < -0.0001f) ? boltBackTime : boltForwardTime;
        _boltCurrent = Mathf.SmoothDamp(_boltCurrent, _boltTarget, ref _boltVelocity, boltSmoothTime);

        // Reached back → flip to return
        if (_boltTarget < 0f && Mathf.Abs(_boltCurrent - _boltTarget) < 0.0001f)
        {
            _boltTarget   = 0f;
            _boltVelocity = 0f;
        }
        // Snap to rest
        if (_boltTarget >= 0f && Mathf.Abs(_boltCurrent) < 0.00005f)
        {
            _boltCurrent  = 0f;
            _boltVelocity = 0f;
        }

        // ── Trigger: SmoothDamp toward target ─────────────────────────────────
        float triggerSmoothTime = (_triggerTarget > 0.01f) ? triggerPullTime : triggerReleaseTime;
        _triggerCurrent = Mathf.SmoothDamp(_triggerCurrent, _triggerTarget, ref _triggerVelocity, triggerSmoothTime);

        // Reached pulled position → flip to release
        if (_triggerTarget > 0f && Mathf.Abs(_triggerCurrent - _triggerTarget) < 0.01f)
        {
            _triggerTarget   = 0f;
            _triggerVelocity = 0f;
        }
        // Snap to rest
        if (_triggerTarget <= 0f && Mathf.Abs(_triggerCurrent) < 0.001f)
        {
            _triggerCurrent  = 0f;
            _triggerVelocity = 0f;
        }
    }

    void LateUpdate()
    {
        // Bolt: move on local -X axis (change axis here if needed)
        if (topBone)
            topBone.localPosition = _boltRestPos + new Vector3(-_boltCurrent, 0f, 0f);

        // Trigger: rotate on local X axis
        if (triggerBone)
            triggerBone.localRotation = _triggerRestRot
                                        * Quaternion.Euler(_triggerCurrent, 0f, 0f);
    }

    // ── Fire ──────────────────────────────────────────────────────────────────

    void Fire()
    {
        Vector3 origin    = muzzlePoint ? muzzlePoint.position : transform.position;
        Vector3 direction = muzzlePoint ? muzzlePoint.forward  : transform.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitLayers))
        {
            Debug.DrawLine(origin, hit.point, Color.red, 0.3f);

            // ── Replace with your damage system ──────────────────────────────
            // Health health = hit.collider.GetComponentInParent<Health>();
            // if (health != null) health.TakeDamage(damage);
            Debug.Log($"Hit: {hit.collider.name}");
        }

        if (_muzzleFlash) _muzzleFlash.Play();
        if (gunshotClip)  _audio.PlayOneShot(gunshotClip);

        // Bolt: kick back from current position
        _boltTarget   = -boltTravelDistance;
        _boltVelocity = -boltTravelDistance / boltBackTime;

        // Trigger: kick to pulled angle from current position
        _triggerTarget   = triggerRotationAngle;
        _triggerVelocity = triggerRotationAngle / triggerPullTime;
    }
}
