using UnityEngine;

public enum FireMode { FullAuto, SemiAuto, Burst }

/// <summary>
/// All data for one weapon type: stats, fire behaviour, FX, and feel.
/// No per-instance Transform refs live here — those stay on GunController.
///
/// Adding a new weapon:
///   1. Create this SO  →  fill in every section below
///   2. Create / reuse AmmunitionSO + MagazineSO with a matching CaliberSO
///   3. Duplicate a gun prefab  →  assign this SO to GunController.weaponData
///   4. Drag the prefab into WeaponManager._initialWeapons
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Deadend District/Weapon")]
public class WeaponSO : ItemSO
{
    // ── Identity ───────────────────────────────────────────────────────────

    [Header("=== Identity ===")]
    [Tooltip("Must reference the same CaliberSO as the magazines/ammo for this weapon.")]
    public CaliberSO caliber;

    // ── Fire mode ──────────────────────────────────────────────────────────

    [Header("=== Fire Mode ===")]
    public FireMode fireMode = FireMode.FullAuto;
    [Tooltip("Shots per burst (Burst mode only).")]
    public int burstCount = 3;
    [Tooltip("Delay between individual shots inside a burst (seconds).")]
    public float burstShotInterval = 0.08f;

    // ── Stats ──────────────────────────────────────────────────────────────

    [Header("=== Stats ===")]
    [Tooltip("Rounds per minute.")]
    public float fireRate = 600f;
    [Tooltip("Damage used when no magazine / ammo SO provides a value.")]
    public float baseDamage = 25f;
    public float range = 100f;
    public LayerMask hitLayers = ~0;
    [Tooltip("Weapon weight (1 = neutral). Scales move speed, jump, and bob frequency via sqrt curve — heavier guns move and bob slower.")]
    [Range(0.1f, 10f)]
    public float weight = 1f;

    // ── Reload ─────────────────────────────────────────────────────────────

    [Header("=== Reload ===")]
    [Tooltip("Total reload animation length in seconds.")]
    public float reloadTime = 1.8f;
    [Tooltip("Seconds after reload starts when the old mag physically drops (fires OnMagEjected).")]
    public float reloadMagEjectTime = 0.3f;
    [Tooltip("Seconds after reload starts when the new mag clicks in (fires OnMagInserted).")]
    public float reloadMagInsertTime = 1.2f;
    [Tooltip("Default magazine type — used for auto-fill in debug/testing without an inventory.")]
    public MagazineSO defaultMagazineType;

    // ── ADS ────────────────────────────────────────────────────────────────

    [Header("=== ADS ===")]
    public float adsInTime = 0.15f;
    public float adsOutTime = 0.12f;
    [Tooltip("Fire rate multiplier while ADS (< 1 = slower).")]
    public float adsFirerateMultiplier = 0.75f;

    // ── Bolt animation ─────────────────────────────────────────────────────

    [Header("=== Bolt Animation ===")]
    public float boltTravelDistance = 0.03f;
    public float boltBackTime = 0.04f;
    public float boltForwardTime = 0.10f;

    // ── Trigger animation ──────────────────────────────────────────────────

    [Header("=== Trigger Animation ===")]
    public float triggerRotationAngle = 15f;
    public float triggerPullTime = 0.03f;
    public float triggerReleaseTime = 0.08f;

    // ── Casing ejection ────────────────────────────────────────────────────

    [Header("=== Casing Ejection ===")]
    [Tooltip("Must have a Rigidbody.")]
    public GameObject casingPrefab;
    public float casingEjectForce = 3f;
    public float casingEjectSpread = 1.5f;
    public float casingTorque = 8f;
    public float casingLifetime = 4f;

    // ── FX ─────────────────────────────────────────────────────────────────

    [Header("=== FX ===")]
    public ParticleSystem muzzleFlashPrefab;
    public AudioClip gunshotClip;

    // ── Default ammo ───────────────────────────────────────────────────────

