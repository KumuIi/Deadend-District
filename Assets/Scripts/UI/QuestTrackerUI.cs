using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Top-RIGHT HUD list of ACTIVE quests (title + current step / live progress like "Collect 3 -- 1/3"),
/// plus a brief "// COMPLETE" line for quests that just finished so you can see what you wrapped up
/// before it clears.
///
/// Self-builds its panel under the nearest Canvas (like PlayerHUD / WeaponHUD). Refreshes on
/// QuestManager.OnQuestsChanged and on objective/economy WSM changes (for live counters).
///
/// Setup: add to any child of a Canvas, assign QuestManager in the Inspector.
/// </summary>
public sealed class QuestTrackerUI : MonoBehaviour
{
    [Header("=== References ===")]
    [SerializeField] private QuestManager _questManager;

    [Header("=== Position ===")]
    [SerializeField] private float _paddingRight = 20f;
    [SerializeField] private float _paddingTop   = 20f;
    [SerializeField] private float _width        = 280f;

    [Header("=== Completed ===")]
    [Tooltip("How long a finished quest stays on the tracker as '// COMPLETE' before clearing.")]
    [SerializeField] private float _completedDisplaySeconds = 6f;

    [Header("=== Style ===")]
    [SerializeField] private int   _titleFontSize = 15;
    [SerializeField] private int   _stepFontSize  = 12;
    [SerializeField] private Color _titleColor    = new Color(1.00f, 0.478f, 0.102f, 1f); // HudKit.Orange
    [SerializeField] private Color _stepColor     = new Color(0.949f, 0.941f, 0.902f, 1f); // HudKit.OffWhite
    [SerializeField] private Color _completedColor = new Color(0.549f, 0.910f, 0.188f, 1f); // HudKit.Green

    // ── Panel / header ────────────────────────────────────────────────────
    private RectTransform    _panel;
    private GameObject       _headerGO;     // OBJECTIVES chip -- shown only when entries > 0
    private Image            _headerChip;
    private Image            _headerAccent;

    private readonly List<GameObject> _entries = new List<GameObject>();

    // Completed-quest notifications: questId -> Time.time when it should clear.
    private readonly Dictionary<string, float> _completedUntil = new Dictionary<string, float>();
    private readonly List<string> _expiredScratch = new List<string>();

    // Animation state: track which quest ids have already played their intro / toast
    private readonly HashSet<string> _shownActive    = new HashSet<string>();
    private readonly HashSet<string> _toastFlashed   = new HashSet<string>();

    // Inventory-suppression: hide while inventory is open (overlay canvas always renders above camera canvas)
    private bool _suppressed;

    // First-refresh guard: skip animations on the very first Refresh after Awake so that
    // quests restored from a save never play slide-in animations.
    private bool _firstRefreshDone;

    // Save/scene loads fire several Refreshes in a row (quest restore, then WSM writes), and can
    // run while Time.timeScale is 0 — a scaled tween then freezes at its start offset (entry stuck
    // half-slid until the next rebuild). Suppress animations for a short unscaled window after
    // (re)enable so loads always settle instantly at the rest position.
    private float _animReadyTime;
    private bool  AnimationsAllowed => _firstRefreshDone && Time.unscaledTime >= _animReadyTime;

