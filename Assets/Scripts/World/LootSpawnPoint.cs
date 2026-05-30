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

        ItemSO toSpawn = ResolveItem();
        if (toSpawn == null) return null;

        var instance = ItemInstanceFactory.Create(toSpawn);
        if (instance == null) return null;

        return ItemDropSpawner.Place(instance, transform.position, transform.rotation, interactableLayer);
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
