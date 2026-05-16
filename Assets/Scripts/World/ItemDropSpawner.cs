using UnityEngine;

/// <summary>
/// Spawns a physics-enabled world item from an inventory ItemInstance.
/// Called by InventoryUI when the player drops an item via the context menu.
///
/// Usage: ItemDropSpawner.TryDrop(item, cameraTransform, throwForce, spinForce, interactableLayer, obstacleMask)
/// Returns false if spawn failed — caller should NOT remove from inventory on false.
/// </summary>
public static class ItemDropSpawner
{
    private const float SpawnReach       = 1.5f;
    private const float SpawnMinDist     = 0.35f;
    private const float SphereCastRadius = 0.15f;

    public static bool TryDrop(ItemInstance item, Transform origin,
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

        var spawnPos = FindSafeSpawnPoint(origin, obstacleMask);

        var go = new GameObject($"Dropped_{item.data.itemName}");
        go.layer = interactableLayer; // must match PlayerInteractor's interaction mask
        go.transform.position = spawnPos;

        // Spawn the visual model as a child (neutral rotation so bounds math is clean)
        if (item.data.modelPrefab != null)
        {
            var model = Object.Instantiate(item.data.modelPrefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }

        // Build collider BEFORE applying random rotation — bounds are root-local at this point
        AddBoundsCollider(go, interactableLayer);

        // Apply random rotation after collider sizing so bounds math is correct
        go.transform.rotation = Random.rotation;

        // Physics — add after collider so no CCD warnings
        var rb = go.AddComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // LootItemWorld last — Initialize() before Start() runs is fine since Start() uses FindObjectOfType
        var loot = go.AddComponent<LootItemWorld>();
        loot.Initialize(item);

        // Throw forward and add tumble spin
        rb.AddForce(origin.forward * throwForce, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * spinForce, ForceMode.Impulse);

        return true;
    }

    private static Vector3 FindSafeSpawnPoint(Transform origin, int obstacleMask)
    {
        var   ray  = new Ray(origin.position, origin.forward);
        float dist = SpawnReach;

        if (Physics.SphereCast(ray, SphereCastRadius, out var hit, SpawnReach,
                               obstacleMask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(SpawnMinDist, hit.distance - SphereCastRadius);

        return origin.position + origin.forward * dist;
    }

    private static void AddBoundsCollider(GameObject root, int layer)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            // Collect bounds in root local space (root is identity rotation here)
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

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
