using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Bottom-RIGHT HUD panel — current weapon name and ammo in
/// Persona-style angular layout.
///
/// Setup:
///   1. Add this component to ANY child GameObject inside a Canvas.
///   2. Assign WeaponManager in the Inspector.
///
/// The script creates its own RectTransform panel as a direct child of the nearest
/// Canvas so positioning is always relative to the screen edge, regardless of
/// where in the hierarchy this component lives.
///
/// All sizes are in canvas REFERENCE UNITS (Canvas ScaleWithScreenSize 800x600, match width).
/// Panel occupies a 180x70 ref-unit block at the bottom-right corner.
/// </summary>
public sealed class WeaponHUD : MonoBehaviour
{
    [Header("=== References ===")]
    public WeaponManager weaponManager;

    [Header("=== Position ===")]
    public float paddingRight  = 20f;
    public float paddingBottom = 20f;

    [Header("=== Style ===")]
    public Color ammoColor = new Color(0.949f, 0.941f, 0.902f, 1f);  // HudKit.OffWhite
    public Color nameColor = new Color(1.00f, 0.478f, 0.102f, 1f);   // HudKit.Orange

    // ── Layout constants (reference units) ────────────────────────────────
    // Panel: 180x70, pivot bottom-right, anchored to canvas bottom-right.
    //
    // All children use anchorMin=anchorMax=(1,0), pivot=(1,0) so that
    // anchoredPosition=(0,0) places the child's right edge at the panel's right edge.
    //
    // From bottom up:
    //   Nameplate:   y=0,  w=170, h=18  (ink chip, skew 6, name font 10 uppercase Orange)
    //   NameAccent:  pivot(1,1) at y=0, h=1.5, w=170  (green underline below nameplate)
    //   AmmoLine:    y=22, w=180, h=30  (single rich-text label, font 26 bold-italic, BottomRight)
    //   PipStrip:    y=54, h=9          (pip 4x9, gap 2, skew 3; right-to-left from pivot)
    //   ReloadHint:  y=54, w=180, h=9  (alongside/instead of pips while reloading)
    //
    private const float PanelW       = 180f;
    private const float PanelH       = 70f;
    private const float NameplateW   = 170f;
    private const float NameplateH   = 18f;
    private const float NameAccentH  = 1.5f;
    private const float AmmoBottom   = 22f;
    private const float AmmoH        = 30f;
    private const float PipBottom    = 54f;
    private const float PipH         = 9f;
    private const float PipW         = 4f;
    private const float PipGap       = 2f;
    private const int   MaxPipCount  = 24;  // hide strip above this (24*4+23*2=142 <= 180)
    private const float PipSkew      = 3f;
    private const float Skew         = 6f;  // nameplate

    // ── Runtime refs ───────────────────────────────────────────────────────
    private RectTransform    _panel;

    // Ammo display — single rich-text label: "bullets<size=55%><alpha=#AA> / capacity"
    private TextMeshProUGUI  _bulletText;
    private TextMeshProUGUI  _reloadHint;       // "RELOAD" blink / "RELOADING"

    // Nameplate
    private Image            _nameBacking;
    private TextMeshProUGUI  _nameText;
    private Image            _nameAccentLine;

    // Pip strip
    private RectTransform    _pipRoot;
    private Image[]          _pips = System.Array.Empty<Image>();

    // ── Cached state ───────────────────────────────────────────────────────
    private GunController    _trackedGun;
    private int              _lastBullets   = -1;
    private int              _lastCapacity  = -1;
    private bool             _lastReloading = false;
    private string           _lastWeaponName;
    private bool             _isReloading;

    // ── Tweens ────────────────────────────────────────────────────────────
    private Tween  _dangerPulse;
    private Tween  _reloadingPulse;
    private Tween  _reloadHintBlink;
    private bool   _wasDanger;

    // ── Awake ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Force palette — scene prefab still carries pre-overhaul serialized colors.
        ammoColor = HudKit.OffWhite;
        nameColor = HudKit.Orange;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[WeaponHUD] Must be inside a Canvas."); return; }

        // Root panel — bottom-right anchor, pivot bottom-right
        var panelGO        = new GameObject("WeaponHUDPanel", typeof(RectTransform));
        panelGO.transform.SetParent(canvas.transform, false);
        _panel             = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin   = new Vector2(1f, 0f);
        _panel.anchorMax   = new Vector2(1f, 0f);
        _panel.pivot       = new Vector2(1f, 0f);
        _panel.sizeDelta   = new Vector2(PanelW, PanelH);
        _panel.anchoredPosition = new Vector2(-12f, 10f);

        Transform pt = panelGO.transform;

