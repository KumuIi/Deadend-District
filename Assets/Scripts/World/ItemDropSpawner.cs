using UnityEngine;

/// <summary>
/// Spawns physics-enabled world items. Two entry points share one builder:
///   • <see cref="TryDrop"/> — throws an item from an origin (camera/muzzle) with force + spin.
///       Used by InventoryUI context-menu drops and enemy weapon drops.
///   • <see cref="Place"/>   — places an item at an exact world position with no throw.
///       Used by LootSpawnSystem to populate sector loot.
///
/// Both return failure (false / null) on bad input — callers must NOT remove the source
/// item from inventory on failure.
/// </summary>
public static class ItemDropSpawner
{
    private const float SpawnReach       = 1.5f;
    private const float SpawnMinDist     = 0.35f;
    private const float SphereCastRadius = 0.15f;

    /// <summary>
    /// Throws an item from <paramref name="origin"/> along <paramref name="throwDirection"/>.
    /// SphereCasts forward to find a safe spawn point so the item never clips through walls.
    /// </summary>
    public static bool TryDrop(ItemInstance item, Transform origin,
                                Vector3 throwDirection,
                                float throwForce        = 5f,
                                float spinForce         = 3f,
                                int   interactableLayer = 6,
                                int   obstacleMask      = -5) // -5 = Physics.DefaultRaycastLayers (all except IgnoreRaycast)
    {
        if (item == null || item.data == null || origin == null)
        {
            Debug.LogWarning("[ItemDropSpawner] Cannot drop: null item or origin.");
            return false;
        }

        var spawnPos = FindSafeSpawnPoint(origin, throwDirection, obstacleMask);

        var go = BuildWorldItem(item, spawnPos, interactableLayer);
        if (go == null) return false;

        // Random tumble rotation AFTER the collider is sized (BuildWorldItem leaves root at identity).
        go.transform.rotation = Random.rotation;

        var rb = go.AddComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.AddForce(throwDirection.normalized * throwForce, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * spinForce, ForceMode.Impulse);

        return true;
    }

    /// <summary>
    /// Places an item at an exact world position/rotation with no throw force — for static
    /// loot placement. The Rigidbody lets it settle onto surfaces. Returns the spawned
    /// GameObject (so the caller can move it into a sector scene for cleanup) or null on failure.
    /// </summary>
    public static GameObject Place(ItemInstance item, Vector3 position, Quaternion rotation,
                                    int interactableLayer = 6)
    {
        if (item == null || item.data == null)
        {
            Debug.LogWarning("[ItemDropSpawner] Cannot place: null item.");
            return null;
        }

        var go = BuildWorldItem(item, position, interactableLayer);
        if (go == null) return null;

        go.transform.rotation = rotation;

        var rb = go.AddComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return go;
    }

    // ── Shared builder ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds the world-item GameObject (model + bounds collider + LootItemWorld) at
    /// <paramref name="position"/> with identity rotation. Does NOT add a Rigidbody —
    /// callers add physics after applying their own rotation, so the bounds math runs in
    /// the root-local (identity) frame where world == local.
    /// </summary>
    private static GameObject BuildWorldItem(ItemInstance item, Vector3 position, int interactableLayer)
    {
        var go = new GameObject($"Dropped_{item.data.itemName}");
        go.layer = interactableLayer; // must match PlayerInteractor's interaction mask
        go.transform.position = position;

        // Spawn the visual model as a child (neutral rotation so bounds math is clean)
        if (item.data.modelPrefab != null)
        {
            var model = Object.Instantiate(item.data.modelPrefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Re-center the model on the root origin so a baked-in mesh pivot offset doesn't hang
            // the item off the spawn point (and through the floor). go is at identity rotation/scale
            // here. Shared with the inventory preview — see ModelPrefabUtil. No-op when centered.
            ModelPrefabUtil.CenterOn(go.transform, model.transform);
        }

        // Build collider while root is still at identity rotation — bounds are root-local here
        AddBoundsCollider(go, interactableLayer);

        // LootItemWorld last — Initialize() before Start() runs is fine since Start() uses FindObjectOfType
        var loot = go.AddComponent<LootItemWorld>();
        loot.Initialize(item);

        return go;
    }

    private static Vector3 FindSafeSpawnPoint(Transform origin, Vector3 direction, int obstacleMask)
    {
        var   ray  = new Ray(origin.position, direction);
        float dist = SpawnReach;

        if (Physics.SphereCast(ray, SphereCastRadius, out var hit, SpawnReach,
                               obstacleMask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(SpawnMinDist, hit.distance - SphereCastRadius);

        return origin.position + direction.normalized * dist;
    }

    private static void AddBoundsCollider(GameObject root, int layer)
    {
        if (ModelPrefabUtil.TryGetCombinedRendererBounds(root, out var b))
        {
            // root has identity rotation and default scale (1,1,1) so world == local
            var box    = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size   = b.size;
            box.gameObject.layer = layer;
            return;
        }

        // Fallback for items with no model prefab: small unit box
        var fallback = root.AddComponent<BoxCollider>();
        fallback.size = Vector3.one * 0.15f;
        fallback.gameObject.layer = layer;
    }
}
