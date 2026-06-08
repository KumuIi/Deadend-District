using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per-invocation configuration for a context menu. Built by the requesting InventoryUI
/// and handed to <see cref="InventoryContextMenu.Show"/>. Because a single shared menu now
/// serves every panel (player, stash, trader), the callbacks can't live on the menu — they
/// differ per right-click, so they travel with the request instead.
/// </summary>
public sealed class ContextMenuRequest
{
    public ItemInstance Item;
    public Vector2      ScreenPos;

    /// <summary>When false, the Equip/Unequip entry is omitted (e.g. the stash).</summary>
    public bool AllowEquip = true;

    /// <summary>When false, the standard mutation entries (Remove Magazine/Battery, Drop) are omitted.</summary>
    public bool AllowItemActions = true;

    public Action<ItemInstance>      OnEquip;
    public Action<ItemInstance>      OnUnequip;
    public Action<ItemInstance>      OnRemoveMagazine;
    public Action<ItemInstance>      OnRemoveBattery;
    public Action<ItemInstance>      OnDrop;
    public Action<AmmoItemInstance, int> OnSplitAmmo;

    /// <summary>Return true if the given item is currently the equipped weapon/flashlight.</summary>
    public Func<ItemInstance, bool> IsItemEquipped;

    /// <summary>Optional hook to append context-agnostic extra entries (e.g. a trader's Buy/Sell).</summary>
    public Func<ItemInstance, List<(string label, Action action)>> ExtraEntriesProvider;
}

/// <summary>
/// Right-click context menu for inventory items. Pure rendering engine — owns no per-panel
/// state; every Show() call supplies its own <see cref="ContextMenuRequest"/>.
///
/// Built on a top-most Screen Space – Overlay canvas (see InventoryContextMenuService) so it
/// always wins the GraphicRaycaster against any panel canvas and renders above the 3D item
/// meshes. A single instance is shared by all panels.
///
/// Dismissal uses a fullscreen transparent background Button so that both the dismiss and the
/// item-button clicks live inside the EventSystem — avoiding the timing bug where
/// Input.GetMouseButtonDown closes the panel before Button.onClick fires.
/// </summary>
public sealed class InventoryContextMenu
{
    private const float ButtonH = 48f;  // 1.5× base
    private const float ButtonW = 255f; // 1.5× base

    private readonly GameObject    _panel;
    private readonly RectTransform _panelRT;
    private readonly GameObject    _bgDismiss;
    private readonly Canvas        _canvas;

    public bool IsOpen => _panel.activeSelf;

    public InventoryContextMenu(Canvas canvas)
    {
        _canvas = canvas;

        // ── Fullscreen dismiss layer (behind the panel) ────────────────────
        _bgDismiss = new GameObject("ContextMenuBG",
            typeof(RectTransform), typeof(Image), typeof(Button));
        _bgDismiss.transform.SetParent(canvas.transform, false);

        var bgRT          = _bgDismiss.GetComponent<RectTransform>();
        bgRT.anchorMin    = Vector2.zero;
        bgRT.anchorMax    = Vector2.one;
        bgRT.offsetMin    = Vector2.zero;
        bgRT.offsetMax    = Vector2.zero;

        _bgDismiss.GetComponent<Image>().color        = new Color(0f, 0f, 0f, 0f);
        _bgDismiss.GetComponent<Image>().raycastTarget = true;
        _bgDismiss.GetComponent<Button>().onClick.AddListener(Hide);
        _bgDismiss.SetActive(false);

        // ── Menu panel (above the dismiss layer) ──────────────────────────
        _panel = new GameObject("ContextMenuPanel",
            typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _panel.transform.SetParent(canvas.transform, false);

        _panelRT       = _panel.GetComponent<RectTransform>();
        _panelRT.pivot = new Vector2(1f, 1f);

        _panel.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.11f, 0.97f);
        _panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

        _panel.SetActive(false);
    }