        // ── Nameplate (bottom of panel) ────────────────────────────────────
        // anchorMin=anchorMax=(1,0), pivot=(1,0): right edge pinned to panel's right edge.
        // anchoredPosition=(0,0) places right edge flush with panel right, bottom edge at panel bottom.
        _nameBacking = HudKit.Img(pt, "NameBacking", HudKit.Ink);
        var nbRT = _nameBacking.rectTransform;
        nbRT.anchorMin        = new Vector2(1f, 0f);
        nbRT.anchorMax        = new Vector2(1f, 0f);
        nbRT.pivot            = new Vector2(1f, 0f);
        nbRT.sizeDelta        = new Vector2(NameplateW, NameplateH);
        nbRT.anchoredPosition = new Vector2(0f, 0f);
        HudKit.Skew(_nameBacking, Skew);

        // Weapon name text inside nameplate, right-aligned, font 10, uppercase Orange
        _nameText = HudKit.Text(pt, "WeaponName", 10f, nameColor,
            TextAlignmentOptions.Right, FontStyles.Bold | FontStyles.Italic);
        var ntRT = _nameText.rectTransform;
        ntRT.anchorMin        = new Vector2(1f, 0f);
        ntRT.anchorMax        = new Vector2(1f, 0f);
        ntRT.pivot            = new Vector2(1f, 0f);
        ntRT.sizeDelta        = new Vector2(NameplateW - 6f, NameplateH);
        ntRT.anchoredPosition = new Vector2(-3f, 1f);

        // Thin green accent line under/at bottom of nameplate.
        // pivot (1,1) at y=0: top edge of accent sits at panel bottom, so accent extends downward.
        _nameAccentLine = HudKit.Img(pt, "NameAccent", HudKit.Green);
        var naRT = _nameAccentLine.rectTransform;
        naRT.anchorMin        = new Vector2(1f, 0f);
        naRT.anchorMax        = new Vector2(1f, 0f);
        naRT.pivot            = new Vector2(1f, 1f);
        naRT.sizeDelta        = new Vector2(NameplateW, NameAccentH);
        // -3: the skewed chip's BOTTOM edge is shifted left by half the skew (6/2),
        // so the underline must shift with it to line up with the chip's bottom corner.
        naRT.anchoredPosition = new Vector2(-3f, 0f);

        // ── Ammo count row (single rich-text label) ────────────────────────
        // y=AmmoBottom=22, h=AmmoH=30. Font 26 bold-italic, BottomRight alignment.
        // Rich text: "{bullets}<size=55%><alpha=#AA> / {capacity}"
        // The capacity fraction is rendered smaller and dimmer within the same label,
        // so the two numbers can never overlap or stack.
        _bulletText = HudKit.Text(pt, "AmmoLine", 26f, ammoColor,
            TextAlignmentOptions.BottomRight, FontStyles.Bold | FontStyles.Italic);
        _bulletText.richText = true;
        var btRT = _bulletText.rectTransform;
        btRT.anchorMin        = new Vector2(1f, 0f);
        btRT.anchorMax        = new Vector2(1f, 0f);
        btRT.pivot            = new Vector2(1f, 0f);
        btRT.sizeDelta        = new Vector2(PanelW, AmmoH);
        btRT.anchoredPosition = new Vector2(0f, AmmoBottom);

        // Reload hint (font 9, right-aligned) — sits at y=54 alongside pips
        _reloadHint = HudKit.Text(pt, "ReloadHint", 9f, HudKit.OrangeHot,
            TextAlignmentOptions.Right, FontStyles.Bold | FontStyles.Italic);
        var rhRT = _reloadHint.rectTransform;
        rhRT.anchorMin        = new Vector2(1f, 0f);
        rhRT.anchorMax        = new Vector2(1f, 0f);
        rhRT.pivot            = new Vector2(1f, 0f);
        rhRT.sizeDelta        = new Vector2(PanelW, PipH);
        rhRT.anchoredPosition = new Vector2(0f, PipBottom);
        _reloadHint.text      = "";

