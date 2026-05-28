using UnityEngine;

/// <summary>
/// Place one in the Hub and one in each sector.
/// Teleports the player to this position on scene start.
/// Hub spawn points (tick Is Hub Spawn) also fire when the player
/// returns from a run so they land back at the hub entrance.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour, IRunLifecycleListener
{
    [SerializeField] private string _playerTag  = "Player";
    [SerializeField] private bool   _isHubSpawn = false;

    private void Start()          => TeleportPlayer();
    private void OnEnable()       => RunManager.Instance?.RegisterListener(this);
    private void OnDisable()      => RunManager.Instance?.UnregisterListener(this);

    public void OnRunStarted()    { }
    public void OnRunExtracted()  { }
    public void OnRunDied()       { }

    public void OnReturnedToHub()
    {
        if (_isHubSpawn) TeleportPlayer();
    }

    private void TeleportPlayer()
    {
        var player = GameObject.FindWithTag(_playerTag);
        if (player == null)
        {
            Debug.LogWarning($"[PlayerSpawnPoint] No GameObject tagged '{_playerTag}' found.");
            return;
        }

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(transform.position, transform.rotation);
        if (cc != null) cc.enabled = true;
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
