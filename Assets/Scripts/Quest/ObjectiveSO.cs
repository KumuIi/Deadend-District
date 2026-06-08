using UnityEngine;

/// <summary>What kind of thing the player must do. Every type still resolves to ONE done flag.</summary>
public enum ObjectiveType
{
    ReachZone,          // walk into an ObjectiveTrigger volume
    CollectItems,       // pick up N items (optionally of one type)
    ReachCurrency,      // have >= N credits
    UseRechargeStation, // use a recharge station N times (1 = "recharge your battery")
    KillEnemies,        // kill N enemies (optionally of one team)
    SurviveSeconds,     // spend N seconds in a raid
    UnlockAnyDoor,      // unlock N doors (key OR shortcut)
    UnlockAnyShortcut,  // open N shortcut doors
    ExtractRaid,        // successfully extract from a raid
    CustomFlag          // escape hatch: done when a chosen WSM bool is true
}

/// <summary>When progress accrues. Quest objectives should usually only count while their quest is active.</summary>
public enum ObjectiveAccrualMode
{
    WhileQuestActive, // only counts while an owning quest is Active (correct for quests)
    Lifetime          // always counts (achievement-style)
}

/// <summary>
/// A reusable "objective" asset — the drag-and-drop building block of a quest. Pick a type, set a
/// number, name it; drop it into a quest's objective list (and, for ReachZone, onto an
/// <see cref="ObjectiveTrigger"/>). The <see cref="ObjectiveService"/> does all the counting/timing
/// and flips this objective's single bool fact (<see cref="DoneKey"/>) when satisfied — so quests
/// never deal with counts, comparisons, or raw WSM keys.
///
/// Create via Assets › Create › Quest › Objective.
/// </summary>
[CreateAssetMenu(menuName = "Quest/Objective", fileName = "Objective_")]
public class ObjectiveSO : ScriptableObject
{
    [SerializeField, HideInInspector] private string _objectiveId;
    public string Id => _objectiveId;

    [Tooltip("Shown in the quest tracker, e.g. 'Find the laboratory' or 'Collect 3 items'.")]
    public string displayName;

    public ObjectiveType type = ObjectiveType.ReachZone;

    [Tooltip("Target amount: items to collect, enemies to kill, credits to reach, seconds to survive, " +
             "doors/shortcuts to unlock, recharges to do. Ignored by Reach Zone / Custom (treated as 1).")]
    public int amount = 1;

    [Tooltip("Counts only while an owning quest is Active (recommended), or always (Lifetime / achievement-style).")]
    public ObjectiveAccrualMode accrual = ObjectiveAccrualMode.WhileQuestActive;

    [Tooltip("Clear this objective's progress at the start of each raid. Use for in-raid goals " +
             "(collect 3 this run, survive 5 minutes).")]
    public bool resetEachRun;

    [Header("Collect Items")]
    [Tooltip("Only count pickups of this exact item. Leave empty to count ANY item.")]
    public ItemSO itemFilter;

    [Header("Kill Enemies")]
    [Tooltip("Only count kills of this team (Guard / Monster…). Uncheck to count ANY enemy.")]
    public bool useTeamFilter;
    public TeamId killTeam = TeamId.Guard;

    [Header("Custom Flag")]
    [Tooltip("Escape hatch: this objective is done when this WSM bool is true.")]
    [WsmKey] public string customKey;

    // ── Derived facts ─────────────────────────────────────────────────────────
    public string DoneKey  => $"objective.{_objectiveId}.done";
    public string CountKey => $"objective.{_objectiveId}.count";

    /// <summary>The number that means "done" for count-style types (always ≥ 1).</summary>
    public int Target => Mathf.Max(1, amount);

    public bool IsCountType =>
        type == ObjectiveType.CollectItems   || type == ObjectiveType.KillEnemies       ||
        type == ObjectiveType.SurviveSeconds || type == ObjectiveType.UnlockAnyDoor      ||
        type == ObjectiveType.UnlockAnyShortcut || type == ObjectiveType.UseRechargeStation;

    /// <summary>Tracker text, e.g. 'Collect items  1/3' or 'Survive  02:30 / 05:00'.</summary>
    public string ProgressText(int current)
    {
        string label = string.IsNullOrEmpty(displayName) ? name : displayName;
        switch (type)
        {
            case ObjectiveType.SurviveSeconds:
                return $"{label}  {Fmt(current)} / {Fmt(Target)}";
            case ObjectiveType.CollectItems:
            case ObjectiveType.KillEnemies:
            case ObjectiveType.UnlockAnyDoor:
            case ObjectiveType.UnlockAnyShortcut:
            case ObjectiveType.UseRechargeStation:
            case ObjectiveType.ReachCurrency:
                return $"{label}  {current}/{Target}";
            default: // ReachZone, CustomFlag
                return label;
        }
    }

    private static string Fmt(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(_objectiveId))
        {
            _objectiveId = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // Catch Ctrl+D duplicates (they copy the hidden id and would share the same WSM keys).
        var all = Resources.FindObjectsOfTypeAll<ObjectiveSO>();
        foreach (var o in all)
            if (o != this && o._objectiveId == _objectiveId)
            {
                Debug.LogError($"[ObjectiveSO] '{name}' shares its ID with '{o.name}'! " +
                               "Right-click this asset → Reset Objective ID (after duplicating).", this);
                break;
            }
    }

    [UnityEditor.MenuItem("CONTEXT/ObjectiveSO/Reset Objective ID (use after duplicating)")]
    private static void ResetIdMenu(UnityEditor.MenuCommand cmd)
    {
        var so = cmd.context as ObjectiveSO;
        if (so == null) return;
        so._objectiveId = System.Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(so);
        Debug.Log($"[ObjectiveSO] '{so.name}': ID regenerated.");
    }
#endif
}
