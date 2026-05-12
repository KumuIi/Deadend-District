using UnityEngine;

/// <summary>
/// GunController — entry point for weapon initialization, plus ADS pivot,
/// firing, bolt, trigger, and casing ejection.
///
/// Gun-internal refs (gunPivot, bones, sockets, FX) are serialized in the prefab
/// Inspector once. Player-level refs (playerCam, motor, cameraController) are
/// injected by WeaponManager.Equip() before this object is enabled, so Start()
/// always has valid refs.
/// </summary>
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(GunSway))]
public class GunController : MonoBehaviour
{
    [Header("=== Gun Pivot ===")]
    [Tooltip("The GunPivot empty child — shared with GunSway")]
    public Transform gunPivot;

    [Header("=== Bone References ===")]
    [Tooltip("Drag the 'top' bone (slider/bolt) from the Hierarchy")]
    public Transform topBone;
    [Tooltip("Drag the 'trigger' bone from the Hierarchy")]
    public Transform triggerBone;

    [Header("=== Muzzle ===")]
    [Tooltip("Empty child at the barrel tip — raycast origin + FX spawn point")]
    public Transform muzzlePoint;
    public ParticleSystem muzzleFlashPrefab;
    public AudioClip gunshotClip;

    [Header("=== Casing Ejection ===")]
    [Tooltip("Drag your bullet casing prefab here — must have a Rigidbody")]
    public GameObject casingPrefab;
    [Tooltip("Empty child on the gun where casings eject from (right side of chamber)")]
    public Transform casingEjectPoint;
    [Tooltip("Base rightward force applied to every casing")]
    public float casingEjectForce = 3f;
    [Tooltip("Random spread added on top of base force")]
    public float casingEjectSpread = 1.5f;
    [Tooltip("Random spin torque on ejected casing")]
    public float casingTorque = 8f;
    [Tooltip("Seconds before the casing GameObject is destroyed")]
    public float casingLifetime = 4f;

    [Header("=== ADS (Aim Down Sights) ===")]
    [Tooltip("Empty child placed exactly at the iron sight / optic centre")]
    public Transform aimSocket;
    [Tooltip("Seconds to reach full ADS")]
    public float adsInTime = 0.15f;
    [Tooltip("Seconds to return from ADS")]
    public float adsOutTime = 0.12f;
    [Tooltip("Fire rate multiplier while aiming (slower = more controlled)")]
    public float adsFirerateMultiplier = 0.75f;

    [Header("=== Gun Stats ===")]
    public float fireRate = 600f;
    public float damage   = 25f;
    public float range    = 100f;
    public LayerMask hitLayers = ~0;

    [Header("=== Bolt Animation ===")]
    public float boltTravelDistance = 0.03f;
    public float boltBackTime       = 0.04f;
    public float boltForwardTime    = 0.10f;

    [Header("=== Trigger Animation ===")]
    public float triggerRotationAngle = 15f;
    public float triggerPullTime      = 0.03f;
    public float triggerReleaseTime   = 0.08f;

    [Header("=== Debug ===")]
    public KeyCode holdOpenKey = KeyCode.H;

    // ── Public state (read by GunSway) ────────────────────────────────────
    public bool  IsAiming   { get; private set; }
    /// 0 = hip, 1 = full ADS
    public float AdsWeight  { get; private set; }

    // ── Injected player refs (set by WeaponManager.Equip before Start) ─────
    private Transform _playerCam;

    private GunSway _sway;

    // ── Private state ─────────────────────────────────────────────────────
    private float        _nextFireTime;
    private AudioSource  _audio;
    private ParticleSystem _muzzleFlash;

    // Bolt
    private float _boltCurrent;
    private float _boltTarget;
    private float _boltVelocity;

    // Trigger
    private float _triggerCurrent;
    private float _triggerTarget;
    private float _triggerVelocity;

    // ADS
    private float   _adsWeight;
    private float   _adsVelocity;
    private Vector3 _hipPosition;
    private Vector3 _pivotVelocity;

    // Rest poses for bones
    private Vector3    _boltRestPos;
    private Quaternion _triggerRestRot;

