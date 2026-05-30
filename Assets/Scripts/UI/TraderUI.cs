using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The buy/sell panel a TraderSystem opens. A self-contained uGUI list view — it does NOT
/// reuse the 3D model-over-panel InventoryUI for the player side, it just reads
/// InventoryUI.Grid.PlacedItems and renders a flat sell list. That keeps the trader decoupled
/// from the inventory's drag/model machinery.
///
/// Left column  = trader stock (Buy buttons). Right column = player loot (Sell buttons).
/// Rows are pooled-by-rebuild: cleared and re-instantiated on every Refresh. Stock is small,
/// so this is simpler than a diff and never leaves a stale row behind.
///
/// Input is gated through GameInputState (which also frees the cursor), matching every other
/// menu — gameplay resumes only when the panel closes.
/// </summary>
public class TraderUI : MonoBehaviour
{
    [Header("=== Panel ===")]
    [Tooltip("Root object toggled on/off. Keep this separate from the GameObject holding this script.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Button     _closeButton;

    [Header("=== Header ===")]
    [SerializeField] private TMP_Text _traderNameText;
    [SerializeField] private Image    _portrait;
    [SerializeField] private TMP_Text _creditsText;

    [Header("=== Buy list (trader stock) ===")]
    [SerializeField] private RectTransform _buyContent;
    [SerializeField] private TraderListRow _buyRowPrefab;

    [Header("=== Sell list (player inventory) ===")]
    [SerializeField] private RectTransform _sellContent;
    [SerializeField] private TraderListRow _sellRowPrefab;
    [Tooltip("Source of the player's sellable items.")]
    [SerializeField] private InventoryUI   _playerInventoryUI;

    private TraderSystem _trader;
    private bool _isShowing;
    private readonly List<TraderListRow> _buyRows  = new List<TraderListRow>();
    private readonly List<TraderListRow> _sellRows = new List<TraderListRow>();

    private void Awake()
    {
        if (_root != null) _root.SetActive(false);
        if (_closeButton != null)
            _closeButton.onClick.AddListener(() => _trader?.Close());
    }

    private void OnDisable()
    {
        // Safety net: if the panel is torn down while open (scene unload), release the input
        // block and event subscription so the block count and cursor stay balanced.
        if (!_isShowing) return;
        CurrencyService.OnCreditsChanged -= OnCreditsChanged;
        GameInputState.Unblock();
        _isShowing = false;
        _trader = null;
    }

    // ── Open / Close ───────────────────────────────────────────────────────

    /// <summary>Called by TraderSystem.Open().</summary>
    public void Open(TraderSystem trader)
    {
        if (trader == null) return;
        _trader = trader;

        if (_root != null) _root.SetActive(true);
        GameInputState.Block(); // also frees + shows the cursor
        _isShowing = true;

        CurrencyService.OnCreditsChanged += OnCreditsChanged;
        Refresh();
    }

    /// <summary>
    /// Called by TraderSystem.Close() — TraderSystem owns the IsOpen flag, so the UI never
    /// closes itself directly. This just tears down the view.
    /// </summary>
    public void NotifyClosed()
    {
        if (!_isShowing) return; // OnDisable may have already torn down
        _isShowing = false;

        CurrencyService.OnCreditsChanged -= OnCreditsChanged;
        ClearRows(_buyRows);
        ClearRows(_sellRows);

        if (_root != null) _root.SetActive(false);
        GameInputState.Unblock();
        _trader = null;
    }

    private void OnCreditsChanged(int _) => Refresh();

    // ── Build ──────────────────────────────────────────────────────────────

    /// <summary>Rebuilds both lists and the header. Safe to call any time the panel is open.</summary>
    public void Refresh()
    {
        if (_trader == null) return;

        UpdateHeader();
        BuildBuyList();
        BuildSellList();
    }

    private void UpdateHeader()
    {
        var def = _trader.Definition;
        if (_traderNameText != null) _traderNameText.text = def != null ? def.TraderName : "Trader";
        if (_creditsText != null)    _creditsText.text    = $"{CurrencyService.GetCredits()} cr";

        if (_portrait != null)
        {
            _portrait.sprite  = def != null ? def.TraderPortrait : null;
            _portrait.enabled = _portrait.sprite != null;
        }
    }

    private void BuildBuyList()
    {
        ClearRows(_buyRows);
        if (_buyRowPrefab == null || _buyContent == null) return;

        foreach (var offer in _trader.GetOffers())
        {
            string stock  = offer.Unlimited ? "" : $"  (x{offer.Remaining})";
            string detail = $"{offer.BuyPrice} cr{stock}";
            bool   canBuy = offer.InStock && CurrencyService.CanAfford(offer.BuyPrice);

            int index = offer.StockIndex; // capture for the closure
            var row = Instantiate(_buyRowPrefab, _buyContent);
            row.Bind(offer.Item.itemName, detail, "Buy", canBuy, () => OnBuyClicked(index));
            _buyRows.Add(row);
        }
    }

    private void BuildSellList()
    {
        ClearRows(_sellRows);
        if (_sellRowPrefab == null || _sellContent == null || _playerInventoryUI == null) return;

        foreach (var item in _playerInventoryUI.Grid.PlacedItems)
        {
            int  price    = _trader.GetSellPrice(item);
            bool sellable = price > 0;
            string detail = sellable ? $"{price} cr" : "—";

            var captured = item; // capture for the closure
            var row = Instantiate(_sellRowPrefab, _sellContent);
            row.Bind(item.data != null ? item.data.itemName : "Item", detail, "Sell", sellable,
                     () => OnSellClicked(captured));
            _sellRows.Add(row);
        }
    }

    // ── Button handlers ────────────────────────────────────────────────────

    private void OnBuyClicked(int stockIndex)
    {
        if (_trader != null && _trader.TryBuy(stockIndex))
            Refresh(); // CurrencyService event will also fire, but refresh now in case price was 0
    }

    private void OnSellClicked(ItemInstance item)
    {
        if (_trader != null && _trader.TrySell(item))
            Refresh();
    }

    private void ClearRows(List<TraderListRow> rows)
    {
        foreach (var row in rows)
            if (row != null) Destroy(row.gameObject);
        rows.Clear();
    }
}
