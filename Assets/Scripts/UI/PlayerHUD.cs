using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Bottom-LEFT HUD panel with a health bar (red) and energy bar (yellow).
///
/// Setup:
///   1. Add this component to ANY child GameObject inside a Canvas.
///   2. Assign PlayerHealth in the Inspector.
///
/// The script creates its own RectTransform panel as a direct child of the nearest
/// Canvas so positioning is always relative to the bottom-left screen edge,
/// regardless of where in the hierarchy this component lives.
/// </summary>
public sealed class PlayerHUD : MonoBehaviour
{
    [Header("=== References ===")]
    public PlayerHealth      playerHealth;
    public EncumbranceSystem encumbrance;
    public BatterySystem     batterySystem;

    [Header("=== Position ===")]
    public float paddingLeft   = 20f;
    public float paddingBottom = 20f;

    [Header("=== Style ===")]
    public Color healthColor  = new Color(0.80f, 0.15f, 0.15f, 0.90f);
    public Color energyColor  = new Color(0.85f, 0.75f, 0.15f, 0.90f);
    public Color batteryColor = new Color(0.15f, 0.65f, 0.90f, 0.90f);

    private Image           _healthFill;
    private Image           _energyFill;
    private Image           _batteryFill;
    private TextMeshProUGUI _weightLabel;

    private static Sprite _fillSprite;

    private const float BarW = 200f;
    private const float BarH = 18f;
    private const float Gap  = 5f;

    private void Awake()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[PlayerHUD] Must be inside a Canvas."); return; }

        // Create a self-contained panel directly under the Canvas
        var panel = new GameObject("PlayerHUDPanel", typeof(RectTransform));
        panel.transform.SetParent(canvas.transform, false);

        var rt              = panel.GetComponent<RectTransform>();
        rt.anchorMin        = Vector2.zero;              // bottom-left anchor
        rt.anchorMax        = Vector2.zero;
        rt.pivot            = Vector2.zero;              // pivot at bottom-left
        rt.sizeDelta        = new Vector2(BarW, 3f * BarH + 2f * Gap);
        rt.anchoredPosition = new Vector2(paddingLeft, paddingBottom);

        // Battery bar — row 0 (bottom)
        _batteryFill = BuildBar(panel.transform, 0f, batteryColor, "Battery");
        // Energy bar — row 1
        _energyFill  = BuildBar(panel.transform, 1f * (BarH + Gap), energyColor,  "Energy");
        // Health bar — row 2 (top)
        _healthFill  = BuildBar(panel.transform, 2f * (BarH + Gap), healthColor,  "Health");
        // Weight label — above the bars
        _weightLabel = BuildWeightLabel(panel.transform);
    }

    private void Update()
    {
        if (playerHealth == null) return;
        _healthFill.fillAmount  = playerHealth.maxHealth > 0f
            ? playerHealth.CurrentHealth / playerHealth.maxHealth : 0f;
        _energyFill.fillAmount  = playerHealth.maxEnergy > 0f
            ? playerHealth.CurrentEnergy / playerHealth.maxEnergy : 0f;
        _batteryFill.fillAmount = batterySystem != null
            ? batterySystem.ActiveChargeNormalized : 0f;

        UpdateWeightLabel();
    }

    private void UpdateWeightLabel()
    {
        if (_weightLabel == null || encumbrance == null) return;

        float current = encumbrance.CurrentWeightKg;
        float max     = encumbrance.MaxCarryWeightKg;
        float ratio   = max > 0f ? current / max : 0f;

        _weightLabel.text = $"{current:F1} / {max:F1} kg";
        _weightLabel.color = ratio < 0.6f  ? new Color(0.9f, 0.9f, 0.9f, 0.85f)   // white
                           : ratio < 0.85f ? new Color(0.95f, 0.80f, 0.1f, 0.9f)   // yellow
                           : ratio < 1.0f  ? new Color(0.95f, 0.50f, 0.1f, 0.9f)   // orange
                                           : new Color(0.90f, 0.15f, 0.15f, 0.9f);  // red
    }

    private static TextMeshProUGUI BuildWeightLabel(Transform parent)
    {
        float yPos = 3 * (BarH + Gap) + 2f;

        var go = new GameObject("WeightLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt        = go.GetComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.zero;
        rt.pivot      = Vector2.zero;
        rt.sizeDelta  = new Vector2(BarW, 16f);
        rt.anchoredPosition = new Vector2(0f, yPos);

        var tmp       = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize  = 11f;
        tmp.color     = new Color(0.9f, 0.9f, 0.9f, 0.85f);
        tmp.text      = "0.0 / 40.0 kg";
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Image BuildBar(Transform parent, float yPos, Color fillColor, string label)
    {

        var bgGO              = new GameObject($"{label}BG", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(parent, false);
        var bgRT              = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin        = Vector2.zero;
        bgRT.anchorMax        = Vector2.zero;
        bgRT.pivot            = Vector2.zero;
        bgRT.sizeDelta        = new Vector2(BarW, BarH);
        bgRT.anchoredPosition = new Vector2(0f, yPos);
        bgGO.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.75f);
        bgGO.GetComponent<Image>().raycastTarget = false;

        var fillGO        = new GameObject($"{label}Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(bgGO.transform, false);
        var fillRT        = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin  = Vector2.zero;
        fillRT.anchorMax  = Vector2.one;
        fillRT.offsetMin  = new Vector2(2f, 2f);
        fillRT.offsetMax  = new Vector2(-2f, -2f);

        var fill          = fillGO.GetComponent<Image>();
        fill.color        = fillColor;
        if (_fillSprite == null)
        {
            var tex = Texture2D.whiteTexture;
            _fillSprite = Sprite.Create(
                tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        fill.sprite = _fillSprite;
        fill.type         = Image.Type.Filled;
        fill.fillMethod   = Image.FillMethod.Horizontal;
        fill.fillAmount   = 1f;
        fill.raycastTarget = false;
        return fill;
    }
}