    // ── Injection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by WeaponManager while the object is disabled.
    /// Injects all player-level refs into this script and GunSway.
    /// </summary>
    public void Initialize(WeaponManager mgr)
    {
        _playerCam = mgr.PlayerCam;
        _sway      = GetComponent<GunSway>();
        _sway.Initialize(gunPivot, mgr.PlayerMotor, mgr.PlayerCam, mgr.CameraController, this);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void Start()
    {
        if (topBone)    _boltRestPos    = topBone.localPosition;
        if (triggerBone) _triggerRestRot = triggerBone.localRotation;
        if (gunPivot)    _hipPosition    = gunPivot.localPosition;

        _audio = GetComponent<AudioSource>();

        if (muzzleFlashPrefab && muzzlePoint)
        {
            _muzzleFlash = Instantiate(muzzleFlashPrefab,
                muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);
            _muzzleFlash.Stop();
        }
    }

    void Update()
    {
        // ── Hold-open debug ───────────────────────────────────────────────
        if (Input.GetKey(holdOpenKey))
        {
            _boltTarget   = _boltCurrent = -boltTravelDistance;
            _boltVelocity = 0f;
            return;
        }

        // ── ADS input ─────────────────────────────────────────────────────
        IsAiming = Input.GetButton("Fire2");

        float adsTarget = IsAiming ? 1f : 0f;
        float adsSmooth = IsAiming ? adsInTime : adsOutTime;
        _adsWeight = Mathf.SmoothDamp(_adsWeight, adsTarget, ref _adsVelocity, adsSmooth);
        AdsWeight  = _adsWeight;

        // ── ADS pivot ─────────────────────────────────────────────────────
        // Runs in Update (not LateUpdate) so Animation Rigging IK constraints
        // evaluate after gunPivot has already moved.
        ApplyAdsPivot();

        // ── Fire input ────────────────────────────────────────────────────
        float currentFireRate = IsAiming ? fireRate * adsFirerateMultiplier : fireRate;
        if (Input.GetButton("Fire1") && Time.time >= _nextFireTime)
        {
            Fire();
            _nextFireTime = Time.time + 60f / currentFireRate;
        }

        // ── Bolt tick ─────────────────────────────────────────────────────
        float boltSmooth = (_boltTarget < -0.0001f) ? boltBackTime : boltForwardTime;
        _boltCurrent = Mathf.SmoothDamp(_boltCurrent, _boltTarget, ref _boltVelocity, boltSmooth);

        if (_boltTarget < 0f && Mathf.Abs(_boltCurrent - _boltTarget) < 0.0001f)
        { _boltTarget = 0f; _boltVelocity = 0f; }
        if (_boltTarget >= 0f && Mathf.Abs(_boltCurrent) < 0.00005f)
        { _boltCurrent = 0f; _boltVelocity = 0f; }

        // ── Trigger tick ──────────────────────────────────────────────────
        float trigSmooth = (_triggerTarget > 0.01f) ? triggerPullTime : triggerReleaseTime;
        _triggerCurrent = Mathf.SmoothDamp(_triggerCurrent, _triggerTarget, ref _triggerVelocity, trigSmooth);

        if (_triggerTarget > 0f && Mathf.Abs(_triggerCurrent - _triggerTarget) < 0.01f)
        { _triggerTarget = 0f; _triggerVelocity = 0f; }
        if (_triggerTarget <= 0f && Mathf.Abs(_triggerCurrent) < 0.001f)
        { _triggerCurrent = 0f; _triggerVelocity = 0f; }
    }

    void LateUpdate()
    {
        if (topBone)
            topBone.localPosition = _boltRestPos + new Vector3(-_boltCurrent, 0f, 0f);

        if (triggerBone)
            triggerBone.localRotation = _triggerRestRot
                * Quaternion.Euler(_triggerCurrent, 0f, 0f);
    }

    // ── ADS pivot ─────────────────────────────────────────────────────────

    void ApplyAdsPivot()
    {
        if (!gunPivot || !aimSocket || !_playerCam || _adsWeight <= 0.001f) return;

        Vector3 socketToGunPivot = gunPivot.position - aimSocket.position;
        Vector3 adsWorldPos      = _playerCam.position + socketToGunPivot;
        Vector3 adsLocalPos      = gunPivot.parent
            ? gunPivot.parent.InverseTransformPoint(adsWorldPos)
            : adsWorldPos;

        gunPivot.localPosition = Vector3.Lerp(
            gunPivot.localPosition, adsLocalPos, _adsWeight);
    }

    // ── Fire ──────────────────────────────────────────────────────────────

    void Fire()
    {
        Vector3 origin    = _playerCam ? _playerCam.position : (muzzlePoint ? muzzlePoint.position : transform.position);
        Vector3 direction = _playerCam ? _playerCam.forward  : (muzzlePoint ? muzzlePoint.forward  : transform.forward);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitLayers))
        {
            Debug.DrawLine(origin, hit.point, Color.red, 0.3f);
            Debug.Log($"Hit: {hit.collider.name}");
        }

        if (_muzzleFlash) _muzzleFlash.Play();
        if (gunshotClip)  _audio.PlayOneShot(gunshotClip);

        EjectCasing();

        _boltTarget   = -boltTravelDistance;
        _boltVelocity = -boltTravelDistance / boltBackTime;

        _triggerTarget   = triggerRotationAngle;
        _triggerVelocity = triggerRotationAngle / triggerPullTime;
    }

    void EjectCasing()
    {
        if (!casingPrefab || !casingEjectPoint) return;

        GameObject casing = Instantiate(casingPrefab,
            casingEjectPoint.position, casingEjectPoint.rotation);

        Rigidbody rb = casing.GetComponent<Rigidbody>();
        if (!rb)
        {
            Debug.LogWarning("GunController: casing prefab is missing a Rigidbody — add one to the prefab.", casingPrefab);
            Destroy(casing);
            return;
        }

        Vector3 ejectDir = (casingEjectPoint.right
            + casingEjectPoint.up      * 0.5f
            + casingEjectPoint.forward * -0.2f).normalized;

        Vector3 spread = new Vector3(
            Random.Range(-casingEjectSpread, casingEjectSpread) * 0.3f,
            Random.Range(0f,                 casingEjectSpread) * 0.5f,
            Random.Range(-casingEjectSpread, casingEjectSpread) * 0.2f);

        rb.linearVelocity  = (ejectDir * casingEjectForce + spread)
                           + (_playerCam ? _playerCam.forward * 0.5f : Vector3.zero);

        rb.angularVelocity = new Vector3(
            Random.Range(-casingTorque, casingTorque),
            Random.Range(-casingTorque, casingTorque),
            Random.Range(-casingTorque, casingTorque));

        Destroy(casing, casingLifetime);
    }
}
