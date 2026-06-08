using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-RIGHT HUD list of ACTIVE quests (title + current step / live progress like "Collect 3 — 1/3"),
/// plus a brief "✓ Completed" line for quests that just finished so you can see what you wrapped up
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
    [Tooltip("How long a finished quest stays on the tracker as '✓ Completed' before clearing.")]
    [SerializeField] private float _completedDisplaySeconds = 6f;

    [Header("=== Style ===")]
    [SerializeField] private int   _titleFontSize = 15;
    [SerializeField] private int   _stepFontSize  = 12;
    [SerializeField] private Color _titleColor    = new Color(1.00f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color _stepColor     = new Color(0.86f, 0.86f, 0.86f, 1f);
    [SerializeField] private Color _completedColor = new Color(0.45f, 0.85f, 0.45f, 1f);

    private RectTransform _panel;
    private readonly List<GameObject> _entries = new List<GameObject>();

    // Completed-quest notifications: questId → Time.time when it should clear.
    private readonly Dictionary<string, float> _completedUntil = new Dictionary<string, float>();
    private readonly HashSet<string> _seenCompleted = new HashSet<string>();
    private readonly List<string> _expiredScratch = new List<string>();
    private bool _initialized;

    private void Awake()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[QuestTrackerUI] Must live inside a Canvas.", this); return; }
        BuildPanel(canvas.transform);
    }

    private void OnEnable() { Subscribe(); Refresh(); }
    private void Start()    { Subscribe(); Refresh(); } // WSM / QuestManager may init after our OnEnable

    private void OnDisable()
    {
        if (_questManager != null) _questManager.OnQuestsChanged -= Refresh;
        if (WorldStateManager.Instance != null) WorldStateManager.Instance.OnStateChanged -= OnWsmChanged;
    }

    // Idempotent (-= then +=) so calling from both OnEnable and Start can't double-subscribe.
    private void Subscribe()
    {
        if (_questManager != null)
        {
            _questManager.OnQuestsChanged -= Refresh;
            _questManager.OnQuestsChanged += Refresh;
        }
        if (WorldStateManager.Instance != null)
        {
            WorldStateManager.Instance.OnStateChanged -= OnWsmChanged;
            WorldStateManager.Instance.OnStateChanged += OnWsmChanged;
        }
    }

    // Live progress: refresh when an objective counter (objective.*) or the credits total (economy.*)
    // changes, so "1/3 → 2/3" and "0/150 → 75/150" update on screen.
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

        if (_questManager == null) return;

        float now = Time.time;

        // Detect newly-completed quests and schedule their "Completed" notification. On the very first
        // refresh we only SEED the seen-set (so already-finished quests from a load don't all pop up).
        foreach (var quest in _questManager.Quests)
        {
            if (quest == null) continue;
            if (_questManager.GetStatus(quest) != QuestStatus.Succeeded) continue;
            if (_seenCompleted.Add(quest.QuestId) && _initialized)
                _completedUntil[quest.QuestId] = now + _completedDisplaySeconds;
        }
        _initialized = true;

        // Active quests with their current step / live progress.
        foreach (var quest in _questManager.Quests)
        {
            if (quest == null || _questManager.GetStatus(quest) != QuestStatus.Active) continue;
            BuildEntry(quest.title, TrackerLineFor(quest), _titleColor, _stepColor);
        }

        // Recently-completed quests (kept briefly so you can see what you finished).
        foreach (var quest in _questManager.Quests)
        {
            if (quest == null) continue;
            if (_completedUntil.TryGetValue(quest.QuestId, out float until) && now < until &&
                _questManager.GetStatus(quest) == QuestStatus.Succeeded)
                BuildEntry(quest.title, "✓ Completed", _completedColor, _completedColor);
        }

        _panel.gameObject.SetActive(_entries.Count > 0);
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

                // Objective asset → live progress ("Collect 3 — 1/3"). A typed description override wins.
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

    // ── UI construction ──────────────────────────────────────────────────────

    private void BuildPanel(Transform canvas)
    {
        var panelGO = new GameObject("QuestTrackerPanel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGO.transform.SetParent(canvas, false);

        _panel               = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin     = new Vector2(1f, 1f);
        _panel.anchorMax     = new Vector2(1f, 1f);
        _panel.pivot         = new Vector2(1f, 1f);
        _panel.sizeDelta     = new Vector2(_width, 0f);
        _panel.anchoredPosition = new Vector2(-_paddingRight, -_paddingTop);

        var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 10f;
        vlg.childAlignment     = TextAnchor.UpperRight;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = panelGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _panel.gameObject.SetActive(false);
    }

    private void BuildEntry(string title, string step, Color titleColor, Color stepColor)
    {
        var entryGO = new GameObject("QuestEntry", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        entryGO.transform.SetParent(_panel, false);

        var vlg = entryGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 1f;
        vlg.childAlignment     = TextAnchor.UpperRight;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = entryGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var titleText = NewText("Title", entryGO.transform, _titleFontSize, titleColor);
        titleText.fontStyle = FontStyles.Bold;
        titleText.text      = string.IsNullOrEmpty(title) ? "Quest" : title;

        if (!string.IsNullOrEmpty(step))
        {
            var stepText = NewText("Step", entryGO.transform, _stepFontSize, stepColor);
            stepText.text = step;
        }

        _entries.Add(entryGO);
    }

    private static TextMeshProUGUI NewText(string goName, Transform parent, int size, Color color)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t           = go.GetComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.color         = color;
        t.alignment     = TextAlignmentOptions.TopRight;
        t.raycastTarget = false;
        return t;
    }
}
