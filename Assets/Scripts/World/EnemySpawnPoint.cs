using UnityEngine;

/// <summary>
/// A placeable enemy spawn marker. Drop these in a sector (as children of the
/// <see cref="EnemySpawnSystem"/> object) and drag in the enemy prefab and — for guards —
/// the patrol route the spawned guard should walk. The system decides when/how many to spawn.
///
/// One prefab can serve many points with different scene routes: the route lives on the
/// spawn point, not baked into the prefab. Monsters (Mimic) ignore the route.
///
/// A standalone point is "for itself": by default it spawns every run. Turn on
/// <see cref="_limitedSpawn"/> to make it spawn every run only UNTIL its enemy is killed,
/// after which it stops (forever, or for <see cref="_runsUntilRespawn"/> runs). Kill state
/// is persisted via <see cref="SpawnPersistence"/> so it survives save/extract. Put points
/// under an <see cref="EnemySpawnGroup"/> instead to spawn only a random subset per run.
/// </summary>
public class EnemySpawnPoint : MonoBehaviour
{
    [Tooltip("Enemy prefab to spawn at this point (guard or monster).")]
    [SerializeField] private GameObject _enemyPrefab;

    [Tooltip("Patrol route assigned to the spawned guard (EnemyBrain). Leave empty for " +
             "monsters or stationary guards.")]
    [SerializeField] private PatrolRoute _patrolRoute;

    [Tooltip("Spawn facing this point's rotation. Off keeps the prefab's own orientation.")]
    [SerializeField] private bool _useSpawnFacing = true;

    [Header("Spawn Chance")]
    [Tooltip("Percent chance [0..100] this point spawns on a run it's eligible. 100 = always.")]
    [Range(0f, 100f)]
    [SerializeField] private float _spawnChancePercent = 100f;

    [Header("Limited Spawn")]
    [Tooltip("ON: spawns every run UNTIL this enemy is killed, then stops (saved). " +
             "OFF: spawns every run, forever.")]
    [SerializeField] private bool _limitedSpawn = false;

    [Tooltip("Runs the enemy stays gone after being killed before it respawns.\n" +
             "0 = never respawn (gone for good).\n" +
             "1 = skips the next run, returns the run after.\n" +
             "2 = skips two runs, etc.")]
    [Min(0)]
    [SerializeField] private int _runsUntilRespawn = 0;

    [Tooltip("Stable id for save persistence — auto-generated. If you DUPLICATE a configured " +
             "limited point, right-click the component → 'New Spawn Id' so the copy tracks its " +
             "own kill state instead of sharing the original's.")]
    [SerializeField] private string _spawnId;

    public bool   HasPrefab    => _enemyPrefab != null;
    public bool   LimitedSpawn => _limitedSpawn;

    /// <summary>Eligible this run? Non-limited points are always eligible; limited points
    /// consult their persisted kill state.</summary>
    public bool IsAvailableThisRun() =>
        !_limitedSpawn || SpawnPersistence.IsAvailableThisRun(_spawnId, _runsUntilRespawn);

    /// <summary>Per-spawn random gate. 100% always passes.</summary>
    public bool RollSpawnChance() =>
        _spawnChancePercent >= 100f || Random.value * 100f < _spawnChancePercent;

    /// <summary>Records the kill so a limited point stops spawning (or pauses for N runs).</summary>
    public void MarkConsumed()
    {
        if (_limitedSpawn) SpawnPersistence.MarkConsumed(_spawnId);
    }

    /// <summary>
    /// Instantiates the enemy and (for guards) assigns the patrol route before the brain's
    /// Start() runs, so patrolling begins on the right route. Returns null if no prefab set.
    /// Eligibility/chance are decided by the caller (EnemySpawnSystem / EnemySpawnGroup).
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

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(_spawnId))
        {
            _spawnId = System.Guid.NewGuid().ToString("N");
            return;
        }
#if UNITY_EDITOR
        // Ctrl+D copies _spawnId — only the new copy's OnValidate fires, so it regenerates and
        // the original keeps its id. Without this, killing one would suppress every copy.
        foreach (var other in FindObjectsByType<EnemySpawnPoint>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (other != this && other._spawnId == _spawnId)
            {
                _spawnId = System.Guid.NewGuid().ToString("N");
                break;
            }
#endif
    }

    [ContextMenu("New Spawn Id")]
    private void RegenerateSpawnId() => _spawnId = System.Guid.NewGuid().ToString("N");

    private void OnDrawGizmos()
    {
        Gizmos.color = !HasPrefab ? Color.gray
                     : _limitedSpawn ? new Color(0.6f, 0.3f, 1f)   // purple = limited
                                     : new Color(1f, 0.4f, 0.1f);  // orange = always
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1f);
        if (_patrolRoute != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _patrolRoute.transform.position);
        }
    }
}
