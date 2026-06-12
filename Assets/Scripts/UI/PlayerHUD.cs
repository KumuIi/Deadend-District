using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Bottom-LEFT HUD panel — health, energy, and battery bars in a
/// Persona-style diagonal skewed stack, plus weight readout.
///
/// Setup:
///   1. Add this component to ANY child GameObject inside a Canvas.
///   2. Assign PlayerHealth (and optionally EncumbranceSystem, FlashlightSlot) in the Inspector.
///
/// The script creates its own RectTransform panel as a direct child of the nearest
/// Canvas so positioning is always relative to the bottom-left screen edge,
/// regardless of where in the hierarchy this component lives.
///
/// All sizes are in canvas REFERENCE UNITS (Canvas ScaleWithScreenSize 800×600, match width).
/// </summary>
public sealed class PlayerHUD : MonoBehaviour
{
    [Header("=== References ===")]
    public PlayerHealth      playerHealth;
    public EncumbranceSystem encumbrance;
    public FlashlightSlot    flashlightSlot;

    [Header("=== Position ===")]
    public float paddingLeft   = 20f;
    public float paddingBottom = 20f;

    [Header("=== Style ===")]
    public Color healthColor  = new Color(1.00f, 0.478f, 0.102f, 1f);   // HudKit.Orange
    public Color energyColor  = new Color(0.549f, 0.910f, 0.188f, 1f);  // HudKit.Green
    public Color batteryColor = new Color(0.85f, 0.88f, 0.80f, 0.9f);   // desaturated white-green

    // ── Layout constants (reference units) ────────────────────────────────
    // Health: 150×14   Energy: 125×9   Battery: 105×6
    // Row gap 4. Stagger each row +8 right per level (0=battery,1=energy,2=health).
    private const float HealthW    = 150f;
    private const float HealthH    = 14f;
    private const float EnergyW    = 125f;
    private const float EnergyH    = 9f;
    private const float BatteryW   = 105f;
    private const float BatteryH   = 6f;
    private const float RowGap     = 4f;
    private const float RowStagger = 8f;   // each row shifts right by this per level
    // One shear SLOPE for everything: skewPixels = height * SkewSlope, so every box
    // leans at the exact same angle regardless of its height (clean parallel edges).
    private const float SkewSlope = 0.43f;
    private static float SkewFor(float height) => height * SkewSlope;
    private const float FillInset  = 1.5f; // backing oversize per side
    // Backing is bar + FillInset on each side → w = barW + FillInset*2, h = barH + FillInset*2
    // y bottom-left: health row starts at BatteryH + RowGap + EnergyH + RowGap
    private const float HealthY    = BatteryH + RowGap + EnergyH + RowGap; // = 6+4+9+4 = 23
    private const float EnergyY    = BatteryH + RowGap;                    // = 6+4 = 10
    private const float BatteryY   = 0f;

    // ── Runtime refs ───────────────────────────────────────────────────────
    private RectTransform    _panel;

    // Health row
    private Image            _healthBacking;
    private Image            _healthGhost;
    private Image            _healthFill;
    private TextMeshProUGUI  _healthNum;

    // Energy row
    private Image            _energyBacking;
    private Image            _energyGhost;
    private Image            _energyFill;

    // Battery row
    private Image            _batteryBacking;
    private Image            _batteryFill;

    // Weight chip
    private TextMeshProUGUI  _weightLabel;
    private Image            _weightBacking;

    // ── Cached values to avoid per-frame string alloc ─────────────────────
    private int   _lastHealthInt  = -1;
    private float _lastHealthNorm = -1f;
    private float _lastEnergyNorm = -1f;
    private float _lastBattNorm   = -1f;
    private float _lastWeightCur  = -1f;
    private float _lastWeightMax  = -1f;

    // ── Ghost-chip tween state ─────────────────────────────────────────────
    private Sequence _healthGhostSeq;
    private Sequence _energyGhostSeq;

    // ── Low-health pulse tween ─────────────────────────────────────────────
    private Tween  _lowHealthPulse;
    private bool   _wasLowHealth;

    // ── Low-energy pulse tween ────────────────────────────────────────────
    private Tween  _lowEnergyPulse;
    private bool   _wasLowEnergy;

    // ── Awake ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Force palette — scene prefab still carries pre-overhaul serialized colors.
        healthColor  = HudKit.Orange;
        energyColor  = HudKit.Green;
        batteryColor = new Color(0.85f, 0.88f, 0.80f, 0.9f);

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[PlayerHUD] Must be inside a Canvas."); return; }