    [Header("=== Default Ammo ===")]
    [Tooltip("Ammo type for auto-fill testing; also the fallback when no magazine is loaded.")]
    public AmmunitionSO defaultAmmo;

    // ── Feel (GunSway tuning — per weapon, not per prefab) ─────────────────

    [Header("=== Feel / Sway ===")]
    public WeaponFeelData feel = new WeaponFeelData();

    // ── Recoil ─────────────────────────────────────────────────────────────

    [Header("=== Recoil ===")]
    public WeaponRecoilData recoil = new WeaponRecoilData();
}

/// <summary>
/// All GunSway tuning values for this weapon type.
/// Stored inside WeaponSO so every weapon can have its own feel
/// without manually re-tuning 20+ fields per prefab.
/// </summary>
[System.Serializable]
public class WeaponFeelData
{
    [Header("Mouse Sway")]
    public float swayAmount = 0.04f;
    public float swaySmooth = 8f;
    public float swayMaxDelta = 0.06f;

    [Header("Mouse Tilt (Roll)")]
    public float tiltAmount = 4f;
    public float tiltSmooth = 8f;
    [Tooltip("Tilt multiplier while ADS (lower = less roll when aiming).")]
    public float adsTiltMultiplier = 0.2f;

    [Header("Lean Gun Tilt")]
    [Tooltip("Extra Z-roll added to the gun on top of camera lean.")]
    public float leanGunTiltAmount = 5f;

    [Header("Idle Breathing")]
    public float breatheAmplitudeY = 0.0015f;
    public float breatheAmplitudeX = 0.0008f;
    public float breatheFrequency = 0.8f;
    [Range(0f, 1f)]
    [Tooltip("How much idle breathing persists while ADS. 0 = none, 1 = full hip amount.")]
    public float adsBreathScale = 0.3f;

    [Header("Walk Bob")]
    public float walkBobSpeedThreshold = 0.5f;
    public float walkBobFrequency = 2.2f;
    public float walkBobAmplitudeY = 0.006f;
    public float walkBobAmplitudeX = 0.003f;

    [Header("Sprint Bob")]
    public float sprintBobFrequency = 3.2f;
    public float sprintBobAmplitudeY = 0.012f;
    public float sprintBobAmplitudeX = 0.006f;
    public float sprintTiltZ = 5f;
    public float sprintTiltSmooth = 6f;

    [Header("Airborne")]
    public float airborneRiseAmount = 0.04f;
    public float airborneRiseSmooth = 0.15f;
    public float airborneReturnSmooth = 0.08f;

    [Header("Landing Slam")]
    public float landSlamAmount = 0.025f;
    public float landRecoverSmooth = 0.06f;
    public float landVelocityThresh = -3f;

    [Header("Footstep Nudge")]
    public float stepNudgeAmount = 0.003f;
    public float stepNudgeSmooth = 10f;

    [Header("General")]
    [Range(0f, 2f)]
    public float masterIntensity = 1f;
    public float returnSmooth = 12f;
}

[System.Serializable]
public class WeaponRecoilData
{
    [Header("Hip Kick")]
    public float kickUp    = 2f;
    public float kickHoriz = 0.5f;
    public float kickRoll  = 0.3f;

    [Header("ADS Kick")]
    public float adsKickUp    = 0.8f;
    public float adsKickHoriz = 0.15f;
    public float adsKickRoll  = 0.1f;

    [Header("Spring")]
    [Tooltip("How fast targetRecoil decays back to zero between shots.")]
    public float targetDecaySpeed   = 10f;
    [Tooltip("How fast currentRecoil chases targetRecoil — higher = snappier kick.")]
    public float currentFollowSpeed = 20f;

    [Header("Clamp")]
    [Tooltip("Max accumulated vertical (pitch-up) recoil in degrees.")]
    public float maxVertical = 15f;
    [Tooltip("Max accumulated horizontal (yaw) recoil in degrees, both sides.")]
    public float maxHoriz    = 5f;
    [Tooltip("Max accumulated roll recoil in degrees, both sides.")]
    public float maxRoll     = 3f;
}
