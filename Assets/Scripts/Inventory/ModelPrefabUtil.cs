using UnityEngine;

/// <summary>
/// Shared helpers for instantiating <see cref="ItemSO.modelPrefab"/> consistently across the
/// game. Centralizes the fix for Blender/FBX exports that bake a pivot offset into the MESH
/// itself (the geometry sits far from the mesh-local origin). Left uncorrected, such a model:
///   • spawns off the spawn point in the world (and drops through the floor), and
///   • renders off-frame / scaled to nothing in the inventory preview (invisible icons).
///
/// Both call sites (ItemDropSpawner, InventoryItemView) recenter through here so there is one
/// source of truth and the same model behaves identically everywhere.
/// </summary>
public static class ModelPrefabUtil
{
    /// <summary>World-space AABB enclosing every Renderer under <paramref name="root"/>.
    /// False when the model has no renderers (caller falls back to a default).</summary>
    public static bool TryGetCombinedRendererBounds(GameObject root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { bounds = default; return false; }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    /// <summary>
    /// Shifts <paramref name="model"/> so its render-bounds CENTER sits on
    /// <paramref name="anchor"/>'s origin, cancelling a baked-in mesh pivot offset.
    /// <paramref name="anchor"/> must be at identity rotation+scale when called so the measured
    /// world offset maps cleanly onto the model's position. No-op for already-centered models.
    /// </summary>
    public static void CenterOn(Transform anchor, Transform model)
    {
        if (TryGetCombinedRendererBounds(anchor.gameObject, out var b))
            model.position -= b.center - anchor.position;
    }

    /// <summary>
    /// Instantiates <paramref name="modelPrefab"/> wrapped in a root whose ORIGIN is the model's
    /// render-bounds center. Rotate / scale / position the returned transform freely — the visible
    /// mesh stays centered on the origin. Returns null when <paramref name="modelPrefab"/> is null.
    /// Use this anywhere a model is positioned by its transform (e.g. the inventory preview).
    /// </summary>
    public static GameObject InstantiateCentered(GameObject modelPrefab)
    {
        if (modelPrefab == null) return null;

        var root  = new GameObject(modelPrefab.name);
        var model = Object.Instantiate(modelPrefab, root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        // root is fresh (origin, identity, scale 1), so CenterOn maps the world offset directly.
        CenterOn(root.transform, model.transform);
        return root;
    }
}
