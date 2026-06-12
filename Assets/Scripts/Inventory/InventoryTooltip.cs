using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Cursor-following tooltip that shows item stats on hover.
/// Restyled to match the angular orange/toxic-green HUD:
///   - Ink skewed chip (skew 6) as background.
///   - Item name in HudKit.Orange bold-italic.
///   - Toxic-green underline (1.5px high) under the name.
///   - Stat/description lines in HudKit.OffWhite.
///   - Subtle 0.06-alpha orange Stripes overlay.
///   - Quick fade + slide-in (0.12 s, 6 px offset) on Show.
/// Parented to the root Canvas so it is never tilted.
/// </summary>
public sealed class InventoryTooltip
{
    // ── Layout constants ────────────────────────────────────────────────────

    private const float ChipW        = 220f;
    private const float ChipH        = 120f;
    private const float Padding       = 10f;   // inner edge inset
    private const float NameFontSize  = 14f;
    private const float BodyFontSize  = 12f;
    private const float UnderlineH    = 1.5f;
    private const float SlidePx       = 6f;
    private const float FadeDur       = 0.12f;

    // ── State ───────────────────────────────────────────────────────────────

    private readonly RectTransform   _rt;          // root GO
    private readonly CanvasGroup     _cg;          // for fade tween
    private readonly TextMeshProUGUI _nameText;
    private readonly TextMeshProUGUI _bodyText;
    private readonly Canvas          _canvas;

    /// <summary>
    /// Optional extra line appended to the tooltip body (e.g. a trader's "Sell: 50 cr").
    /// Null by default so normal inventory tooltips are unaffected. Return null/empty to add nothing.
    /// </summary>
    public System.Func<ItemInstance, string> ExtraLineProvider;

    // ── Constructor ─────────────────────────────────────────────────────────

