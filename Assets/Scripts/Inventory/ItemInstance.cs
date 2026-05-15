using UnityEngine;

/// <summary>
/// Runtime placement state of one inventory item.
/// The ItemSO is the immutable template; this class holds the mutable position + rotation.
///
/// Save contract: store (data.name, gridPosition.x, gridPosition.y, isRotated).
/// Reconstruct by resolving the SO via IItemSOResolver, then restore fields.
/// </summary>
public class ItemInstance
{
    /// <summary>
    /// Unique identity for this specific runtime item.
    /// Two guns of the same type have the same ItemSO but different InstanceIds.
    /// </summary>
    public readonly System.Guid InstanceId = System.Guid.NewGuid();

    /// <summary>The immutable ScriptableObject definition for this item.</summary>
    public readonly ItemSO data;

    /// <summary>Top-left anchor cell in the grid (origin = top-left = (0,0)).</summary>
    public Vector2Int gridPosition;

    /// <summary>True when the item is rotated 90° clockwise.</summary>
    public bool isRotated;

    public ItemInstance(ItemSO definition)
    {
        data = definition;
    }

    // ── Shape API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the world-space cell offsets for the current rotation.
    /// Rotating 90° clockwise: (x,y) → (y, -x), then normalised so min x/y = 0.
    /// </summary>
    public Vector2Int[] GetCurrentOffsets()
    {
        var source = data.GetOffsets();
        if (!isRotated) return source;

        var rotated = new Vector2Int[source.Length];
        int minX = int.MaxValue, minY = int.MaxValue;

        for (int i = 0; i < source.Length; i++)
        {
            // 90° clockwise: (x,y) → (y, -x)
            rotated[i] = new Vector2Int(source[i].y, -source[i].x);
            if (rotated[i].x < minX) minX = rotated[i].x;
            if (rotated[i].y < minY) minY = rotated[i].y;
        }

        // Normalise so top-left = (0,0)
        for (int i = 0; i < rotated.Length; i++)
            rotated[i] -= new Vector2Int(minX, minY);

        return rotated;
    }

    /// <summary>
    /// Axis-aligned bounding size of the item in its current rotation.
    /// Used for UI sizing and grid clamping.
    /// </summary>
    public Vector2Int CurrentSize
    {
        get
        {
            var offsets = GetCurrentOffsets();
            int maxX = 0, maxY = 0;
            foreach (var o in offsets)
            {
                if (o.x > maxX) maxX = o.x;
                if (o.y > maxY) maxY = o.y;
            }
            // +1 because offsets are 0-based (cell at (2,0) means 3 wide)
            return new Vector2Int(maxX + 1, maxY + 1);
        }
    }
}
