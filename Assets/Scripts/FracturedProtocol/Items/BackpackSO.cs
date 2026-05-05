#nullable enable
using UnityEngine;

namespace FracturedProtocol.Combat.Items
{
    /// <summary>Defines a backpack container with a fixed grid size.</summary>
    [CreateAssetMenu(fileName = "New_Backpack", menuName = "Items/Backpack")]
    public sealed class BackpackSO : ItemSO
    {
        public Vector2Int gridDimensions = new Vector2Int(6, 4);
    }
}
