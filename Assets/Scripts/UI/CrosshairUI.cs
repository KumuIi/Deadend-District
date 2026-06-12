using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural four-tick crosshair for Deadend District.
/// Self-builds under the nearest Canvas (or creates its own ScreenSpaceOverlay at sortingOrder 40).
/// Driven by WeaponManager / GunController state; hides when gameplay is blocked or dialogue is open.
/// No per-frame allocations — all state tracked via cached fields.
///
/// When a private canvas is created it receives the same ScaleWithScreenSize (800×600, match-width)
/// scaler as the HUD canvas so crosshair ticks are visually consistent at any resolution.
/// Tick positions use anchoredPosition offsets from a centre-anchored root — these are reference
/// units and require NO scaleFactor division (unlike screen-pixel sources such as WorldToScreenPoint).
/// </summary>
public sealed class CrosshairUI : MonoBehaviour
{
    [Header("=== References ===")]
    [SerializeField] public WeaponManager weaponManager; // auto-found in Start if null

    [Header("=== Style ===")]
    [SerializeField] private float _pixelsPerDegree = 50f;
    [SerializeField] private float _tickLength      = 14f;
    [SerializeField] private float _tickThickness   = 2f;
    [SerializeField] private float _baseGap         = 8f;  // gap from centre at rest
    [SerializeField] private Color _color           = new Color(1.00f, 0.478f, 0.102f, 1f); // HudKit.Orange

    // ── Runtime ────────────────────────────────────────────────────────────

    private Canvas       _canvas;
    private GameObject   _canvasGO;
    private CanvasGroup  _group;

    // Tick rects: 0=top, 1=bottom, 2=left, 3=right
    private RectTransform[] _ticks;
    private Image[]          _tickImages;

    // Center dot
    private Image _dot;

    // Shot bloom synthesis
    private float _bloomOffset;      // extra gap added by shot punch, decays to 0
    private int   _lastBullets = -1; // detect BulletsRemaining decrease

    // Aim state
    private float _aimAlpha;         // smoothed 0→1 while aiming

    // Canvas-group alpha (visibility)
    private float _targetGroupAlpha;

    // ── Unity ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Force palette/metrics — prefab carries pre-overhaul serialized values.
        _color         = HudKit.Orange;
        _tickLength    = 7f;
        _tickThickness = 1.5f;
        _baseGap       = 5f;
        // bloom +6 is applied in LateUpdate via _bloomOffset additive shot punch

