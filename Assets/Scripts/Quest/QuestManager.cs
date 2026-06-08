using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic quest evaluator. Reads WorldStateManager facts — never contains quest-specific logic.
///
/// Evaluation order per Active quest:
///   1. globalFailConditions — any true → Failed (no branching)
///   2a. outcomes[]          — first matching outcome fires (if any defined)
///   2b. objectives[]        — all mandatory met → Succeeded (used when no outcomes defined)
///
/// Activation: all requiredQuests Succeeded AND activeCondition passes (or is empty).
/// On activation: cancelOnActivate quests are set to Cancelled.
/// On fail: failsWithMe propagation (cycle-safe via visited set).
/// Expiry: tracked in Update; canExpire + expirationSeconds → status Expired.
/// Repeatable: runtime reset when resetCondition passes (WSM flags are NOT cleared).
/// </summary>
public class QuestManager : MonoBehaviour, ISaveable
{
    public static QuestManager Instance { get; private set; }

    [SerializeField] private List<QuestSO> _quests = new List<QuestSO>();

    /// <summary>
    /// Fires whenever any quest's status changes — activate / succeed / fail / expire / cancel /
    /// repeatable reset / save-load. UI (e.g. QuestTrackerUI) subscribes to refresh without polling.
    /// </summary>
    public event Action OnQuestsChanged;

    /// <summary>All quests registered on this manager (read-only). Pair with <see cref="GetStatus(QuestSO)"/>.</summary>
    public IReadOnlyList<QuestSO> Quests => _quests;

    private void NotifyChanged() => OnQuestsChanged?.Invoke();

    private readonly Dictionary<string, QuestRuntimeState> _runtime
        = new Dictionary<string, QuestRuntimeState>();

    // ── Singleton + lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        InitRuntime();
        SaveSystem.Instance?.Register(this);