    public InventoryTooltip(Canvas canvas)
    {
        _canvas = canvas;

        // ── Root GO — invisible chip container ──────────────────────────
        var root = new GameObject("InventoryTooltip", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);

        _rt           = root.GetComponent<RectTransform>();
        _rt.anchorMin = new Vector2(0.5f, 0.5f);
        _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot     = new Vector2(1f, 1f);   // top-right follows cursor; box extends left & down
        _rt.sizeDelta = new Vector2(ChipW, ChipH);

        _cg = root.GetComponent<CanvasGroup>();
        _cg.blocksRaycasts = false;

        // ── Background chip — Ink, skew 6 ───────────────────────────────
        // Force-assign palette color here in case stale serialized color leaks from an older prefab.
        var chipColor = new Color(HudKit.Ink.r, HudKit.Ink.g, HudKit.Ink.b, 0.95f); // Ink @0.95 alpha
        var bg = HudKit.Img(root.transform, "BG", chipColor);
        bg.rectTransform.anchorMin = Vector2.zero;
        bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = Vector2.zero;
        bg.rectTransform.offsetMax = Vector2.zero;
        HudKit.Skew(bg, 6f);

        // ── Stripes overlay — Orange @ 0.06 alpha, stretched ────────────
        var stripeColor = new Color(HudKit.Orange.r, HudKit.Orange.g, HudKit.Orange.b, 0.06f);
        var stripes = HudKit.Img(root.transform, "Stripes", stripeColor, HudKit.Stripes);
        stripes.type = Image.Type.Tiled;
        stripes.rectTransform.anchorMin = Vector2.zero;
        stripes.rectTransform.anchorMax = Vector2.one;
        stripes.rectTransform.offsetMin = Vector2.zero;
        stripes.rectTransform.offsetMax = Vector2.zero;
        // raycastTarget already false from HudKit.Img

        // ── Item name text — Orange, bold-italic ─────────────────────────
        _nameText = HudKit.Text(root.transform, "NameText", NameFontSize,
            HudKit.Orange,
            TextAlignmentOptions.TopLeft,
            FontStyles.Bold | FontStyles.Italic);
        _nameText.textWrappingMode = TextWrappingModes.NoWrap;
        _nameText.overflowMode     = TextOverflowModes.Ellipsis;
        var nRT           = _nameText.rectTransform;
        nRT.anchorMin     = new Vector2(0f, 1f);
        nRT.anchorMax     = new Vector2(1f, 1f);
        nRT.pivot         = new Vector2(0f, 1f);
        nRT.offsetMin     = new Vector2(Padding, -Padding - NameFontSize);
        nRT.offsetMax     = new Vector2(-Padding, -Padding);
        // sizeDelta.y is ignored — anchored row; height driven by offsetMin/Max

        // ── Toxic-green underline — 1.5 px high, sits just below the name row ──
        var underlineColor = new Color(HudKit.Green.r, HudKit.Green.g, HudKit.Green.b, 1f);
        var underline = HudKit.Img(root.transform, "NameUnderline", underlineColor);
        var uRT       = underline.rectTransform;
        uRT.anchorMin = new Vector2(0f, 1f);
        uRT.anchorMax = new Vector2(1f, 1f);
        uRT.pivot     = new Vector2(0f, 1f);
        // Position it just below the name baseline
        float underlineTop = -(Padding + NameFontSize + 2f);
        uRT.offsetMin = new Vector2(Padding, underlineTop - UnderlineH);
        uRT.offsetMax = new Vector2(-Padding, underlineTop);

        // ── Body / stats text — OffWhite, normal weight ──────────────────
        _bodyText = HudKit.Text(root.transform, "BodyText", BodyFontSize,
            HudKit.OffWhite,
            TextAlignmentOptions.TopLeft,
            FontStyles.Normal);
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        _bodyText.overflowMode     = TextOverflowModes.Overflow;
        var bRT       = _bodyText.rectTransform;
        bRT.anchorMin = Vector2.zero;
        bRT.anchorMax = Vector2.one;
        // Top inset below the underline; sides and bottom leave padding
        float bodyTop = underlineTop - UnderlineH - 3f;
        bRT.offsetMin = new Vector2(Padding, Padding);
        bRT.offsetMax = new Vector2(-Padding, bodyTop);

        root.SetActive(false);
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public void Show(ItemInstance item, Vector2 screenPos)
    {
        _rt.gameObject.SetActive(true);

        _nameText.text = GetItemName(item);
        _bodyText.text = BuildBodyText(item);

        // Append optional extra line (e.g. trader price)
        string extra = ExtraLineProvider?.Invoke(item);
        if (!string.IsNullOrEmpty(extra)) _bodyText.text += "\n" + extra;

        MoveToScreen(screenPos);
        _rt.transform.SetAsLastSibling();

        // Fade + slide-in: start transparent and offset, tween to opaque at rest position
        _cg.alpha = 0f;
        var startPos = _rt.anchoredPosition;
        _rt.anchoredPosition = new Vector2(startPos.x, startPos.y - SlidePx);

        _cg.DOFade(1f, FadeDur)
            .SetEase(Ease.OutCubic)
            .SetLink(_rt.gameObject);
        _rt.DOAnchorPosY(startPos.y, FadeDur)
            .SetEase(Ease.OutCubic)
            .SetLink(_rt.gameObject);
    }

    public void UpdatePosition(Vector2 screenPos) => MoveToScreen(screenPos);

    public void Hide() => _rt.gameObject.SetActive(false);

    // ── Internal helpers ────────────────────────────────────────────────────

    private void MoveToScreen(Vector2 screenPos) =>
        CanvasUtils.MoveToScreenPoint(_rt, _canvas, screenPos);

    private static string GetItemName(ItemInstance item) => item switch
    {
        WeaponItemInstance    wi => wi.WeaponDef.itemName,
        MagazineItemInstance  mi => mi.MagDef.itemName,
        AmmoItemInstance      ai => ai.AmmoDef.itemName,
        BatteryItemInstance   bi => bi.data.itemName,
        FlashlightItemInstance fi => fi.data.itemName,
        _                        => item.data.itemName,
    };

    private string BuildBodyText(ItemInstance item)
    {
        switch (item)
        {
            case WeaponItemInstance wi:
            {
                string cal  = wi.WeaponDef.caliber ? wi.WeaponDef.caliber.displayName : "—";
                string ammo = wi.LoadedMagazine != null
                    ? $"{wi.LoadedMagazine.RuntimeMag.BulletCount} / {wi.LoadedMagazine.MagDef.capacity}"
                    : "No magazine";
                string mode = FireModeLabel(wi.WeaponDef.fireMode);
                return $"Caliber: {cal}\n" +
                       $"Ammo: {ammo}\n" +
                       $"Fire type: {mode}" +
                       WeightLine(item);
            }
            case MagazineItemInstance mi:
            {
                string cal = mi.MagDef.caliber ? mi.MagDef.caliber.displayName : "—";
                return $"Caliber: {cal}\n" +
                       $"Ammo: {mi.RuntimeMag.BulletCount} / {mi.MagDef.capacity}" +
                       WeightLine(item);
            }
            case AmmoItemInstance ai:
            {
                string cal = ai.AmmoDef.caliber ? ai.AmmoDef.caliber.displayName : "—";
                return $"Caliber: {cal}\n" +
                       $"Count: {ai.CurrentCount}" +
                       WeightLine(item);
            }
            case BatteryItemInstance bi:
            {
                string charge = $"{Mathf.RoundToInt(bi.ChargeNormalized * 100f)}%";
                string type   = bi.BatteryType == BatteryType.Rechargeable ? "Rechargeable" : "One-time";
                return $"Charge: {charge}\n" +
                       $"Type: {type}" +
                       WeightLine(item);
            }
            case FlashlightItemInstance fi:
            {
                string battery = $"{Mathf.RoundToInt(fi.ChargeNormalized * 100f)}%";
                return $"Battery: {battery}" +
                       WeightLine(item);
            }
            default:
                return WeightLine(item).TrimStart('\n');
        }
    }

    private static string WeightLine(ItemInstance item) =>
        $"\nWeight: {item.data.weightKg:0.0} kg";

    private static string FireModeLabel(FireMode mode) => mode switch
    {
        FireMode.FullAuto => "Full-Auto",
        FireMode.SemiAuto => "Semi-Auto",
        FireMode.Burst    => "Burst",
        _                 => mode.ToString(),
    };
}
