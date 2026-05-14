using UnityEngine;

/// <summary>
/// Extension methods for UnityEngine.GameObject shared across the inventory system.
/// </summary>
public static class GameObjectExtensions
{
    /// <summary>
    /// Recursively sets every GameObject in the hierarchy (including <paramref name="go"/>)
    /// to the specified <paramref name="layer"/>.
    /// Silently no-ops for invalid layer indices.
    /// </summary>
    public static void SetLayerRecursive(this GameObject go, int layer)
    {
        if (layer < 0 || layer > 31) return;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }
}
