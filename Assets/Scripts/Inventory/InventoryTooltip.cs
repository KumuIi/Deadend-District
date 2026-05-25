using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Cursor-following tooltip that shows item stats on hover.
/// No background panel — text-only with a drop shadow for legibility.
/// Parented to the root Canvas so it is never tilted.
/// </summary>
public sealed class InventoryTooltip
{
    /// <summary>Set by InventoryUI so the tooltip can show equipped state without coupling to InventoryUI internals.</summary>
    public Func<ItemInstance, bool> IsItemEquipped;

    private readonly RectTransform   _rt;
    private readonly TextMeshProUGUI _text;
    private readonly Canvas          _canvas;

    public InventoryTooltip(Canvas canvas)
    {
        _canvas = canvas;

        var go = new GameObject("InventoryTooltip",
            typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(canvas.transform, false);

        _rt           = go.GetComponent<RectTransform>();
        _rt.anchorMin = new Vector2(0.5f, 0.5f);
        _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot     = new Vector2(1f, 1f);        // top-right follows cursor; box extends left
        _rt.sizeDelta = new Vector2(220f, 120f);    // fixed size; text overflows vertically if longer

        go.GetComponent<CanvasGroup>().blocksRaycasts = false;

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);

        var trt       = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        trt.sizeDelta = Vector2.zero;               // pure stretch — fills the container exactly

        _text                    = textGO.GetComponent<TextMeshProUGUI>();
        _text.font               = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        _text.fontSize           = 13;
        _text.color              = Color.white;
        _text.alignment          = TextAlignmentOptions.TopRight;
        _text.richText           = true;
        _text.enableWordWrapping = true;
        _text.overflowMode       = TextOverflowModes.Overflow;
        _text.raycastTarget      = false;

        go.SetActive(false);
    }

    public void Show(ItemInstance item, Vector2 screenPos)
    {
        _rt.gameObject.SetActive(true);   // activate before positioning so anchor is applied immediately
        _text.text = BuildText(item);
        MoveToScreen(screenPos);
        _rt.transform.SetAsLastSibling();
    }

    public void UpdatePosition(Vector2 screenPos) => MoveToScreen(screenPos);

    public void Hide() => _rt.gameObject.SetActive(false);

    private void MoveToScreen(Vector2 screenPos) =>
        CanvasUtils.MoveToScreenPoint(_rt, _canvas, screenPos);

    private string BuildText(ItemInstance item)
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
                return $"<b>{wi.WeaponDef.itemName}</b>\n" +
                       $"Caliber: {cal}\n" +
                       $"Ammo: {ammo}\n" +
                       $"Fire type: {mode}" +
                       WeightLine(item);
            }
            case MagazineItemInstance mi:
            {
                string cal = mi.MagDef.caliber ? mi.MagDef.caliber.displayName : "—";
                return $"<b>{mi.MagDef.itemName}</b>\n" +
                       $"Caliber: {cal}\n" +
                       $"Ammo: {mi.RuntimeMag.BulletCount} / {mi.MagDef.capacity}" +
                       WeightLine(item);
            }
            case AmmoItemInstance ai:
            {
                string cal = ai.AmmoDef.caliber ? ai.AmmoDef.caliber.displayName : "—";
                return $"<b>{ai.AmmoDef.itemName}</b>\n" +
                       $"Caliber: {cal}\n" +
                       $"Count: {ai.CurrentCount}" +
                       WeightLine(item);
            }
            case FlashlightItemInstance fi:
            {
                bool equipped = IsItemEquipped?.Invoke(fi) ?? false;
                string battery = equipped
                    ? $"{Mathf.RoundToInt((BatterySystem.Instance?.ActiveChargeNormalized ?? 0f) * 100f)}%"
                    : "--";
                return $"<b>{fi.data.itemName}</b>\n" +
                       $"Battery: {battery}" +
                       WeightLine(item);
            }
            default:
                return $"<b>{item.data.itemName}</b>" + WeightLine(item);
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