    public void Show(ContextMenuRequest req)
    {
        if (req == null || req.Item == null) { Hide(); return; }
        ItemInstance item = req.Item;

        // Rebuild buttons for this item type
        for (int i = _panel.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(_panel.transform.GetChild(i).gameObject);

        var entries = new List<(string label, Action action)>();

        if (item is WeaponItemInstance wi)
        {
            if (req.AllowEquip)
            {
                bool equipped = req.IsItemEquipped?.Invoke(item) ?? false;
                if (equipped)
                    entries.Add(("Unequip",         () => { req.OnUnequip?.Invoke(item);        Hide(); }));
                else
                    entries.Add(("Equip",           () => { req.OnEquip?.Invoke(item);          Hide(); }));
            }

            if (req.AllowItemActions && wi.LoadedMagazine != null)
                entries.Add(("Remove Magazine", () => { req.OnRemoveMagazine?.Invoke(item); Hide(); }));
        }
        else if (item is FlashlightItemInstance fi)
        {
            if (req.AllowEquip)
            {
                bool equipped = req.IsItemEquipped?.Invoke(item) ?? false;
                if (equipped)
                    entries.Add(("Unequip", () => { req.OnUnequip?.Invoke(item); Hide(); }));
                else
                    entries.Add(("Equip",   () => { req.OnEquip?.Invoke(item);   Hide(); }));
            }

            if (req.AllowItemActions && fi.InsertedBattery != null)
                entries.Add(("Remove Battery", () => { req.OnRemoveBattery?.Invoke(item); Hide(); }));
        }
        else if (item is AmmoItemInstance ammo && req.AllowItemActions)
        {
            // Split halves the stack; Take 10 peels a fixed 10 off. Both leave ≥1 round behind,
            // so they only appear when there's enough to split (Split.cs enforces the same rule).
            if (ammo.CurrentCount > 1)
                entries.Add(("Split",   () => { req.OnSplitAmmo?.Invoke(ammo, ammo.CurrentCount / 2); Hide(); }));
            if (ammo.CurrentCount > 10)
                entries.Add(("Take 10", () => { req.OnSplitAmmo?.Invoke(ammo, 10); Hide(); }));
        }

        if (req.AllowItemActions)
            entries.Add(("Drop", () => { req.OnDrop?.Invoke(item); Hide(); }));

        // Injected, economy-agnostic entries (Buy/Sell). Hide() after each so a refresh that
        // destroys this item's view can't leave a dangling menu.
        var extra = req.ExtraEntriesProvider?.Invoke(item);
        if (extra != null)
            foreach (var (label, action) in extra)
                entries.Add((label, () => { action?.Invoke(); Hide(); }));

        // Nothing to show (e.g. a view-only grid item the trader won't buy) — don't pop an empty menu.
        if (entries.Count == 0) { Hide(); return; }

        _panelRT.sizeDelta = new Vector2(ButtonW, ButtonH * entries.Count);

        for (int i = 0; i < entries.Count; i++)
            AddButton(entries[i].label, i, entries[i].action);

        CanvasUtils.MoveToScreenPoint(_panelRT, _canvas, req.ScreenPos);

        // Dismiss BG first in Z, panel on top
        _bgDismiss.SetActive(true);
        _bgDismiss.transform.SetAsLastSibling();

        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
    }

    public void Hide()
    {
        _panel.SetActive(false);
        _bgDismiss.SetActive(false);
    }

    private void AddButton(string label, int index, Action onClick)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_panel.transform, false);

        var rt              = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.sizeDelta        = new Vector2(0f, ButtonH);
        rt.anchoredPosition = new Vector2(0f, -index * ButtonH);

        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var btn             = go.GetComponent<Button>();
        var colors          = btn.colors;
        colors.normalColor      = new Color(0f, 0f, 0f, 0f);
        colors.highlightedColor = new Color(0.22f, 0.22f, 0.30f, 0.90f);
        colors.pressedColor     = new Color(0.12f, 0.48f, 0.12f, 0.90f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);

        var trt       = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(18f, 0f); // 1.5× base
        trt.offsetMax = Vector2.zero;

        var t           = textGO.GetComponent<TextMeshProUGUI>();
        t.text          = label;
        t.fontSize      = 20; // 1.5× base (≈19.5)
        t.color         = new Color(0.90f, 0.90f, 0.90f, 1f);
        t.alignment     = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
    }
}
