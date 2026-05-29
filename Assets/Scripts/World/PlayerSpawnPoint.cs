using UnityEngine;

/// <summary>
/// Marker that defines where the player should appear in a scene.
/// Place one in the Hub (tick Is Hub Spawn) and one in each sector.
///
/// Teleportation is driven by SceneTransitionManager after each transition
/// completes — not by Start() — so it always fires after the fade and after
/// old scene geometry is fully unloaded.
///
/// Start() still teleports on the very first scene load (no transition running),
/// so the player lands at the correct position when entering Play Mode directly.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    [SerializeField] private string _playerTag  = "Player";
    [SerializeField] private bool   _isHubSpawn = false;

    public bool IsHubSpawn => _isHubSpawn;

    private void Start()
    {
        // Only self-teleport on first scene boot — SceneTransitionManager owns it during transitions
        if (SceneTransitionManager.Instance == null || !SceneTransitionManager.Instance.IsTransitioning)
            Teleport();
    }

    public void Teleport()
    {
        var player = GameObject.FindWithTag(_playerTag);

        if (player == null)
        {
            // FindWithTag skips inactive objects — search by component as fallback
            foreach (PlayerHealth ph in Resources.FindObjectsOfTypeAll(typeof(PlayerHealth)))
            {
                if (ph.hideFlags != HideFlags.None) continue; // skip prefab assets
                player = ph.gameObject;
                break;
            }
        }

        if (player == null)
        {
            Debug.LogWarning($"[PlayerSpawnPoint] No GameObject tagged '{_playerTag}' found.");
            return;
        }

        // PlayerMotor uses a kinematic Rigidbody — must go through its Teleport method
        // to update rb.position and zero velocity. Setting transform.position directly
        // doesn't update rb.position and gets corrected back on the next FixedUpdate.
        var motor = player.GetComponent<PlayerMotor>();
        if (motor != null)
        {
            motor.Teleport(transform.position, transform.rotation);
        }
        else
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(transform.position, transform.rotation);
            if (cc != null) cc.enabled = true;
        }

        Debug.Log($"[PlayerSpawnPoint] Teleported player to '{name}' at {transform.position}.");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = _isHubSpawn ? new Color(0f, 1f, 0.4f, 0.8f) : new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawSphere(transform.position, 0.25f);
        Gizmos.DrawRay(transform.position, transform.forward * 0.6f);
    }
#endif
}
