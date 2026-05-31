using UnityEngine;

/// <summary>
/// A placeable enemy spawn marker. Drop these in a sector (as children of the
/// <see cref="EnemySpawnSystem"/> object) and drag in the enemy prefab and — for guards —
/// the patrol route the spawned guard should walk. The system decides when/how many to spawn.
///
/// One prefab can serve many points with different scene routes: the route lives on the
/// spawn point, not baked into the prefab. Monsters (Mimic) ignore the route.
/// </summary>
public class EnemySpawnPoint : MonoBehaviour
{
    [Tooltip("Enemy prefab to spawn at this point (guard or monster).")]
    [SerializeField] private GameObject _enemyPrefab;

    [Tooltip("Patrol route assigned to the spawned guard (EnemyBrain). Leave empty for " +
             "monsters or stationary guards.")]
    [SerializeField] private PatrolRoute _patrolRoute;

    [Tooltip("Seconds after this point's enemy dies before it respawns. 0 = never respawn.")]
    [SerializeField] private float _respawnDelay = 0f;

    [Tooltip("Spawn facing this point's rotation. Off keeps the prefab's own orientation.")]
    [SerializeField] private bool _useSpawnFacing = true;

    public float RespawnDelay => _respawnDelay;
    public bool  HasPrefab    => _enemyPrefab != null;

    /// <summary>
    /// Instantiates the enemy and (for guards) assigns the patrol route before the brain's
    /// Start() runs, so patrolling begins on the right route. Returns null if no prefab set.
    /// </summary>
    public GameObject Spawn()
    {
        if (_enemyPrefab == null)
        {
            Debug.LogWarning($"[EnemySpawnPoint] {name}: no enemy prefab assigned.");
            return null;
        }

        Quaternion rot = _useSpawnFacing ? transform.rotation : _enemyPrefab.transform.rotation;
        GameObject go  = Instantiate(_enemyPrefab, transform.position, rot);

        // Runtime patrol assignment — legitimate inspection of a freshly spawned instance,
        // not auto-wiring of a component's own serialized dependencies.
        if (_patrolRoute != null && go.TryGetComponent(out EnemyBrain brain))
            brain.AssignPatrolRoute(_patrolRoute);

        return go;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = HasPrefab ? new Color(1f, 0.4f, 0.1f) : Color.gray;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1f);
        if (_patrolRoute != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _patrolRoute.transform.position);
        }
    }
}
