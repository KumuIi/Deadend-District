using UnityEngine;

/// <summary>
/// Designer-authored definition of a hub trader: what it sells, at what prices, how much it
/// pays for the player's loot, and how often its stock refreshes. Pure data — TraderSystem
/// owns the runtime stock counts and transaction logic so the asset is never mutated at runtime.
/// </summary>
[CreateAssetMenu(menuName = "Economy/Trader", fileName = "NewTrader")]
public class TraderSO : ScriptableObject
{
    [System.Serializable]
    public struct StockEntry
    {
        [Tooltip("Item offered for sale.")]
        public ItemSO Item;

        [Tooltip("Credits the player pays to buy ONE of this item.")]
        [Min(0)] public int BuyPrice;

        [Tooltip("How many are available between restocks. 0 = unlimited stock.")]
        [Min(0)] public int StockCount;
    }

    [Header("Identity")]
    public string TraderName = "Trader";

    [Tooltip("Shown in the trader UI. Reused by DialogueSystem (Wave 5) for the greeting portrait.")]
    public Sprite TraderPortrait;

    [Header("Stock (selling TO the player)")]
    public StockEntry[] Stock = System.Array.Empty<StockEntry>();

    [Header("Buying FROM the player")]
    [Tooltip("Fraction of an item's Base Value paid when the player sells it to this trader.")]
    [Range(0f, 1f)] public float SellFraction = 0.5f;

    [Tooltip("When true, this trader buys any item with Base Value > 0. When false, it only buys " +
             "items it also stocks.")]
    public bool BuysAnything = true;

    [Header("Restock")]
    [Tooltip("Number of returns to the hub between restocks. 0 = never restocks.")]
    [Min(0)] public int RestockIntervalRuns = 3;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Stock == null) return;
        foreach (var e in Stock)
            if (e.Item == null)
                Debug.LogWarning($"[TraderSO] '{name}' has a stock entry with no Item.", this);
    }
#endif
}
