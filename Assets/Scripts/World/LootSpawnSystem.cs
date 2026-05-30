using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Populates a sector's loot when a run starts. One instance per sector scene — put the
/// LootSpawnPoint objects as children of this system's GameObject (a "LootSpawns" parent),
/// so collection is scene-scoped for free and we avoid FindObjectOfType (see RULEBOOK).
///
/// Spawned items are moved into this system's scene so unloading the sector destroys them.
///
/// Timing note (the additive-load trap): when a sector loads ADDITIVELY into an already-active
/// run, RunManager has already broadcast OnRunStarted before this Start() runs — so registering
/// as a listener here would miss the event. Start() catches up by spawning when State is already
/// InRun. For same-scene runs (StartRunInPlace) the system is already registered and OnRunStarted
/// drives it. <see cref="_spawnedThisRun"/> guards against double-spawning across the two paths.
/// </summary>
public class LootSpawnSystem : MonoBehaviour, IRunLifecycleListener
{
    [Tooltip("Physics layer for spawned pickups — must match PlayerInteractor's interaction mask.")]
    [SerializeField] private int _interactableLayer = 6;

    private readonly List<LootSpawnPoint> _points = new List<LootSpawnPoint>();
    private bool _collected;
    private bool _spawnedThisRun;

    private void OnEnable()  => RunManager.Instance?.RegisterListener(this);
    private void OnDisable() => RunManager.Instance?.UnregisterListener(this);

    private void Start()
    {
        CollectPoints();

        // Catch-up: an additive sector loaded into an active run missed the OnRunStarted broadcast.
        if (!_spawnedThisRun && RunManager.Instance != null &&
            RunManager.Instance.State == RunManager.RunState.InRun)
            SpawnAll();
    }

    private void CollectPoints()
    {
        if (_collected) return;
        _collected = true;

        _points.Clear();
        // Children of this object — same scene by construction, no cross-sector leakage.
        GetComponentsInChildren<LootSpawnPoint>(true, _points);
    }

    private void SpawnAll()
    {
        if (_spawnedThisRun) return;
        _spawnedThisRun = true;

        CollectPoints(); // harmless if Start already ran; collects if OnRunStarted beat Start

        int spawned = 0;
        foreach (var point in _points)
        {
            if (point == null) continue;
            var go = point.TrySpawn(_interactableLayer);
            if (go == null) continue;

            // Move into the sector scene so unloading the sector cleans the loot up.
            SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            spawned++;
        }

        Debug.Log($"[LootSpawnSystem] Spawned {spawned} item(s) across {_points.Count} point(s) in '{gameObject.scene.name}'.");
    }

    // ── IRunLifecycleListener ──────────────────────────────────────────────

    public void OnRunStarted()
    {
        // New run begins — clear per-run guards so a same-scene re-run respawns fresh loot.
        _spawnedThisRun = false;
        foreach (var p in _points)
            if (p != null) p.ResetSpawn();

        SpawnAll();
    }

    public void OnRunExtracted() { }
    public void OnRunDied()      { }
    public void OnReturnedToHub() { }
}
