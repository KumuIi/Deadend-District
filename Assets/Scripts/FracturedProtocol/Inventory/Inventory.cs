#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using FracturedProtocol.Combat.Instances;

namespace FracturedProtocol.Combat.Containers
{
    /// <summary>
    /// A 2-D grid that stores ItemInstances by occupying rectangular cell ranges.
    /// Plain C# — no MonoBehaviour, no ScriptableObject.
    /// Lives in Combat namespace to avoid a circular assembly dependency with any UI layer.
    /// </summary>
    [Serializable]
    public sealed class Inventory
    {
        private readonly Vector2Int _dimensions;

        [NonSerialized] private ItemInstance?[,] _cells = null!;
        public List<ItemInstance> Items { get; private set; } = new List<ItemInstance>();

        public Vector2Int Dimensions => _dimensions;

        public Inventory(Vector2Int dimensions)
        {
            _dimensions = dimensions;
            _cells = new ItemInstance?[dimensions.x, dimensions.y];
        }

        /// <summary>Rebuild the internal cell grid from the serialized Items list after deserialization.</summary>
        public void RebuildCells()
        {
            _cells = new ItemInstance?[_dimensions.x, _dimensions.y];
            foreach (ItemInstance item in Items)
                WriteCells(item, item.gridPos, item.rotated, item);
        }

        /// <summary>True when all cells the item would occupy at pos are in-bounds and vacant.</summary>
        public bool CanPlace(ItemInstance item, Vector2Int pos, bool rotated)
        {
            Vector2Int size = GetSize(item, rotated);
            for (int x = pos.x; x < pos.x + size.x; x++)
            {
                for (int y = pos.y; y < pos.y + size.y; y++)
                {
                    if (x < 0 || x >= _dimensions.x || y < 0 || y >= _dimensions.y) return false;
                    if (_cells[x, y] != null) return false;
                }
            }
            return true;
        }

        /// <summary>Place item at pos. Returns true on success.</summary>
        public bool TryPlace(ItemInstance item, Vector2Int pos, bool rotated)
        {
            if (!CanPlace(item, pos, rotated)) return false;
            item.gridPos = pos;
            item.rotated = rotated;
            WriteCells(item, pos, rotated, item);
            Items.Add(item);
            return true;
        }

        /// <summary>Remove item from the grid. Returns false if item is not present.</summary>
        public bool Remove(ItemInstance item)
        {
            if (!Items.Remove(item)) return false;
            WriteCells(item, item.gridPos, item.rotated, null);
            return true;
        }

        /// <summary>Returns the item occupying cell pos, or null if empty.</summary>
        public ItemInstance? GetItemAt(Vector2Int pos)
        {
            if (pos.x < 0 || pos.x >= _dimensions.x || pos.y < 0 || pos.y >= _dimensions.y) return null;
            return _cells[pos.x, pos.y];
        }

        /// <summary>Enumerate every cell coordinate occupied by item.</summary>
        public IEnumerable<Vector2Int> GetOccupiedCells(ItemInstance item)
        {
            Vector2Int size = GetSize(item, item.rotated);
            for (int x = item.gridPos.x; x < item.gridPos.x + size.x; x++)
                for (int y = item.gridPos.y; y < item.gridPos.y + size.y; y++)
                    yield return new Vector2Int(x, y);
        }

        private static Vector2Int GetSize(ItemInstance item, bool rotated)
        {
            Vector2Int s = item.def!.gridSize;
            return rotated ? new Vector2Int(s.y, s.x) : s;
        }

        private void WriteCells(ItemInstance item, Vector2Int pos, bool rotated, ItemInstance? value)
        {
            Vector2Int size = GetSize(item, rotated);
            for (int x = pos.x; x < pos.x + size.x; x++)
                for (int y = pos.y; y < pos.y + size.y; y++)
                    _cells[x, y] = value;
        }
    }
}
