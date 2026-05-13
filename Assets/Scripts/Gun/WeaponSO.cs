using UnityEngine;

public enum FireMode { FullAuto, SemiAuto, Burst }

/// <summary>
/// Stateless data asset for a single weapon type.
/// Extends ItemSO so weapons are first-class inventory items with a grid footprint.
///
/// All numeric stats, FX prefabs, and fire behaviour live here.
/// Per-instance Transform refs (grip points, bones, sockets) stay on GunController.
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Deadend District/Weapon")]
public class WeaponSO : ItemSO
{
    [Header("=== Identity ===")]
    [Tooltip("Caliber tag — must match MagazineSO.caliber for mag compatibility")]
    public string caliber = "9x19";

    [Header("=== Fire Mode ===")]
    public FireMode fireMode          = FireMode.FullAuto;
    [Tooltip("Shots per burst (Burst mode only)")]
    public int      burstCount        = 3;
    [Tooltip("Delay between individual shots inside a burst (seconds)")]
    public float    burstShotInterval = 0.08f;

    [Header("=== Stats ===")]
    [Tooltip("Rounds per minute")]
    public float    fireRate   = 600f;
    [Tooltip("Damage used when no magazine / ammo SO provides a value")]
    public float    baseDamage = 25f;
    public float    range      = 100f;
    public LayerMask hitLayers = ~0;

    [Header("=== Reload ===")]
    public float reloadTime = 1.8f;
    [Tooltip("Default magazine type — used for auto-fill in debug/testing without inventory")]
    public MagazineSO defaultMagazineType;

    [Header("=== ADS ===")]
    public float adsInTime             = 0.15f;
    public float adsOutTime            = 0.12f;
    [Tooltip("Fire rate multiplier while ADS (< 1 = slower)")]
    public float adsFirerateMultiplier = 0.75f;

    [Header("=== Bolt Animation ===")]
    public float boltTravelDistance = 0.03f;
    public float boltBackTime       = 0.04f;
    public float boltForwardTime    = 0.10f;

    [Header("=== Trigger Animation ===")]
    public float triggerRotationAngle = 15f;
    public float triggerPullTime      = 0.03f;
    public float triggerReleaseTime   = 0.08f;

    [Header("=== Casing Ejection ===")]
    [Tooltip("Must have a Rigidbody")]
    public GameObject casingPrefab;
    public float casingEjectForce  = 3f;
    public float casingEjectSpread = 1.5f;
    public float casingTorque      = 8f;
    public float casingLifetime    = 4f;

    [Header("=== FX ===")]
    public ParticleSystem muzzleFlashPrefab;
    public AudioClip      gunshotClip;

    [Header("=== Default Ammo ===")]
    [Tooltip("Ammo type for auto-fill testing; also the fallback when no magazine is loaded")]
    public AmmunitionSO defaultAmmo;
}
