using UnityEngine;
using UnityEngine.UI;

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
    public PlayerHealth playerHealth;

    [Header("=== Position ===")]
    public float paddingLeft   = 20f;
    public float paddingBottom = 20f;

    [Header("=== Style ===")]
    public Color healthColor = new Color(0.80f, 0.15f, 0.15f, 0.90f);
    public Color energyColor = new Color(0.85f, 0.75f, 0.15f, 0.90f);

    private Image _healthFill;
    private Image _energyFill;

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
        rt.sizeDelta        = new Vector2(BarW, 2f * BarH + Gap);
        rt.anchoredPosition = new Vector2(paddingLeft, paddingBottom);

        // Energy bar — row 0 (bottom)
        _energyFill = BuildBar(panel.transform, 0, energyColor, "Energy");
        // Health bar — row 1 (top)
        _healthFill = BuildBar(panel.transform, 1, healthColor,  "Health");
    }

    private void Update()
    {
        if (playerHealth == null) return;
        _healthFill.fillAmount = playerHealth.maxHealth > 0f
            ? playerHealth.CurrentHealth / playerHealth.maxHealth : 0f;
        _energyFill.fillAmount = playerHealth.maxEnergy > 0f
            ? playerHealth.CurrentEnergy / playerHealth.maxEnergy : 0f;
    }

    private static Image BuildBar(Transform parent, int row, Color fillColor, string label)
    {
        float yPos = row * (BarH + Gap);

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
        fill.type         = Image.Type.Filled;
        fill.fillMethod   = Image.FillMethod.Horizontal;
        fill.fillAmount   = 1f;
        fill.raycastTarget = false;
        return fill;
    }
}