        // Find or create canvas
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            _canvas   = parentCanvas;
            _canvasGO = null; // not owned by us
        }
        else
        {
            _canvasGO = new GameObject("CrosshairCanvas");
            _canvasGO.transform.SetParent(null);
            _canvas              = _canvasGO.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;

            // Match HUD canvas scaler so crosshair sizes are consistent at any resolution.
            // Tick positions are reference-unit offsets from a centred root, so NO scaleFactor
            // division is needed here — unlike InteractionHighlightUI which reads screen pixels.
            var scaler = _canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 600f);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0f; // match width
        }

        _group = _canvas.gameObject.GetComponent<CanvasGroup>();
        if (_group == null) _group = _canvas.gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        BuildCrosshair();
        _group.alpha = 0f;
    }

    private void OnEnable()
    {
        // Guard null: may be called before Awake if the GameObject was disabled at prefab time
        if (_canvasGO != null) _canvasGO.SetActive(true);
    }

    private void OnDisable()
    {
        if (_canvasGO != null) _canvasGO.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    private void Start()
    {
        if (weaponManager == null)
            weaponManager = FindFirstObjectByType<WeaponManager>();
    }

    private void LateUpdate()
    {
        // ── Visibility gate ───────────────────────────────────────────────
        bool hasWeapon = weaponManager != null && weaponManager.CurrentWeapon != null;
        bool hidden    = !hasWeapon || GameInputState.GameplayBlocked || DialogueUI.IsOpen;

        _targetGroupAlpha = hidden ? 0f : 1f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, _targetGroupAlpha, 8f * Time.deltaTime);

        if (hidden)
        {
            _lastBullets = -1;
            return;
        }

        GunController gun = weaponManager.CurrentWeapon;

        // ── Shot bloom detection ──────────────────────────────────────────
        int bullets = gun.BulletsRemaining;
        if (_lastBullets >= 0 && bullets < _lastBullets)
        {
            // A shot was fired — punch bloom offset by +6 ref units
            _bloomOffset += 6f;
        }
        _lastBullets = bullets;

        // Decay bloom offset
        _bloomOffset = Mathf.MoveTowards(_bloomOffset, 0f, _bloomOffset * 8f * Time.deltaTime + 40f * Time.deltaTime);
        if (_bloomOffset < 0.01f) _bloomOffset = 0f;

        // ── Aim state ─────────────────────────────────────────────────────
        float aimTarget = gun.IsAiming ? 1f : 0f;
        _aimAlpha = Mathf.MoveTowards(_aimAlpha, aimTarget, 6f * Time.deltaTime);

        // ── Movement expand ───────────────────────────────────────────────
        float moveExpand = 0f;
        PlayerMotor motor = weaponManager.PlayerMotor;
        if (motor != null && motor.IsMoving)
            moveExpand = 4f;

        // ── Compute final gap ─────────────────────────────────────────────
        float aimGapMult = Mathf.Lerp(1f, 0.35f, _aimAlpha); // tighten gap while aiming
        float gap        = (_baseGap + _bloomOffset + moveExpand) * aimGapMult;

        // Tick alpha: fade to 30% while aiming
        float tickAlpha = Mathf.Lerp(1f, 0.30f, _aimAlpha);
        Color tickColor = new Color(_color.r, _color.g, _color.b, tickAlpha);
        for (int i = 0; i < 4; i++)
            _tickImages[i].color = tickColor;

        // ── Position ticks (reference units, pivot at tick centre) ────────
        // 0=top, 1=bottom, 2=left, 3=right
        _ticks[0].anchoredPosition = new Vector2(0f,  gap + _tickLength * 0.5f);
        _ticks[1].anchoredPosition = new Vector2(0f, -(gap + _tickLength * 0.5f));
        _ticks[2].anchoredPosition = new Vector2(-(gap + _tickLength * 0.5f), 0f);
        _ticks[3].anchoredPosition = new Vector2( gap + _tickLength * 0.5f,  0f);
    }

    // ── Build helpers ──────────────────────────────────────────────────────

    private void BuildCrosshair()
    {
        // Root container, centred in the canvas
        var rootGO    = new GameObject("CrosshairRoot", typeof(RectTransform));
        rootGO.transform.SetParent(_canvas.transform, false);
        var rootRT    = rootGO.GetComponent<RectTransform>();
        rootRT.anchorMin        = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax        = new Vector2(0.5f, 0.5f);
        rootRT.pivot            = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta        = Vector2.zero;
        rootRT.anchoredPosition = Vector2.zero;

        _ticks      = new RectTransform[4];
        _tickImages = new Image[4];

        // top
        _ticks[0] = BuildTick(rootGO.transform, "Tick_Top",   new Vector2(_tickThickness, _tickLength),
            skew: false);
        // bottom
        _ticks[1] = BuildTick(rootGO.transform, "Tick_Bottom",new Vector2(_tickThickness, _tickLength),
            skew: false);
        // left (horizontal — apply slight skew for style)
        _ticks[2] = BuildTick(rootGO.transform, "Tick_Left",  new Vector2(_tickLength, _tickThickness),
            skew: true);
        // right (horizontal — apply slight skew for style)
        _ticks[3] = BuildTick(rootGO.transform, "Tick_Right", new Vector2(_tickLength, _tickThickness),
            skew: true);

        for (int i = 0; i < 4; i++)
            _tickImages[i] = _ticks[i].GetComponent<Image>();

        // Center dot — 1.5 ref units, OffWhite, low alpha
        var dotGO    = new GameObject("CenterDot", typeof(RectTransform), typeof(Image));
        dotGO.transform.SetParent(rootGO.transform, false);
        _dot = dotGO.GetComponent<Image>();
        _dot.sprite        = HudKit.White;
        _dot.color         = new Color(HudKit.OffWhite.r, HudKit.OffWhite.g, HudKit.OffWhite.b, 0.55f);
        _dot.raycastTarget = false;
        var dotRT          = dotGO.GetComponent<RectTransform>();
        dotRT.anchorMin        = new Vector2(0.5f, 0.5f);
        dotRT.anchorMax        = new Vector2(0.5f, 0.5f);
        dotRT.pivot            = new Vector2(0.5f, 0.5f);
        dotRT.sizeDelta        = new Vector2(1.5f, 1.5f);
        dotRT.anchoredPosition = Vector2.zero;
    }

    private RectTransform BuildTick(Transform parent, string tickName, Vector2 size, bool skew)
    {
        var go  = new GameObject(tickName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var img           = go.GetComponent<Image>();
        img.sprite        = HudKit.White;
        img.color         = _color;
        img.raycastTarget = false;

        var rt             = go.GetComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 0.5f);
        rt.anchorMax       = new Vector2(0.5f, 0.5f);
        rt.pivot           = new Vector2(0.5f, 0.5f);
        rt.sizeDelta       = size;
        rt.anchoredPosition = Vector2.zero;

        if (skew) HudKit.Skew(img, 5f);

        return rt;
    }
}