        if (WorldStateManager.Instance == null)
        {
            Debug.LogWarning("[QuestManager] WorldStateManager missing — quest tracking disabled.");
            return;
        }
        WorldStateManager.Instance.OnStateChanged += OnWSMChanged;
        EvaluateAll();
    }

    private void OnEnable()
    {
        SaveSystem.Instance?.Register(this);
        if (WorldStateManager.Instance != null)
        {
            WorldStateManager.Instance.OnStateChanged -= OnWSMChanged;
            WorldStateManager.Instance.OnStateChanged += OnWSMChanged;
        }
    }

    private void OnDisable()
    {
        SaveSystem.Instance?.Unregister(this);
        if (WorldStateManager.Instance != null)
            WorldStateManager.Instance.OnStateChanged -= OnWSMChanged;
    }

    private void Update()
    {
        foreach (var quest in _quests)
        {
            if (quest == null || !_runtime.TryGetValue(quest.QuestId, out var r)) continue;
            if (r.status != QuestStatus.Active) continue;

            // Expiry countdown
            if (quest.canExpire && quest.expirationSeconds > 0f)
            {
                r.activeTimeElapsed += Time.deltaTime;
                if (r.activeTimeElapsed >= quest.expirationSeconds)
                    Transition(quest, r, QuestStatus.Expired, -1);
            }

            // Expiry only — repeatable reset is handled in the second loop below
        }

        // Repeatable: check reset condition for any finished quest (Succeeded, Expired, Failed, Cancelled)
        foreach (var quest in _quests)
        {
            if (quest == null || !quest.isRepeatable) continue;
            if (!_runtime.TryGetValue(quest.QuestId, out var r)) continue;
            if (r.status == QuestStatus.Inactive || r.status == QuestStatus.Active) continue;
            if (quest.resetCondition == null || string.IsNullOrEmpty(quest.resetCondition.wsmKey)) continue;
            if (quest.resetCondition.Evaluate())
            {
                // Reset runtime to Inactive FIRST so EvaluateAll (triggered by WSM clear) can re-activate
                r.ResetForRepeat();
                ClearQuestWSMFlags(quest);
                NotifyChanged();
            }
        }
    }

    // ── ISaveable ────────────────────────────────────────────────────────────

    public string      SaveId    => "quest.manager";
    public string      SaveType  => "QuestManager";
    public RunScopeTag SaveScope => RunScopeTag.Profile;

    public object CaptureSaveData()
    {
        var dto = new QuestManagerDTO();
        foreach (var kv in _runtime)
        {
            dto.ids.Add(kv.Key);
            dto.statuses.Add((int)kv.Value.status);
            dto.objectives.Add(PackBools(kv.Value.objectivesComplete));
            dto.revealed.Add(PackBools(kv.Value.objectivesRevealed));
            dto.fails.Add(PackBools(kv.Value.failConditionsTriggered));
            dto.outcomeIndices.Add(kv.Value.resolvedOutcomeIndex);
            dto.activeTimeElapsed.Add(kv.Value.activeTimeElapsed);
        }
        return dto;
    }

    public void RestoreSaveData(object data)
    {
        var dto = JsonUtility.FromJson<QuestManagerDTO>((string)data);
        if (dto == null) return;

        InitRuntime();
        for (int i = 0; i < dto.ids.Count; i++)
        {
            if (!_runtime.TryGetValue(dto.ids[i], out var r)) continue;
            int rawStatus = i < dto.statuses.Count ? dto.statuses[i] : 0;
            r.status = Enum.IsDefined(typeof(QuestStatus), rawStatus)
                ? (QuestStatus)rawStatus : QuestStatus.Inactive;
            UnpackBools(i < dto.objectives.Count    ? dto.objectives[i]    : "", r.objectivesComplete);
            UnpackBools(i < dto.revealed.Count      ? dto.revealed[i]      : "", r.objectivesRevealed);
            UnpackBools(i < dto.fails.Count         ? dto.fails[i]         : "", r.failConditionsTriggered);
            r.resolvedOutcomeIndex = i < dto.outcomeIndices.Count    ? dto.outcomeIndices[i]    : -1;
            r.activeTimeElapsed    = i < dto.activeTimeElapsed.Count ? dto.activeTimeElapsed[i] : 0f;
        }
        // Do NOT call EvaluateAll() — WorldStateSaveAdapter may not have run yet.
        // But DO refresh listeners so the tracker reflects the restored statuses.
        NotifyChanged();
    }

    // ── WSM event ────────────────────────────────────────────────────────────

    private void OnWSMChanged(string key, WorldStateValue oldVal, WorldStateValue newVal)
    {
        EvaluateAll();
    }

    // ── Evaluation ───────────────────────────────────────────────────────────

    private void EvaluateAll()
    {
        foreach (var quest in _quests)
        {
            if (quest == null || string.IsNullOrEmpty(quest.QuestId)) continue;
            if (!_runtime.TryGetValue(quest.QuestId, out var r)) continue;
            EvaluateQuest(quest, r);
        }
    }

    private void EvaluateQuest(QuestSO quest, QuestRuntimeState r)
    {
        switch (r.status)
        {
            case QuestStatus.Inactive:
                if (ShouldActivate(quest))
                    Transition(quest, r, QuestStatus.Active, -1);
                break;

            case QuestStatus.Active:
                // 1 — global fail conditions (simple, no branch)
                EvaluateFailConditions(quest.globalFailConditions, r.failConditionsTriggered);
                if (r.AnyFailTriggered())
                {
                    Transition(quest, r, QuestStatus.Failed, -1);
                    break;
                }

                // 2a — branching outcomes (if any defined)
                if (quest.outcomes != null && quest.outcomes.Length > 0)
                {
                    for (int i = 0; i < quest.outcomes.Length; i++)
                    {
                        var outcome = quest.outcomes[i];
                        if (outcome == null || outcome.condition == null) continue;
                        if (outcome.condition.Evaluate())
                        {
                            Transition(quest, r, TerminalToQuestStatus(outcome.terminalStatus), i);
                            return;
                        }
                    }
                    break;
                }

                // 2b — simple objectives (all mandatory must pass)
                EvaluateObjectives(quest.objectives, r);
                if (r.AllMandatoryObjectivesMet(quest.objectives))
                    Transition(quest, r, QuestStatus.Succeeded, -1);
                break;
        }
    }

    private bool ShouldActivate(QuestSO quest)
    {
        // All required quests must be succeeded
        if (quest.requiredQuests != null)
        {
            foreach (var req in quest.requiredQuests)
            {
                if (req == null) continue;
                if (!_runtime.TryGetValue(req.QuestId, out var rs) || rs.status != QuestStatus.Succeeded)
                    return false;
            }
        }
        // Optional WSM gate
        if (quest.activeCondition != null && !string.IsNullOrEmpty(quest.activeCondition.wsmKey))
            return quest.activeCondition.Evaluate();
        return true;
    }

    private void EvaluateFailConditions(QuestConditionDefinition[] conditions, bool[] results)
    {
        if (conditions == null || conditions.Length == 0) return;
        for (int i = 0; i < conditions.Length && i < results.Length; i++)
            results[i] = conditions[i]?.Evaluate() ?? false;
    }

    private void EvaluateObjectives(QuestObjectiveDefinition[] defs, QuestRuntimeState r)
    {
        if (defs == null) return;
        for (int i = 0; i < defs.Length && i < r.objectivesComplete.Length; i++)
        {
            var def = defs[i];
            if (def == null) continue;

            // An Objective asset (drag-and-drop) wins: it owns a single done flag the ObjectiveService
            // flips. Otherwise fall back to the manually-authored WSM condition.
            r.objectivesComplete[i] = def.objective != null
                ? (WorldStateManager.Instance != null && WorldStateManager.Instance.GetBool(def.objective.DoneKey))
                : (def.condition?.Evaluate() ?? false);

            // Reveal check
            if (!r.objectivesRevealed[i] && def.hidden)
            {
                if (r.objectivesComplete[i])
                    r.objectivesRevealed[i] = true; // auto-reveal on completion
                else if (def.revealCondition != null && !string.IsNullOrEmpty(def.revealCondition.wsmKey))
                    r.objectivesRevealed[i] = def.revealCondition.Evaluate();
            }
            else if (!def.hidden)
            {
                r.objectivesRevealed[i] = true; // non-hidden objectives always revealed
            }
        }
    }

    private void Transition(QuestSO quest, QuestRuntimeState r, QuestStatus next, int outcomeIndex)
    {
        r.status               = next;
        r.resolvedOutcomeIndex = outcomeIndex;
        if (next == QuestStatus.Active) r.activeTimeElapsed = 0f;

        string outcomeLabel = outcomeIndex >= 0 && quest.outcomes != null && outcomeIndex < quest.outcomes.Length
            ? $" [{quest.outcomes[outcomeIndex].label}]" : "";
        Debug.Log($"[QuestManager] '{quest.title}' → {next}{outcomeLabel}");

        // Write WSM flag for this quest's new status
        if (WorldStateManager.Instance != null)
        {
            string flag = next switch
            {
                QuestStatus.Active    => $"quest.{quest.QuestId}.active",
                QuestStatus.Succeeded => $"quest.{quest.QuestId}.succeeded",
                QuestStatus.Failed    => $"quest.{quest.QuestId}.failed",
                QuestStatus.Expired   => $"quest.{quest.QuestId}.expired",
                QuestStatus.Cancelled => $"quest.{quest.QuestId}.cancelled",
                _                     => null
            };
            if (flag != null) WorldStateManager.Instance.SetBool(flag, true);
        }

        // Notify BEFORE the activation early-return below, so the tracker refreshes on activate too.
        NotifyChanged();

        // On activation: cancel mutually exclusive quests
        if (next == QuestStatus.Active)
        {
            if (quest.cancelOnActivate != null)
                foreach (var q in quest.cancelOnActivate)
                    CancelQuest(q);

            // Immediately evaluate objectives/outcomes now that we're active
            EvaluateQuest(quest, r);
            return;
        }

        // On any terminal status: fire outcome downstream effects
        if (outcomeIndex >= 0 && quest.outcomes != null && outcomeIndex < quest.outcomes.Length)
        {
            var outcome = quest.outcomes[outcomeIndex];
            if (outcome.activateQuests != null)
                foreach (var q in outcome.activateQuests) TryActivateQuest(q);
            if (outcome.cancelQuests != null)
                foreach (var q in outcome.cancelQuests) CancelQuest(q);
            if (outcome.failQuests != null)
                foreach (var q in outcome.failQuests) PropagateFailure(q, new HashSet<string>());
        }

        // On fail: propagate failsWithMe — single visited set shared across the whole chain
        if (next == QuestStatus.Failed && quest.failsWithMe != null)
        {
            var visited = new HashSet<string> { quest.QuestId };
            foreach (var q in quest.failsWithMe)
                PropagateFailure(q, visited);
        }
    }

    private void TryActivateQuest(QuestSO quest)
    {
        if (quest == null || !_runtime.TryGetValue(quest.QuestId, out var r)) return;
        if (r.status != QuestStatus.Inactive) return;
        if (ShouldActivate(quest))
            Transition(quest, r, QuestStatus.Active, -1);
    }

    private void CancelQuest(QuestSO quest)
    {
        if (quest == null || !_runtime.TryGetValue(quest.QuestId, out var r)) return;
        if (r.status == QuestStatus.Active || r.status == QuestStatus.Inactive)
            Transition(quest, r, QuestStatus.Cancelled, -1);
    }

    private void PropagateFailure(QuestSO quest, HashSet<string> visited)
    {
        if (quest == null || visited.Contains(quest.QuestId)) return;
        visited.Add(quest.QuestId);

        if (!_runtime.TryGetValue(quest.QuestId, out var r)) return;
        // Never overwrite terminal states — only propagate to active/inactive quests
        if (r.status != QuestStatus.Active && r.status != QuestStatus.Inactive) return;

        // Set directly to avoid Transition creating a second visited set and firing outcome effects
        r.status               = QuestStatus.Failed;
        r.resolvedOutcomeIndex = -1;
        Debug.Log($"[QuestManager] '{quest.title}' → Failed (propagated)");
        WorldStateManager.Instance?.SetBool($"quest.{quest.QuestId}.failed", true);
        NotifyChanged();

        // Continue with the same visited set
        if (quest.failsWithMe != null)
            foreach (var q in quest.failsWithMe)
                PropagateFailure(q, visited);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public QuestStatus GetStatus(string questId) =>
        _runtime.TryGetValue(questId, out var r) ? r.status : QuestStatus.Inactive;

    public QuestStatus GetStatus(QuestSO quest) =>
        quest != null ? GetStatus(quest.QuestId) : QuestStatus.Inactive;

    public bool IsObjectiveComplete(string questId, int index)
    {
        if (!_runtime.TryGetValue(questId, out var r)) return false;
        return index >= 0 && index < r.objectivesComplete.Length && r.objectivesComplete[index];
    }

    public bool IsObjectiveComplete(QuestSO quest, int index) =>
        quest != null && IsObjectiveComplete(quest.QuestId, index);

    public bool IsObjectiveRevealed(QuestSO quest, int index)
    {
        if (quest == null || !_runtime.TryGetValue(quest.QuestId, out var r)) return false;
        return index >= 0 && index < r.objectivesRevealed.Length && r.objectivesRevealed[index];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void InitRuntime()
    {
        _runtime.Clear();
        var seen = new HashSet<string>();
        foreach (var quest in _quests)
        {
            if (quest == null || string.IsNullOrEmpty(quest.QuestId)) continue;
            if (!seen.Add(quest.QuestId))
            {
                Debug.LogError($"[QuestManager] Duplicate QuestId '{quest.QuestId}' on '{quest.name}'. " +
                               "Did you duplicate the asset? Right-click it → Reset Quest ID.", quest);
                continue;
            }
            int failCount = quest.globalFailConditions?.Length ?? 0;
            int objCount  = quest.objectives?.Length           ?? 0;
            _runtime[quest.QuestId] = new QuestRuntimeState(quest.QuestId, objCount, failCount);
        }
    }

    /// <summary>Clears quest-graph WSM flags written by Transition. Called before a repeatable reset.</summary>
    private void ClearQuestWSMFlags(QuestSO quest)
    {
        if (WorldStateManager.Instance == null) return;
        var id = quest.QuestId;
        WorldStateManager.Instance.SetBool($"quest.{id}.active",    false);
        WorldStateManager.Instance.SetBool($"quest.{id}.succeeded", false);
        WorldStateManager.Instance.SetBool($"quest.{id}.failed",    false);
        WorldStateManager.Instance.SetBool($"quest.{id}.expired",   false);
        WorldStateManager.Instance.SetBool($"quest.{id}.cancelled", false);
    }

    private static QuestStatus TerminalToQuestStatus(QuestTerminalStatus t) => t switch
    {
        QuestTerminalStatus.Succeeded => QuestStatus.Succeeded,
        QuestTerminalStatus.Failed    => QuestStatus.Failed,
        QuestTerminalStatus.Expired   => QuestStatus.Expired,
        _                             => QuestStatus.Succeeded,
    };

    private static string PackBools(bool[] arr)
    {
        if (arr == null || arr.Length == 0) return "";
        var sb = new System.Text.StringBuilder(arr.Length);
        foreach (var b in arr) sb.Append(b ? '1' : '0');
        return sb.ToString();
    }

    private static void UnpackBools(string packed, bool[] target)
    {
        for (int i = 0; i < packed.Length && i < target.Length; i++)
            target[i] = packed[i] == '1';
    }

    [Serializable]
    private class QuestManagerDTO
    {
        public List<string> ids                = new List<string>();
        public List<int>    statuses           = new List<int>();
        public List<string> objectives         = new List<string>();
        public List<string> revealed           = new List<string>();
        public List<string> fails              = new List<string>();
        public List<int>    outcomeIndices     = new List<int>();
        public List<float>  activeTimeElapsed  = new List<float>();
    }
}
