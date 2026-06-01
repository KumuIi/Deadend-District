using UnityEngine;

/// <summary>
/// Marks a spot in a sector where loot can appear. A passive marker: it holds the spawn
/// config and exposes <see cref="TrySpawn"/>, but LootSpawnSystem owns the run-lifecycle
/// wiring and drives every point in its scene. Keeping the lifecycle in one place avoids
/// dozens of points each registering with RunManager (and each hitting the additive-load
/// timing trap — see LootSpawnSystem).
///
/// Two modes:
///   • Soft spawn (default): rolls <see cref="_spawnChance"/>; on success pulls a weighted
///     item from <see cref="_poolSO"/>.
///   • Hard spawn (<see cref="_isHardSpawn"/>): always places <see cref="_fixedItem"/> —
///     for quest items or guaranteed landmark loot.
///
/// <see cref="HasSpawned"/> is a per-run runtime guard (never serialized). Sectors unload
/// between runs so it resets naturally; LootSpawnSystem calls <see cref="ResetSpawn"/> for
/// same-scene re-runs (StartRunInPlace).
/// </summary>
public class LootSpawnPoint : MonoBehaviour
{
    [Header("Soft Spawn (weighted pool)")]
    [Tooltip("Weighted item pool rolled when this is NOT a hard spawn.")]
    [SerializeField] private LootPoolSO _poolSO;

    [Tooltip("Probability [0..1] that anything spawns here this run.")]
    [Range(0f, 1f)]
    [SerializeField] private float _spawnChance = 0.5f;

    [Header("Hard Spawn (guaranteed)")]
    [Tooltip("When true, always spawns FixedItem and ignores the pool/chance.")]
    [SerializeField] private bool _isHardSpawn;

    [Tooltip("Item placed every run when Is Hard Spawn is true.")]
    [SerializeField] private ItemSO _fixedItem;

    [Header("Limited Spawn")]
    [Tooltip("ON: spawns every run UNTIL the player loots it, then stops (saved — you can't " +
             "reload and grab it again once you've saved). OFF: rolls fresh every run.")]
    [SerializeField] private bool _limitedSpawn = false;

    [Tooltip("Runs the loot stays gone after being looted before it returns.\n" +
             "0 = never returns (gone for good).\n" +
             "1 = skips the next run, returns the run after.\n" +
             "2 = skips two runs, etc.")]
    [Min(0)]
    [SerializeField] private int _runsUntilRespawn = 0;

    [Header("Persist Until Looted")]
    [Tooltip("ON (soft spawns only): once an item rolls here it STAYS the same item every run " +
             "until the player loots it, then it re-rolls next run. A failed % roll is never " +
             "saved, so the point keeps rolling until it populates. OFF: re-rolls fresh every " +
             "run. Hard spawns ignore this (their item is fixed anyway).")]
    [SerializeField] private bool _persistUntilLooted = true;

    [Tooltip("Stable id for save persistence — auto-generated. If you DUPLICATE a configured " +
             "limited point, right-click the component → 'New Spawn Id' so the copy tracks its " +
             "own looted state.")]
    [SerializeField] private string _spawnId;

    /// <summary>True once this point has resolved its spawn this run (success or empty roll).</summary>
    public bool HasSpawned { get; private set; }

    public void ResetSpawn() => HasSpawned = false;

    /// <summary>
    /// Resolves which item (if any) to spawn this run and places it at this point's transform.
    /// Returns the spawned GameObject, or null if nothing spawned (failed roll / empty config /
    /// already spawned this run).
    /// </summary>
    public GameObject TrySpawn(int interactableLayer)
    {
        if (HasSpawned) return null;
        HasSpawned = true;

        // Limited loot still inside its post-pickup suppression window stays gone entirely.
        if (_limitedSpawn && !SpawnPersistence.IsAvailableThisRun(_spawnId, _runsUntilRespawn))
            return null;

        ItemSO toSpawn = ResolvePersistentItem();
        if (toSpawn == null) return null;   // empty roll → persist nothing, re-roll next run

        var instance = ItemInstanceFactory.Create(toSpawn);
        if (instance == null) return null;

        var go = ItemDropSpawner.Place(instance, transform.position, transform.rotation, interactableLayer);
        if (go == null) return null;

        // Remember which item is sitting here so it re-creates identically next run (soft spawns).
        if (PersistsItem) WorldStateManager.Instance?.SetString(LootItemKey, toSpawn.name);

        // On pickup: clear the stored item (re-roll next run) and/or mark a limited point consumed.
        if (PersistsItem || _limitedSpawn)
        {
            if (go.TryGetComponent(out LootItemWorld loot))
            {
                if (loot.OnPickup == null) loot.OnPickup = new UnityEngine.Events.UnityEvent();
                loot.OnPickup.AddListener(OnLooted);
            }
        }
        return go;
    }

    /// <summary>True when this point should remember its rolled item between runs (soft spawns only).</summary>
    private bool PersistsItem => _persistUntilLooted && !_isHardSpawn;

    private string LootItemKey => $"loot.{_spawnId}.item";

    /// <summary>
    /// Resolves the item to spawn, preferring a previously-persisted choice so uncollected loot
    /// stays the same item run-to-run. Falls back to a fresh roll when nothing is persisted, or
    /// when a persisted name no longer resolves (asset renamed/moved — we don't lock the point).
    /// </summary>
    private ItemSO ResolvePersistentItem()
    {
        if (!PersistsItem) return ResolveItem();

        var wsm = WorldStateManager.Instance;
        if (wsm != null)
        {
            string saved = wsm.GetString(LootItemKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                var item = new ResourcesItemSOResolver().Resolve(saved);
                if (item != null) return item;

                Debug.LogWarning($"[LootSpawnPoint] '{name}' had persisted loot '{saved}' that no " +
                                 "longer resolves under Resources/Items — clearing and re-rolling.", this);
                wsm.SetString(LootItemKey, "");
            }
        }
        return ResolveItem();   // fresh roll; an empty result is never persisted (see TrySpawn)
    }

    private void OnLooted()
    {
        // Forget the item so this point re-rolls next run (subject to spawn %).
        if (PersistsItem) WorldStateManager.Instance?.SetString(LootItemKey, "");
        // Limited points additionally stop spawning (or pause for N runs).
        if (_limitedSpawn) SpawnPersistence.MarkConsumed(_spawnId);
    }

    private ItemSO ResolveItem()
    {
        if (_isHardSpawn)
        {
            if (_fixedItem == null)
                Debug.LogWarning($"[LootSpawnPoint] '{name}' is a hard spawn but has no Fixed Item.", this);
            return _fixedItem;
        }

        if (Random.value > _spawnChance) return null; // failed the roll — empty this run

        if (_poolSO == null)
        {
            Debug.LogWarning($"[LootSpawnPoint] '{name}' has no Loot Pool assigned.", this);
            return null;
        }
        return _poolSO.Roll();
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
        // the original keeps its id. Without this, looting one would suppress every copy.
        foreach (var other in FindObjectsByType<LootSpawnPoint>(
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

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = _isHardSpawn ? new Color(1f, 0.85f, 0.2f, 0.9f)  // gold = guaranteed
                                    : new Color(0.3f, 0.8f, 1f, 0.7f);  // blue = chance
        Gizmos.DrawWireSphere(transform.position, 0.25f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.5f);
    }
#endif
}
