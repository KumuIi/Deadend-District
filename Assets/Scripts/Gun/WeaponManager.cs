using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds all player-level references once and manages equip/unequip lifecycle.
/// Instantiates gun prefabs lazily (first equip), caches them, and toggles
/// SetActive instead of destroying so state (ammo, bolt pos) is preserved.
///
/// Weapon input / switching lives in WeaponSwitcher — this script is purely
/// state + lifecycle.
/// </summary>
public class WeaponManager : MonoBehaviour
{
    [Header("=== Player References ===")]
    [Tooltip("The camera transform — passed to every gun on equip")]
    public Transform playerCam;
    [Tooltip("CameraController on the camera — used for lean data")]
    public CameraController cameraController;
    [Tooltip("PlayerMotor on the player root — used for movement-driven sway")]
    public PlayerMotor playerMotor;

    [Header("=== Weapon Socket ===")]
    [Tooltip("Transform under which gun instances are spawned and parented")]
    public Transform weaponSocket;

    // ── Public properties read by Weapon.Initialize ───────────────────────
    public Transform        PlayerCam          => playerCam;
    public CameraController CameraController   => cameraController;
    public PlayerMotor      PlayerMotor        => playerMotor;

    public Weapon CurrentWeapon { get; private set; }

    // Prefab → live instance cache
    private readonly Dictionary<Weapon, Weapon> _instances = new();

    // ── API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Equip the weapon that corresponds to <paramref name="prefab"/>.
    /// First call instantiates and initialises; subsequent calls just toggle active.
    /// </summary>
    public void Equip(Weapon prefab)
    {
        if (prefab == null) return;

        if (CurrentWeapon != null)
            CurrentWeapon.gameObject.SetActive(false);

        if (!_instances.TryGetValue(prefab, out Weapon instance))
        {
            instance = Instantiate(prefab, weaponSocket);
            instance.gameObject.SetActive(false); // keep disabled until initialized
            instance.Initialize(this);
            _instances[prefab] = instance;
        }

        instance.gameObject.SetActive(true);
        CurrentWeapon = instance;
    }

    /// <summary>Holster the current weapon without equipping another.</summary>
    public void Holster()
    {
        if (CurrentWeapon == null) return;
        CurrentWeapon.gameObject.SetActive(false);
        CurrentWeapon = null;
    }
}
