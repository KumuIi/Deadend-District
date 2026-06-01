using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns and manages a sector's enemies. One instance per sector scene — put the
/// <see cref="EnemySpawnPoint"/> objects (and any <see cref="EnemySpawnGroup"/> "grand
/// spawners") as children so collection is scene-scoped and we avoid FindObjectOfType
/// (see RULEBOOK and the sibling LootSpawnSystem).
///
/// • OnRunStarted: spawns standalone points that are eligible + pass their chance roll, then
///   asks each group for its random subset — all up to <see cref="_maxEnemiesPerSector"/>.
/// • Death:        persists the kill via the point (limited points then stop spawning).
/// • OnReturnedToHub: despawns every living enemy (OnDespawned for poolables, else Destroy).
///
/// There is no in-run respawn: kill an enemy and it stays dead for the run. "Coming back"
/// is governed per-point by Limited Spawn + Runs Until Respawn (run-to-run, persisted).
///
/// Mirrors LootSpawnSystem's additive-load catch-up: a sector loaded into an already-active
/// run missed the OnRunStarted broadcast, so Start() spawns if State is already InRun.
/// </summary>
public class EnemySpawnSystem : MonoBehaviour, IRunLifecycleListener
{
    [Tooltip("Hard cap on living enemies spawned by this system in this sector.")]
    [SerializeField] private int _maxEnemiesPerSector = 5;

    private readonly List<EnemySpawnPoint> _standalonePoints = new List<EnemySpawnPoint>(); // not under a group
    private readonly List<EnemySpawnGroup> _groups           = new List<EnemySpawnGroup>();
    private readonly List<EnemySpawnPoint> _groupPicks       = new List<EnemySpawnPoint>(); // scratch reused per group
    private readonly List<Live> _live = new List<Live>();                 // alive — drives the cap
    private readonly List<GameObject> _spawned = new List<GameObject>();  // every instance, for cleanup (incl. corpses)
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

        _groups.Clear();
        GetComponentsInChildren(true, _groups);

        // Every point claimed by any group (drag-in array, else its children). Array members can
        // live outside the group's hierarchy, so we ask the group rather than walk parents.
        var owned = new HashSet<EnemySpawnPoint>();
        var ownedBuf = new List<EnemySpawnPoint>();
        foreach (var g in _groups)
        {
            if (g == null) continue;
            ownedBuf.Clear();
            g.GetOwnedPoints(ownedBuf);
            foreach (var p in ownedBuf)
                if (p != null) owned.Add(p);
        }

        // Standalone = every point under this system NOT claimed by a group.
        var all = new List<EnemySpawnPoint>();
        GetComponentsInChildren(true, all);
        _standalonePoints.Clear();
        foreach (var p in all)
            if (p != null && !owned.Contains(p))
                _standalonePoints.Add(p);
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnInitial()
    {
        if (_spawnedThisRun) return;
        _spawnedThisRun = true;

        CollectPoints();

        // Standalone points — each decides for itself (eligibility + chance).
        foreach (var point in _standalonePoints)
        {
            if (_live.Count >= _maxEnemiesPerSector) break;
            if (point == null || !point.HasPrefab) continue;
            if (!point.IsAvailableThisRun()) continue;
            if (!point.RollSpawnChance()) continue;
            SpawnAt(point);
        }

        // Grand spawners — each contributes a random subset of its members.
        foreach (var group in _groups)
        {
            if (group == null) continue;
            group.SelectPointsForRun(_groupPicks);
            foreach (var point in _groupPicks)
            {
                if (_live.Count >= _maxEnemiesPerSector) break;
                SpawnAt(point);
            }
        }

        Debug.Log($"[EnemySpawnSystem] Spawned {_live.Count}/{_maxEnemiesPerSector} enemies " +
                  $"across {_standalonePoints.Count} point(s) + {_groups.Count} group(s) in " +
                  $"'{gameObject.scene.name}'.");
    }

    /// <summary>Raw spawn — caller has already decided this point is eligible and rolled chance.</summary>
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
        // prefab nests it. Without it, death never frees the cap slot or persists the kill.
        entry.Health = go.GetComponentInChildren<EnemyHealth>();
        if (entry.Health != null)
            entry.Health.OnDeath += () => OnEnemyDied(entry);
        else
            Debug.LogWarning($"[EnemySpawnSystem] '{go.name}' has no EnemyHealth — it won't " +
                             "free its slot on death or persist a limited-spawn kill.");

        if (go.TryGetComponent(out IPoolableSpawnedEntity poolable))
            poolable.OnSpawned();

        _live.Add(entry);
    }

    private void OnEnemyDied(Live entry)
    {
        _live.Remove(entry);
        // The corpse stays in _spawned so a same-scene rerun's DespawnAll cleans it up.

        // Persist the kill: a limited-spawn point now stops spawning (or pauses for N runs).
        entry.Point?.MarkConsumed();
    }

    // ── Despawn ────────────────────────────────────────────────────────────────

    private void DespawnAll()
    {
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