    // ── Unity ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Force palette/metrics -- prefab carries pre-overhaul serialized values.
        _width         = 190f;
        _titleFontSize = 11;
        _stepFontSize  = 9;
        _titleColor    = HudKit.Orange;
        _stepColor     = HudKit.OffWhite;
        _completedColor = HudKit.Green;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[QuestTrackerUI] Must live inside a Canvas.", this); return; }
        BuildPanel(canvas.transform);
    }

    private void OnEnable()  { _animReadyTime = Time.unscaledTime + 1f; Subscribe(); Refresh(); }
    private void Start()     { Subscribe(); Refresh(); } // WSM / QuestManager may init after our OnEnable

    private void OnDisable()
    {
        if (_questManager != null) _questManager.OnQuestsChanged -= Refresh;
        QuestManager.OnAnyQuestTransition -= OnQuestTransition;
        if (WorldStateManager.Instance != null) WorldStateManager.Instance.OnStateChanged -= OnWsmChanged;
        InventoryUI.OnPlayerInventoryToggled -= OnInventoryToggled;

        // Hide panel and header while disabled
        if (_panel != null)      _panel.gameObject.SetActive(false);
        if (_headerGO != null)   _headerGO.SetActive(false);
    }

    private void OnDestroy()
    {
        // Panel and header are canvas children, not children of this component -- destroy explicitly.
        if (_panel != null)    Destroy(_panel.gameObject);
        if (_headerGO != null) Destroy(_headerGO);
    }

    // Idempotent (-= then +=) so calling from both OnEnable and Start can't double-subscribe.
    private void Subscribe()
    {
        if (_questManager != null)
        {
            _questManager.OnQuestsChanged -= Refresh;
            _questManager.OnQuestsChanged += Refresh;
        }
        // Drive the green "// COMPLETE" toast off real status TRANSITIONS only -- never off a Refresh
        // diff. A save load restores Succeeded statuses by direct assignment (no transition), so loading
        // never re-pops quests you already finished; only in-play completions toast.
        QuestManager.OnAnyQuestTransition -= OnQuestTransition;
        QuestManager.OnAnyQuestTransition += OnQuestTransition;
        if (WorldStateManager.Instance != null)
        {
            WorldStateManager.Instance.OnStateChanged -= OnWsmChanged;
            WorldStateManager.Instance.OnStateChanged += OnWsmChanged;
        }
        // Inventory hide: overlay canvas always renders above camera-space inventory canvas.
        InventoryUI.OnPlayerInventoryToggled -= OnInventoryToggled;
        InventoryUI.OnPlayerInventoryToggled += OnInventoryToggled;
    }

    private void OnInventoryToggled(bool open)
    {
        _suppressed = open;
        UpdateVisibility();
    }

    // Centralized visibility: respects both suppression (inventory open) and entry count.
    private void UpdateVisibility()
    {
        bool show = !_suppressed && _entries.Count > 0;
        if (_panel != null)    _panel.gameObject.SetActive(show);
        if (_headerGO != null) _headerGO.SetActive(show);
    }

    // A quest just changed status in live play. Schedule the brief completed toast on success.
    private void OnQuestTransition(QuestSO quest, QuestStatus status)
    {
        if (quest == null) return;
        if (status == QuestStatus.Succeeded)
            _completedUntil[quest.QuestId] = Time.time + _completedDisplaySeconds;
        Refresh();
    }

    // Live progress: refresh when an objective counter (objective.*) or the credits total (economy.*)
    // changes, so "1/3 -> 2/3" and "0/150 -> 75/150" update on screen.
    private void OnWsmChanged(string key, WorldStateValue oldV, WorldStateValue newV)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (key.StartsWith("objective.", System.StringComparison.Ordinal) ||
            key.StartsWith("economy.",   System.StringComparison.Ordinal))
            Refresh();
    }

    private void Update()
    {
        if (_completedUntil.Count == 0) return;
        float now = Time.time;
        _expiredScratch.Clear();
        foreach (var kv in _completedUntil) if (now >= kv.Value) _expiredScratch.Add(kv.Key);
        if (_expiredScratch.Count == 0) return;
        foreach (var id in _expiredScratch) _completedUntil.Remove(id);
        Refresh();
    }

    private void Refresh()
    {
        if (_panel == null) return;

        foreach (var e in _entries) if (e != null) Destroy(e);
        _entries.Clear();

        if (_questManager == null) { UpdateVisibility(); return; }

        float now = Time.time;

        // Active quests with their current step / live progress.
        foreach (var quest in _questManager.Quests)
        {
            if (quest == null || _questManager.GetStatus(quest) != QuestStatus.Active) continue;
            BuildEntry(quest.QuestId, quest.title, TrackerLineFor(quest), _titleColor, _stepColor, isCompleted: false);
        }

        // Recently-completed quests (kept briefly so you can see what you finished).
        foreach (var quest in _questManager.Quests)
        {
            if (quest == null) continue;
            if (_completedUntil.TryGetValue(quest.QuestId, out float until) && now < until &&
                _questManager.GetStatus(quest) == QuestStatus.Succeeded)
                BuildEntry(quest.QuestId, quest.title, "// COMPLETE", _completedColor, _completedColor, isCompleted: true);
        }

        // Mark that at least one Refresh has now run; subsequent calls may animate new entries.
        _firstRefreshDone = true;

        UpdateVisibility();
    }

    /// <summary>Current step text: first revealed, incomplete, mandatory objective; else a quest-level fallback.</summary>
    private string TrackerLineFor(QuestSO quest)
    {
        var objs = quest.objectives;
        if (objs != null)
        {
            for (int i = 0; i < objs.Length; i++)
            {
                var o = objs[i];
                if (o == null || o.optional) continue;
                if (!_questManager.IsObjectiveRevealed(quest, i)) continue;
                if (_questManager.IsObjectiveComplete(quest, i))  continue;

                // Objective asset -> live progress ("Collect 3 -- 1/3"). A typed description override wins.
                if (o.objective != null)
                {
                    var svc = ObjectiveService.Instance;
                    if (svc != null)
                    {
                        var (cur, _) = svc.GetProgress(o.objective);
                        return o.objective.ProgressText(cur);
                    }
                    if (!string.IsNullOrEmpty(o.objective.displayName)) return o.objective.displayName;
                }
                if (!string.IsNullOrEmpty(o.description)) return o.description;
                if (o.condition != null && !string.IsNullOrEmpty(o.condition.description))
                    return o.condition.description;
                return Fallback(quest);
            }
        }
        return Fallback(quest);
    }

    private static string Fallback(QuestSO quest) =>
        !string.IsNullOrEmpty(quest.trackerText) ? quest.trackerText : quest.description;

    // ── UI construction ────────────────────────────────────────────────────

    private void BuildPanel(Transform canvas)
    {
        // ── Header chip (OBJECTIVES label) -- sibling of the panel, above it ──
        _headerGO = new GameObject("QuestHeader", typeof(RectTransform));
        _headerGO.transform.SetParent(canvas, false);

        var headerRT         = _headerGO.GetComponent<RectTransform>();
        headerRT.anchorMin   = new Vector2(1f, 1f);
        headerRT.anchorMax   = new Vector2(1f, 1f);
        headerRT.pivot       = new Vector2(1f, 1f);
        headerRT.sizeDelta   = new Vector2(100f, 16f);   // 100x16 reference units
        headerRT.anchoredPosition = new Vector2(-_paddingRight, -_paddingTop);

        // Green accent line: exactly 50x1.5 ref units, anchored to LEFT edge of chip, extending left.
        // anchorMin/anchorMax = (0, 0.5) pins to left-center of the chip.
        // pivot = (1, 0.5) means the RIGHT edge of the accent is the anchor point, so it grows leftward.
        // sizeDelta = (50, 1.5) is the rect size in reference units.
        // anchoredPosition = (0, 0) puts the pivot (right edge) exactly at the chip's left-center.
        _headerAccent         = HudKit.Img(_headerGO.transform, "HeaderAccent", HudKit.Green);
        var accentRT          = _headerAccent.rectTransform;
        accentRT.anchorMin    = new Vector2(0f, 0.5f);
        accentRT.anchorMax    = new Vector2(0f, 0.5f);
        accentRT.pivot        = new Vector2(1f, 0.5f);
        accentRT.sizeDelta    = new Vector2(50f, 1.5f);
        accentRT.anchoredPosition = Vector2.zero;

        // Skewed orange chip -- fills the header GO
        _headerChip           = HudKit.Img(_headerGO.transform, "HeaderChip", HudKit.Orange);
        var chipRT            = _headerChip.rectTransform;
        chipRT.anchorMin      = Vector2.zero;
        chipRT.anchorMax      = Vector2.one;
        chipRT.offsetMin      = Vector2.zero;
        chipRT.offsetMax      = Vector2.zero;
        HudKit.Skew(_headerChip, 5f);

        // "OBJECTIVES" label -- font 9.5, dark ink text
        var headerLabel       = HudKit.Text(_headerGO.transform, "HeaderLabel", 9.5f, HudKit.Ink,
            TextAlignmentOptions.Right, FontStyles.Bold | FontStyles.Italic);
        var labelRT           = headerLabel.rectTransform;
        labelRT.anchorMin     = Vector2.zero;
        labelRT.anchorMax     = Vector2.one;
        labelRT.offsetMin     = new Vector2(4f, 0f);
        labelRT.offsetMax     = new Vector2(-6f, 0f);
        headerLabel.text      = "OBJECTIVES";

        _headerGO.SetActive(false);

        // ── Main entry list panel ─────────────────────────────────────────────
        var panelGO = new GameObject("QuestTrackerPanel", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGO.transform.SetParent(canvas, false);

        _panel               = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin     = new Vector2(1f, 1f);
        _panel.anchorMax     = new Vector2(1f, 1f);
        _panel.pivot         = new Vector2(1f, 1f);
        _panel.sizeDelta     = new Vector2(_width, 0f);
        // Position below the header chip (16px chip + 4px gap)
        _panel.anchoredPosition = new Vector2(-_paddingRight, -_paddingTop - 16f - 4f);

        var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 6f;
        vlg.childAlignment     = TextAnchor.UpperRight;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = panelGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _panel.gameObject.SetActive(false);
    }

    /// <param name="questId">Used for animation gating.</param>
    /// <param name="isCompleted">True for the toast row -- plays flash only when newly toasted.</param>
    private void BuildEntry(string questId, string title, string step,
        Color titleColor, Color stepColor, bool isCompleted)
    {
        // ── Entry root: layout-group child, NO tweening ever applied here ────
        // The VerticalLayoutGroup controls this object's position entirely.
        // We never touch its anchoredPosition so the layout group is never fought.
        var entryGO = new GameObject("QuestEntry", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        entryGO.transform.SetParent(_panel, false);

        var vlg = entryGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 1f;
        vlg.childAlignment     = TextAnchor.UpperRight;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding            = new RectOffset(0, 8, 2, 2); // right inset for accent bar

        var fitter = entryGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ── Inner Content RectTransform ───────────────────────────────────────
        // Stretch-fills the entry root via anchors so its resting anchoredPosition is (0,0).
        // ALL visuals (backing, accent, texts) live here. Slide-in tweens target Content only
        // so the layout-managed root is never disturbed.
        //
        // Height propagation: the text elements are direct children of Content and are also
        // direct children of the VLG chain (Content has its own VLG+CSF that drives height).
        // The entry root's CSF reads the preferred height from the entry's own VLG which in turn
        // reads Content's preferred height -- so the height chain is preserved.
        var contentGO = new GameObject("Content", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(entryGO.transform, false);

        var contentRT         = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin   = Vector2.zero;
        contentRT.anchorMax   = Vector2.one;
        contentRT.offsetMin   = Vector2.zero;
        contentRT.offsetMax   = Vector2.zero;
        // Resting anchoredPosition is (0,0) because it stretch-fills; the slide tween is therefore safe.

        var contentVlg = contentGO.GetComponent<VerticalLayoutGroup>();
        contentVlg.spacing            = 1f;
        contentVlg.childAlignment     = TextAnchor.UpperRight;
        contentVlg.childControlWidth  = true;
        contentVlg.childControlHeight = true;
        contentVlg.childForceExpandWidth  = true;
        contentVlg.childForceExpandHeight = false;
        contentVlg.padding            = new RectOffset(0, 0, 0, 0);

        var contentFitter = contentGO.GetComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ── Ink backing chip -- stretch to fill Content, NOT layout-controlled ──
        var backing = HudKit.Img(contentGO.transform, "EntryBacking", HudKit.InkSoft);
        var backRT  = backing.rectTransform;
        backRT.anchorMin  = Vector2.zero;
        backRT.anchorMax  = Vector2.one;
        backRT.offsetMin  = new Vector2(-2f, -2f);
        backRT.offsetMax  = new Vector2( 2f,  2f);
        // Raise alpha so backing is actually visible
        backing.color = new Color(HudKit.InkSoft.r, HudKit.InkSoft.g, HudKit.InkSoft.b, 0.85f);
        HudKit.Skew(backing, 4f);
        // Must NOT be layout-controlled -- back behind text
        var backLE = backing.gameObject.AddComponent<LayoutElement>();
        backLE.ignoreLayout = true;
        backing.transform.SetAsFirstSibling();

        // ── Vertical accent bar on the RIGHT edge of Content, NOT layout-controlled ──
        // anchorMin=(1,0) anchorMax=(1,1): pins to the right side, full height stretch.
        // pivot=(1,0.5): the bar's right edge sits exactly on Content's right edge.
        // sizeDelta=(2.5,0): width=2.5 ref units; height=0 means fully stretched by anchors.
        var accentBar = HudKit.Img(contentGO.transform, "EntryAccent", HudKit.Green);
        var abRT      = accentBar.rectTransform;
        abRT.anchorMin      = new Vector2(1f, 0f);
        abRT.anchorMax      = new Vector2(1f, 1f);
        abRT.pivot          = new Vector2(1f, 0.5f);
        abRT.sizeDelta      = new Vector2(2.5f, 0f);
        // +8 pushes the bar past Content's right edge into the entry's right padding,
        // clear of the right-aligned text (which previously collided with it).
        abRT.anchoredPosition = new Vector2(8f, 0f);
        HudKit.Skew(accentBar, 4f); // same lean as the backing chip so the bar follows the slant
        // Must NOT be layout-controlled
        var accentLE = accentBar.gameObject.AddComponent<LayoutElement>();
        accentLE.ignoreLayout = true;

        // ── Title text ────────────────────────────────────────────────────────
        FontStyles titleStyle = FontStyles.Bold | FontStyles.Italic;
        if (isCompleted) titleStyle |= FontStyles.Strikethrough;

        var titleText       = NewText("Title", contentGO.transform, _titleFontSize, titleColor, titleStyle);
        titleText.text      = string.IsNullOrEmpty(title) ? "Quest" : title;

        // ── Step text ─────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(step))
        {
            var stepText = NewText("Step", contentGO.transform, _stepFontSize, stepColor,
                FontStyles.Normal);
            stepText.text = step;
        }

        // Move accent bar to last sibling within Content
        accentBar.transform.SetAsLastSibling();

        _entries.Add(entryGO);

        // ── Animations ─────────────────────────────────────────────────────
        // We NEVER tween the entryGO (layout-managed root).
        // Instead we tween the inner Content RectTransform whose resting anchoredPosition
        // is (0,0) because it stretch-fills the root. The slide x from +30 -> 0 is safe
        // because Content's anchor system ensures (0,0) is always its correct rest position.
        //
        // The _firstRefreshDone guard prevents save-load from triggering animations:
        // the very first Refresh after Awake sets _firstRefreshDone = true AFTER building
        // all entries, so any entries built during that first pass see _firstRefreshDone=false.

        if (!isCompleted)
        {
            bool isNew = _shownActive.Add(questId); // true when first seen
            if (isNew && AnimationsAllowed)
            {
                // Fade in via CanvasGroup (layout-safe)
                var cg       = contentGO.AddComponent<CanvasGroup>();
                cg.alpha     = 0f;
                cg.blocksRaycasts = false;
                DOTween.To(() => cg.alpha, x => cg.alpha = x, 1f, 0.3f)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true) // unscaled time — never freezes at timeScale 0
                    .SetLink(contentGO);

                // Slide Content from +30 to 0 (layout-safe: Content's correct rest x is always 0).
                // OnKill snaps to rest no matter how the tween ends (complete, killed early,
                // interrupted by a load) — Content can NEVER be stranded at the offset.
                contentRT.anchoredPosition = new Vector2(30f, contentRT.anchoredPosition.y);
                DOTween.To(
                    () => contentRT.anchoredPosition.x,
                    x  => contentRT.anchoredPosition = new Vector2(x, contentRT.anchoredPosition.y),
                    0f, 0.3f)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetLink(contentGO)
                    .OnKill(() =>
                    {
                        if (contentRT != null)
                            contentRT.anchoredPosition = new Vector2(0f, contentRT.anchoredPosition.y);
                    });

                // Brief OrangeHot title flash
                titleText.color = HudKit.OrangeHot;
                titleText.DOColor(titleColor, 0.4f)
                    .SetDelay(0.1f)
                    .SetUpdate(true)
                    .SetLink(contentGO);
            }
        }
        else
        {
            // Completion toast flash -- only once per quest
            bool isNewToast = _toastFlashed.Add(questId);
            if (isNewToast && AnimationsAllowed)
            {
                titleText.color = HudKit.OrangeHot;
                titleText.DOColor(HudKit.Green, 0.5f)
                    .SetUpdate(true)
                    .SetLink(contentGO);
            }
        }
    }

    private static TextMeshProUGUI NewText(string goName, Transform parent, int size, Color color,
        FontStyles style = FontStyles.Bold | FontStyles.Italic)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t                    = go.GetComponent<TextMeshProUGUI>();
        t.fontSize               = size;
        t.color                  = color;
        t.alignment              = TextAlignmentOptions.TopRight;
        t.fontStyle              = style;
        t.raycastTarget          = false;
        t.textWrappingMode       = TextWrappingModes.Normal;
        t.overflowMode           = TextOverflowModes.Overflow;
        return t;
    }
}