        // ── Pip strip (bottom-CENTER of the screen) ────────────────────────
        // Stays a child of the panel so it follows panel SetActive, but is offset to
        // screen center: canvas ref width is ALWAYS 800 (match-width scaler), panel
        // right edge sits at canvas x=788 → anchoredPosition.x = 400-788 = -388 puts
        // the strip's center-pivot at screen center. y=60 → canvas y=70, above the gun.
        var pipGO = new GameObject("PipStrip", typeof(RectTransform));
        pipGO.transform.SetParent(pt, false);
        _pipRoot             = pipGO.GetComponent<RectTransform>();
        _pipRoot.anchorMin   = new Vector2(1f, 0f);
        _pipRoot.anchorMax   = new Vector2(1f, 0f);
        _pipRoot.pivot       = new Vector2(0.5f, 0f);
        _pipRoot.sizeDelta   = new Vector2(PanelW, PipH);
        _pipRoot.anchoredPosition = new Vector2(-388f, 60f);
    }

    // ── Enable / Disable — event wiring ───────────────────────────────────

    private void OnEnable()
    {
        if (_panel != null) _panel.gameObject.SetActive(true);

        // Re-hook the current gun's reload events if we already know it
        if (_trackedGun != null)
        {
            _trackedGun.OnReloadStarted  += HandleReloadStarted;
            _trackedGun.OnReloadFinished += HandleReloadFinished;
        }
    }

    private void OnDisable()
    {
        UnhookGun(_trackedGun);
        _dangerPulse?.Kill();
        _reloadingPulse?.Kill();
        _reloadHintBlink?.Kill();
        _dangerPulse     = null;
        _reloadingPulse  = null;
        _reloadHintBlink = null;

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

    // ── Reload event handlers ──────────────────────────────────────────────

    private void HandleReloadStarted(GunController gun)
    {
        _isReloading = true;
        ShowReloadingState(true);
    }

    private void HandleReloadFinished(GunController gun)
    {
        _isReloading = false;
        ShowReloadingState(false);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (weaponManager == null) return;

        var gun = weaponManager.CurrentWeapon;

        // Weapon change detection
        if (gun != _trackedGun)
        {
            UnhookGun(_trackedGun);
            _trackedGun    = gun;
            _lastBullets   = -1;
            _lastCapacity  = -1;
            _lastWeaponName = null;
            _isReloading   = gun != null && gun.IsReloading; // sync in case events missed
            _lastReloading = _isReloading;

            if (gun != null)
            {
                gun.OnReloadStarted  += HandleReloadStarted;
                gun.OnReloadFinished += HandleReloadFinished;
            }

            // Nameplate slide-in from right + flash
            if (_nameBacking != null)
            {
                float targetX = 0f;
                var nbPos = _nameBacking.rectTransform.anchoredPosition;
                _nameBacking.rectTransform.anchoredPosition = new Vector2(targetX - 60f, nbPos.y);
                DOTween.Kill(_nameBacking.rectTransform);
                _nameBacking.rectTransform.DOAnchorPosX(targetX, 0.22f)
                    .SetEase(Ease.OutCubic)
                    .SetLink(_nameBacking.gameObject);
                _nameText.DOColor(HudKit.OrangeHot, 0.07f)
                    .SetLink(_nameText.gameObject)
                    .OnComplete(() =>
                        _nameText.DOColor(nameColor, 0.22f)
                            .SetLink(_nameText.gameObject));
            }
        }

        if (gun == null)
        {
            SetNoWeapon();
            return;
        }

        // Defensive poll for reload state in case events were missed
        bool polledReloading = gun.IsReloading;
        if (polledReloading != _lastReloading)
        {
            _isReloading   = polledReloading;
            _lastReloading = polledReloading;
            ShowReloadingState(_isReloading);
        }

        int bullets  = gun.BulletsRemaining;
        int capacity = gun.MagazineCapacity;

        // Weapon name
        string wName = gun.weaponData != null ? gun.weaponData.itemName : gun.name;
        if (wName != _lastWeaponName)
        {
            _lastWeaponName  = wName;
            _nameText.text   = wName.ToUpper();
        }

        // Capacity change → rebuild pips and rebuild ammo label
        if (capacity != _lastCapacity)
        {
            _lastCapacity = capacity;
            RebuildPips(capacity);
            // Force ammo label rebuild on next bullet-count check by invalidating cache
            _lastBullets = -1;
        }

        // Bullet count change
        if (bullets != _lastBullets)
        {
            bool decreased = _lastBullets >= 0 && bullets < _lastBullets;
            _lastBullets = bullets;

            if (capacity > 0)
                _bulletText.text = $"{bullets}<size=55%><alpha=#AA> / {capacity}";
            else
                _bulletText.text = "--";

            // Punch scale on shot
            if (decreased && _bulletText != null)
                _bulletText.rectTransform
                    .DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.18f, 6, 0.5f)
                    .SetLink(_bulletText.gameObject);

            // Update pip colors
            UpdatePipColors(bullets, capacity);

            // Reload hint (empty mag)
            bool isEmpty = capacity > 0 && bullets == 0;
            if (isEmpty && !_isReloading)
                StartReloadBlink();
            else if (!_isReloading)
                StopReloadBlink();
        }

        // Danger state (<= 20% ammo)
        bool isDanger = capacity > 0 && (float)bullets / capacity <= 0.20f && bullets > 0;
        if (isDanger != _wasDanger)
        {
            _wasDanger = isDanger;
            _dangerPulse?.Kill();
            if (isDanger)
            {
                _bulletText.color = HudKit.Danger;
                _dangerPulse = _bulletText
                    .DOFade(0.4f, 0.45f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine)
                    .SetLink(_bulletText.gameObject);
            }
            else
            {
                _bulletText.color = ammoColor;
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetNoWeapon()
    {
        if (_lastBullets == -999) return; // already in no-weapon state
        _lastBullets   = -999;
        _lastCapacity  = -1;
        _lastWeaponName = null;
        _bulletText.text   = "--";
        _nameText.text     = "";
        _reloadHint.text   = "";
        SetPipsVisible(false);
        StopReloadBlink();
        _dangerPulse?.Kill();
        _bulletText.color = new Color(HudKit.OffWhite.r, HudKit.OffWhite.g, HudKit.OffWhite.b, 0.35f);
    }

    private void RebuildPips(int capacity)
    {
        // Destroy old pips
        foreach (var p in _pips)
            if (p != null) Object.Destroy(p.gameObject);

        // Hide pips if capacity is 0 or exceeds MaxPipCount (would overflow 180 ref-unit panel)
        if (capacity <= 0 || capacity > MaxPipCount)
        {
            _pips = System.Array.Empty<Image>();
            SetPipsVisible(false);
            return;
        }

        SetPipsVisible(true);
        _pips = new Image[capacity];

        // Pips are centered as a group around _pipRoot's center (strip sits at
        // screen bottom-center). Pip 0 is leftmost; total strip width is computed
        // so the row stays centered for any capacity.
        float totalW = capacity * PipW + (capacity - 1) * PipGap;
        for (int i = 0; i < capacity; i++)
        {
            var pip = HudKit.Img(_pipRoot, $"Pip{i}", HudKit.Orange);
            var rt  = pip.rectTransform;
            rt.anchorMin        = new Vector2(0.5f, 0f);
            rt.anchorMax        = new Vector2(0.5f, 0f);
            rt.pivot            = new Vector2(0f, 0f);
            rt.sizeDelta        = new Vector2(PipW, PipH);
            rt.anchoredPosition = new Vector2(-totalW * 0.5f + i * (PipW + PipGap), 0f);
            HudKit.Skew(pip, PipSkew);
            _pips[i] = pip;
        }
    }

    private void UpdatePipColors(int bullets, int capacity)
    {
        if (_pips == null || _pips.Length == 0) return;
        for (int i = 0; i < _pips.Length; i++)
        {
            if (_pips[i] == null) continue;
            bool filled    = i < bullets;
            _pips[i].color = filled ? HudKit.Orange
                                    : new Color(HudKit.Ink.r, HudKit.Ink.g, HudKit.Ink.b, 0.85f);
        }
    }

    private void SetPipsVisible(bool visible)
    {
        if (_pipRoot != null)
            _pipRoot.gameObject.SetActive(visible);
    }

    private void ShowReloadingState(bool reloading)
    {
        _reloadingPulse?.Kill();
        _reloadHintBlink?.Kill();

        if (reloading)
        {
            _reloadHint.text  = "RELOADING";
            _reloadHint.color = HudKit.Green;
            _reloadingPulse = _reloadHint
                .DOFade(0.3f, 0.4f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetLink(_reloadHint.gameObject);

            // Dim the count while reloading
            _bulletText.DOFade(0.35f, 0.15f).SetLink(_bulletText.gameObject);
        }
        else
        {
            _reloadHint.text = "";
            _bulletText.DOFade(1f, 0.15f).SetLink(_bulletText.gameObject);
        }
    }

    private void StartReloadBlink()
    {
        _reloadHintBlink?.Kill();
        _reloadHint.text  = "RELOAD";
        _reloadHint.color = HudKit.OrangeHot;
        _reloadHintBlink  = _reloadHint
            .DOFade(0f, 0.45f)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine)
            .SetLink(_reloadHint.gameObject);
    }

    private void StopReloadBlink()
    {
        _reloadHintBlink?.Kill();
        _reloadHintBlink = null;
        if (_reloadHint != null) _reloadHint.text = "";
    }

    private void UnhookGun(GunController gun)
    {
        if (gun == null) return;
        gun.OnReloadStarted  -= HandleReloadStarted;
        gun.OnReloadFinished -= HandleReloadFinished;
    }
}