        // Root panel — bottom-left anchor
        // Width: rightmost child is HP number at x=16+150+6=172, width ~50 → 230 ref units.
        // Height: weight chip top = 23+14+4 = 41, chip height = 11 → total 56 ref units.
        var panelGO        = new GameObject("PlayerHUDPanel", typeof(RectTransform));
        panelGO.transform.SetParent(canvas.transform, false);
        _panel             = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin   = Vector2.zero;
        _panel.anchorMax   = Vector2.zero;
        _panel.pivot       = Vector2.zero;
        _panel.sizeDelta   = new Vector2(230f, 60f);
        _panel.anchoredPosition = new Vector2(paddingLeft, paddingBottom);

        // ── Build rows (bottom to top) ─────────────────────────────────────

        // Battery row (rowIndex = 0, y = 0)
        BuildBatteryRow(panelGO.transform, BatteryY, 0);

        // Energy row (rowIndex = 1, y = 10)
        BuildEnergyRow(panelGO.transform, EnergyY, 1);

        // Health row (rowIndex = 2, y = 23)
        BuildHealthRow(panelGO.transform, HealthY, 2);

        // Weight chip above health bar (y = 23+14+4 = 41)
        BuildWeightChip(panelGO.transform, HealthY + HealthH + 4f);

        // Slide-in on start fires in OnEnable
    }

    // ── Enable / Disable — event wiring and slide-in ──────────────────────

    private void OnEnable()
    {
        if (_panel != null) _panel.gameObject.SetActive(true);

        if (playerHealth != null)
            playerHealth.OnDamaged += HandleDamaged;

        // Slide panel in from left
        if (_panel != null)
        {
            float targetX = paddingLeft;
            _panel.anchoredPosition = new Vector2(targetX - 80f, paddingBottom);
            _panel.DOAnchorPosX(targetX, 0.35f)
                  .SetEase(Ease.OutCubic)
                  .SetLink(_panel.gameObject);
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.OnDamaged -= HandleDamaged;

        // Kill state-change pulses safely
        _lowHealthPulse?.Kill();
        _lowHealthPulse = null;
        _lowEnergyPulse?.Kill();
        _lowEnergyPulse = null;

        if (_panel != null)
        {
            DOTween.Kill(_panel);
            _panel.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (_panel != null) Destroy(_panel.gameObject);
    }

    // ── Damage flash ───────────────────────────────────────────────────────

    private void HandleDamaged(float amount)
    {
        if (_healthFill == null) return;

        // Color flash on fill
        _healthFill.DOColor(HudKit.OrangeHot, 0.06f)
            .SetLink(_healthFill.gameObject)
            .OnComplete(() =>
                _healthFill.DOColor(healthColor, 0.18f)
                    .SetLink(_healthFill.gameObject));

        // Positional punch on the whole panel
        if (_panel != null)
            _panel.DOShakeAnchorPos(0.22f, new Vector2(3f, 2f), 12, 60f, false, false)
                  .SetLink(_panel.gameObject);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (playerHealth == null) return;

        float healthNorm = playerHealth.maxHealth > 0f
            ? playerHealth.CurrentHealth / playerHealth.maxHealth : 0f;
        float energyNorm = playerHealth.maxEnergy > 0f
            ? playerHealth.CurrentEnergy / playerHealth.maxEnergy : 0f;
        float battNorm   = flashlightSlot != null ? flashlightSlot.ChargeNormalized : 0f;

        // ── Health ────────────────────────────────────────────────────────
        if (!Mathf.Approximately(healthNorm, _lastHealthNorm))
        {
            bool decreased = healthNorm < _lastHealthNorm;
            float prev     = _lastHealthNorm < 0f ? 1f : _lastHealthNorm;
            _lastHealthNorm = healthNorm;

            // Tween main fill
            _healthFill.DOFillAmount(healthNorm, 0.12f)
                        .SetLink(_healthFill.gameObject);

            // Ghost chip
            _healthGhostSeq?.Kill();
            if (decreased)
            {
                // Ghost stays at old value, then slides down after delay
                _healthGhost.fillAmount = prev;
                _healthGhostSeq = DOTween.Sequence()
                    .SetLink(_healthGhost.gameObject)
                    .AppendInterval(0.35f)
                    .Append(_healthGhost.DOFillAmount(healthNorm, 0.4f));
            }
            else
            {
                // On heal, ghost snaps up immediately
                _healthGhost.fillAmount = healthNorm;
            }
        }

        // Numeric readout — only update string when int changes
        int healthInt = Mathf.CeilToInt(playerHealth.CurrentHealth);
        if (healthInt != _lastHealthInt)
        {
            _lastHealthInt = healthInt;
            _healthNum.SetText("{0}", healthInt);
        }

        // Low-health state
        bool isLowHealth = healthNorm < 0.25f;
        if (isLowHealth != _wasLowHealth)
        {
            _wasLowHealth = isLowHealth;
            _lowHealthPulse?.Kill();
            if (isLowHealth)
            {
                _healthFill.color = HudKit.Danger;
                _lowHealthPulse   = _healthBacking
                    .DOFade(0.55f, 0.5f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine)
                    .SetLink(_healthBacking.gameObject);
            }
            else
            {
                _healthFill.color      = healthColor;
                _healthBacking.color   = HudKit.Ink;
            }
        }

        // ── Energy ────────────────────────────────────────────────────────
        if (!Mathf.Approximately(energyNorm, _lastEnergyNorm))
        {
            bool decreased = energyNorm < _lastEnergyNorm;
            float prev     = _lastEnergyNorm < 0f ? 1f : _lastEnergyNorm;
            _lastEnergyNorm = energyNorm;

            _energyFill.DOFillAmount(energyNorm, 0.12f)
                        .SetLink(_energyFill.gameObject);

            _energyGhostSeq?.Kill();
            if (decreased)
            {
                _energyGhost.fillAmount = prev;
                _energyGhostSeq = DOTween.Sequence()
                    .SetLink(_energyGhost.gameObject)
                    .AppendInterval(0.35f)
                    .Append(_energyGhost.DOFillAmount(energyNorm, 0.4f));
            }
            else
            {
                _energyGhost.fillAmount = energyNorm;
            }
        }

        bool isLowEnergy = energyNorm < 0.20f;
        if (isLowEnergy != _wasLowEnergy)
        {
            _wasLowEnergy = isLowEnergy;
            _lowEnergyPulse?.Kill();
            if (isLowEnergy)
            {
                _lowEnergyPulse = _energyFill
                    .DOFade(0.45f, 0.6f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine)
                    .SetLink(_energyFill.gameObject);
            }
            else
            {
                _energyFill.color = energyColor;
            }
        }

        // ── Battery ───────────────────────────────────────────────────────
        if (!Mathf.Approximately(battNorm, _lastBattNorm))
        {
            _lastBattNorm = battNorm;
            _batteryFill.DOFillAmount(battNorm, 0.12f)
                         .SetLink(_batteryFill.gameObject);
        }

        UpdateWeightLabel();
    }

    // ── Weight label ───────────────────────────────────────────────────────

    private void UpdateWeightLabel()
    {
        if (_weightLabel == null || encumbrance == null) return;

        float current = encumbrance.CurrentWeightKg;
        float max     = encumbrance.MaxCarryWeightKg;

        if (Mathf.Approximately(current, _lastWeightCur) &&
            Mathf.Approximately(max,     _lastWeightMax)) return;

        _lastWeightCur = current;
        _lastWeightMax = max;

        float ratio = max > 0f ? current / max : 0f;
        _weightLabel.text = $"{current:F1} / {max:F1} kg";
        _weightLabel.color = ratio < 0.6f  ? new Color(0.9f, 0.9f, 0.9f, 0.85f)
                           : ratio < 0.85f ? new Color(0.95f, 0.80f, 0.1f, 0.9f)
                           : ratio < 1.0f  ? new Color(0.95f, 0.50f, 0.1f, 0.9f)
                                           : new Color(0.90f, 0.15f, 0.15f, 0.9f);
    }

    // ── Row builders ───────────────────────────────────────────────────────

    // Health row (rowIndex=2): bar at x=16, y=23, w=150, h=14
    //   Backing:   x=13, y=20, w=156, h=17   (bar ±FillInset=1.5 on each side)
    //   Ghost/Fill: x=16, y=23, w=150, h=14
    //   HP num:    x=172, y=23, w=50, h=18
    private void BuildHealthRow(Transform parent, float yPos, int rowIndex)
    {
        float xOff = rowIndex * RowStagger; // = 16
        float w    = HealthW;               // = 150
        float h    = HealthH;               // = 14

        // Ink backing: bar ± FillInset on each side
        _healthBacking = HudKit.Img(parent, "HealthBacking", HudKit.Ink);
        SetAnchored(_healthBacking.rectTransform,
            xOff - FillInset, yPos - FillInset,
            w + FillInset * 2f, h + FillInset * 2f);
        HudKit.Skew(_healthBacking, SkewFor(h + FillInset * 2f));

        // Ghost chip (OffWhite behind fill)
        _healthGhost = HudKit.Img(parent, "HealthGhost",
            new Color(HudKit.OffWhite.r, HudKit.OffWhite.g, HudKit.OffWhite.b, 0.25f));
        SetFillImage(_healthGhost, xOff, yPos, w, h);
        HudKit.Skew(_healthGhost, SkewFor(h));

        // Main orange fill
        _healthFill = HudKit.Img(parent, "HealthFill", healthColor);
        SetFillImage(_healthFill, xOff, yPos, w, h);
        HudKit.Skew(_healthFill, SkewFor(h));

        // Numeric readout: right of bar with 6-unit gap
        // x = xOff + w + 6 = 16+150+6 = 172; y aligns with bar bottom (yPos)
        _healthNum = HudKit.Text(parent, "HealthNum", 17f, HudKit.OffWhite,
            TextAlignmentOptions.Left, FontStyles.Bold | FontStyles.Italic);
        var rt = _healthNum.rectTransform;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.zero;
        rt.pivot            = new Vector2(0f, 0f);
        rt.sizeDelta        = new Vector2(50f, h + 4f);
        rt.anchoredPosition = new Vector2(xOff + w + 6f, yPos);
    }

    // Energy row (rowIndex=1): bar at x=8, y=10, w=125, h=9
    //   Backing: x=5, y=7, w=131, h=12
    private void BuildEnergyRow(Transform parent, float yPos, int rowIndex)
    {
        float xOff = rowIndex * RowStagger; // = 8
        float w    = EnergyW;               // = 125
        float h    = EnergyH;               // = 9

        _energyBacking = HudKit.Img(parent, "EnergyBacking", HudKit.Ink);
        SetAnchored(_energyBacking.rectTransform,
            xOff - FillInset, yPos - FillInset,
            w + FillInset * 2f, h + FillInset * 2f);
        HudKit.Skew(_energyBacking, SkewFor(h + FillInset * 2f));

        _energyGhost = HudKit.Img(parent, "EnergyGhost",
            new Color(HudKit.OffWhite.r, HudKit.OffWhite.g, HudKit.OffWhite.b, 0.18f));
        SetFillImage(_energyGhost, xOff, yPos, w, h);
        HudKit.Skew(_energyGhost, SkewFor(h));

        _energyFill = HudKit.Img(parent, "EnergyFill", energyColor);
        SetFillImage(_energyFill, xOff, yPos, w, h);
        HudKit.Skew(_energyFill, SkewFor(h));
    }

    // Battery row (rowIndex=0): bar at x=0, y=0, w=105, h=6
    //   Backing: x=-1.5, y=-1.5, w=108, h=9
    private void BuildBatteryRow(Transform parent, float yPos, int rowIndex)
    {
        float xOff = rowIndex * RowStagger; // = 0
        float w    = BatteryW;              // = 105
        float h    = BatteryH;              // = 6

        _batteryBacking = HudKit.Img(parent, "BatteryBacking", HudKit.Ink);
        SetAnchored(_batteryBacking.rectTransform,
            xOff - FillInset, yPos - FillInset,
            w + FillInset * 2f, h + FillInset * 2f);
        HudKit.Skew(_batteryBacking, SkewFor(h + FillInset * 2f));

        _batteryFill = HudKit.Img(parent, "BatteryFill", batteryColor);
        SetFillImage(_batteryFill, xOff, yPos, w, h);
        HudKit.Skew(_batteryFill, SkewFor(h));
    }

    // Weight chip: 88×11, font 7.5, above health bar with 4-unit gap.
    // y = HealthY + HealthH + 4 = 23+14+4 = 41
    // xOff matches health row stagger = 16
    private void BuildWeightChip(Transform parent, float yPos)
    {
        float xOff = 2 * RowStagger; // align with health row = 16

        _weightBacking = HudKit.Img(parent, "WeightBacking", HudKit.InkSoft);
        SetAnchored(_weightBacking.rectTransform, xOff, yPos, 88f, 11f);
        HudKit.Skew(_weightBacking, SkewFor(11f));

        _weightLabel = HudKit.Text(parent, "WeightLabel", 7.5f, HudKit.OffWhite,
            TextAlignmentOptions.Left, FontStyles.Italic);
        var rt = _weightLabel.rectTransform;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.zero;
        rt.pivot            = Vector2.zero;
        rt.sizeDelta        = new Vector2(88f, 11f);
        rt.anchoredPosition = new Vector2(xOff + 3f, yPos + 1f);
        _weightLabel.text   = "0.0 / 40.0 kg";
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Positions a RectTransform with bottom-left anchor at exact canvas reference coordinates.</summary>
    private static void SetAnchored(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.zero;
        rt.pivot            = Vector2.zero;
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>Configures an Image as a horizontal-filled bar covering the given rect.</summary>
    private static void SetFillImage(Image img, float x, float y, float w, float h)
    {
        SetAnchored(img.rectTransform, x, y, w, h);
        img.type       = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillAmount = 1f;
    }
}
