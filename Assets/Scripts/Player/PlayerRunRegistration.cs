using UnityEngine;

/// <summary>
/// Sits on the player rig alongside PlayerHealth.
/// Registers PlayerHealth with RunManager when the player becomes active,
/// unregisters on disable. Keeps RunManager free of serialized cross-scene refs
/// and avoids FindObjectOfType.
///
/// Implementors: one instance on the Player root GameObject.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class PlayerRunRegistration : MonoBehaviour
{
    private PlayerHealth _health;

    private void Awake() => _health = GetComponent<PlayerHealth>();

    private void OnEnable()
    {
        if (RunManager.Instance != null)
            RunManager.Instance.RegisterPlayer(_health);
    }

    private void Start()
    {
        // Fallback: if OnEnable fired before RunManager.Awake set Instance, register now.
        if (RunManager.Instance != null)
            RunManager.Instance.RegisterPlayer(_health);
    }

    private void OnDisable()
    {
        if (RunManager.Instance != null)
            RunManager.Instance.UnregisterPlayer(_health);
    }
}
