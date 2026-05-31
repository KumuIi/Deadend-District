using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns and manages a sector's enemies. One instance per sector scene — put the
/// <see cref="EnemySpawnPoint"/> objects as children so collection is scene-scoped and we
/// avoid FindObjectOfType (see RULEBOOK and the sibling LootSpawnSystem).
///
/// • OnRunStarted: spawns one enemy per point, up to <see cref="_maxEnemiesPerSector"/>.
/// • Death:        if that point has a respawn delay and we're under the cap, respawns it.
/// • OnReturnedToHub: despawns every living enemy (OnDespawned for poolables, else Destroy).
///
/// Mirrors LootSpawnSystem's additive-load catch-up: a sector loaded into an already-active
/// run missed the OnRunStarted broadcast, so Start() spawns if State is already InRun.
/// </summary>
public class EnemySpawnSystem : MonoBehaviour, IRunLifecycleListener
{
    [Tooltip("Hard cap on living enemies spawned by this system in this sector.")]
    [SerializeField] private int _maxEnemiesPerSector = 5;

    private readonly List<EnemySpawnPoint> _points = new List<EnemySpawnPoint>();
    private readonly List<Live> _live = new List<Live>();          // alive — drives cap + respawn
    private readonly List<GameObject> _spawned = new List<GameObject>(); // every instance, for cleanup (incl. corpses)
    private bool _collected;
    private bool _spawnedThisRun;

    private class Live
    {
        public GameObject      Go;
        public EnemySpawnPoint Point;
        public EnemyHealth     Health;
    }

    private void OnEnable()  => RunManager.Instance?.RegisterListener(this);
    private void OnDisable() => RunManager.Instance?.UnregisterListener(this);

    private void Start()
    {
        CollectPoints();

        // Catch-up: an additive sector loaded into an active run missed OnRunStarted.
        if (!_spawnedThisRun && RunManager.Instance != null &&
            RunManager.Instance.State == RunManager.RunState.InRun)
            SpawnInitial();
    }

    private void CollectPoints()
    {
        if (_collected) return;
        _collected = true;
        _points.Clear();
        GetComponentsInChildren(true, _points);
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnInitial()
    {
        if (_spawnedThisRun) return;
        _spawnedThisRun = true;

        CollectPoints();

        foreach (var point in _points)
        {
            if (_live.Count >= _maxEnemiesPerSector) break;
            SpawnAt(point);
        }

        Debug.Log($"[EnemySpawnSystem] Spawned {_live.Count}/{_maxEnemiesPerSector} enemies " +
                  $"across {_points.Count} point(s) in '{gameObject.scene.name}'.");
    }

    private void SpawnAt(EnemySpawnPoint point)
    {
        if (point == null || !point.HasPrefab) return;
        if (_live.Count >= _maxEnemiesPerSector) return;

        GameObject go = point.Spawn();
        if (go == null) return;

        // Move into the sector scene so unloading the sector destroys the enemy.
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        _spawned.Add(go);

        var entry = new Live { Go = go, Point = point };
        // Search children too — guards keep EnemyHealth on the root, but this is robust if a
        // prefab nests it. Without it, death never decrements the cap or triggers respawn.
        entry.Health = go.GetComponentInChildren<EnemyHealth>();
        if (entry.Health != null)
            entry.Health.OnDeath += () => OnEnemyDied(entry);
        else
            Debug.LogWarning($"[EnemySpawnSystem] '{go.name}' has no EnemyHealth — it won't " +
                             "free its slot on death or respawn.");

        if (go.TryGetComponent(out IPoolableSpawnedEntity poolable))
            poolable.OnSpawned();

        _live.Add(entry);
    }

    private void OnEnemyDied(Live entry)
    {
        _live.Remove(entry);
        // The corpse stays in _spawned so a same-scene rerun's DespawnAll cleans it up.

        // Respawn only if this point opts in, we're still in a run, and we're under the cap.
        if (entry.Point != null && entry.Point.RespawnDelay > 0f)
            StartCoroutine(RespawnAfter(entry.Point, entry.Point.RespawnDelay));
    }

    private IEnumerator RespawnAfter(EnemySpawnPoint point, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (RunManager.Instance != null && RunManager.Instance.State == RunManager.RunState.InRun
            && _live.Count < _maxEnemiesPerSector)
            SpawnAt(point);
    }

    // ── Despawn ────────────────────────────────────────────────────────────────

    private void DespawnAll()
    {
        StopAllCoroutines(); // cancel pending respawns

        // Iterate every instance (alive AND corpses) so same-scene reruns don't stack bodies.
        foreach (var go in _spawned)
        {
            if (go == null) continue;
            if (go.TryGetComponent(out IPoolableSpawnedEntity poolable))
                poolable.OnDespawned(); // poolables tear themselves down
            else
                Destroy(go);
        }
        _spawned.Clear();
        _live.Clear();
    }

    // ── IRunLifecycleListener ──────────────────────────────────────────────────

    public void OnRunStarted()
    {
        // Fresh run — clear any leftovers and respawn from scratch (handles same-scene re-runs).
        DespawnAll();
        _spawnedThisRun = false;
        SpawnInitial();
    }

    public void OnRunExtracted() { }
    public void OnRunDied()      { }

    public void OnReturnedToHub()
    {
        DespawnAll();
        _spawnedThisRun = false;
    }
}
