using UnityEngine;

/// <summary>
/// Immutable quest definition. Contains only data — no runtime state.
///
/// SIMPLE QUESTS  : Fill objectives[]. Leave outcomes[] empty.
///   → Quest succeeds when all mandatory objectives pass.
///   → Quest fails when any globalFailCondition passes.
///
/// BRANCHING QUESTS : Fill outcomes[] instead of objectives[].
///   → QuestManager checks outcomes in array order; first match wins.
///   → Each outcome carries its own terminalStatus + downstream quests.
///
/// EVALUATION ORDER (while Active):
///   1. globalFailConditions  — any true → Failed (no branch)
///   2. outcomes[]            — first matching outcome triggers (if any defined)
///   2b. objectives[]         — all mandatory met → Succeeded (fallback when no outcomes)
///
/// Create via Assets › Create › Quest › Quest Definition.
/// </summary>
[CreateAssetMenu(menuName = "Quest/Quest Definition", fileName = "QuestSO_")]
public class QuestSO : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────────────────────

    [Tooltip("Stable unique id — auto-generated, never edit by hand.")]
    [SerializeField, HideInInspector] private string _questId;

    public string QuestId => _questId;

    [Header("Display")]
    public string title;
    [TextArea] public string description;

    [Tooltip("Short one-line summary for the on-screen quest tracker (top-right HUD), e.g. " +
             "'Find the 2A bunker door and return your findings'. The tracker prefers the current " +
             "objective's description; this is the fallback when objectives have no description.")]
    public string trackerText;

    // ── Activation ────────────────────────────────────────────────────────────

    [Header("Activation")]
    [Tooltip("All listed quests must be Succeeded before this one can activate.")]
    public QuestSO[] requiredQuests;

    [Tooltip("Additional WSM condition that must pass before activation. Leave wsmKey empty to ignore.")]
    public QuestConditionDefinition activeCondition;

    [Tooltip("When this quest activates, immediately cancel these quests. Use for faction/mutually-exclusive pairs.")]
    public QuestSO[] cancelOnActivate;

    // ── Objectives (simple path) ──────────────────────────────────────────────

    [Header("Objectives  —  simple path (leave empty if using Outcomes)")]
    [Tooltip("All mandatory objectives must pass for the quest to succeed. Optional objectives don't block completion.")]
    public QuestObjectiveDefinition[] objectives;

    // ── Outcomes (branching path) ─────────────────────────────────────────────

    [Header("Outcomes  —  branching path (overrides Objectives when non-empty)")]
    [Tooltip("Checked in array order while Active. First matching outcome fires. Leave empty to use Objectives instead.")]
    public QuestOutcomeDefinition[] outcomes;

    // ── Fail conditions ───────────────────────────────────────────────────────

    [Header("Fail Conditions  —  any one triggers immediate failure (no branching)")]
    [Tooltip("Use for simple fail gates: escort died, player detected, timer ran out. " +
             "If the failure needs to open another questline, use an Outcome with terminalStatus=Failed instead.")]
    public QuestConditionDefinition[] globalFailConditions;

    // ── Fail propagation ──────────────────────────────────────────────────────

    [Header("Fail Propagation")]
    [Tooltip("When this quest fails, immediately fail these quests too. Cycle-safe.")]
    public QuestSO[] failsWithMe;

    // ── Expiration ────────────────────────────────────────────────────────────

    [Header("Expiration")]
    [Tooltip("Quest soft-expires (status → Expired, not Failed) after this many seconds while Active.")]
    public bool canExpire;
    [Tooltip("Seconds of active time before expiry. Only used when canExpire = true.")]
    public float expirationSeconds;

    // ── Repeatable ────────────────────────────────────────────────────────────

    [Header("Repeatable / Contract")]
    [Tooltip("When true, the quest can reset and run again once resetCondition passes.")]
    public bool isRepeatable;
    [Tooltip("WSM condition that resets the quest runtime (e.g. new in-game day). " +
             "Does NOT clear WSM flags — reset any gameplay counters via UnityEvents instead.")]
    public QuestConditionDefinition resetCondition;

    // ── Editor validation ─────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Auto-generate stable GUID on first creation only.
        // Duplicating an asset (Ctrl+D) copies the GUID — use Reset Quest ID from the context menu to fix.
        if (string.IsNullOrEmpty(_questId))
        {
            _questId = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // Warn immediately if another loaded QuestSO shares this ID (catches duplicates on save/select)
        var all = Resources.FindObjectsOfTypeAll<QuestSO>();
        foreach (var other in all)
        {
            if (other != this && other._questId == _questId)
            {
                Debug.LogError($"[QuestSO] '{name}' has the same Quest ID as '{other.name}'! " +
                               "Right-click this asset in the Project window → Reset Quest ID.", this);
                break;
            }
        }

        if (canExpire && expirationSeconds <= 0f)
            Debug.LogWarning($"[QuestSO] '{name}': canExpire is true but expirationSeconds <= 0.", this);

        if (isRepeatable && (resetCondition == null || string.IsNullOrEmpty(resetCondition.wsmKey)))
            Debug.LogWarning($"[QuestSO] '{name}': isRepeatable is true but no resetCondition is set.", this);

        if (outcomes != null)
        {
            foreach (var o in outcomes)
            {
                if (o != null && string.IsNullOrEmpty(o.label))
                    Debug.LogWarning($"[QuestSO] '{name}': an Outcome has no label — add one so you can tell them apart in the inspector.", this);
            }
        }
    }

    /// <summary>
    /// Call this after duplicating a QuestSO asset (Ctrl+D) to give the copy a fresh unique ID.
    /// Right-click the asset → Reset Quest ID.
    /// </summary>
    [UnityEditor.MenuItem("CONTEXT/QuestSO/Reset Quest ID (use after duplicating)")]
    private static void RegenerateQuestIdMenu(UnityEditor.MenuCommand cmd)
    {
        var so = cmd.context as QuestSO;
        if (so == null) return;
        so._questId = System.Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(so);
        Debug.Log($"[QuestSO] '{so.name}': Quest ID regenerated → {so._questId}");
    }
#endif
}
