using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic quest evaluator. Reads WorldStateManager facts — never contains quest-specific logic.
///
/// Lifecycle per quest:
///   Inactive → watches activeCondition → Active
///   Active   → checks failConditions (any true → Failed), then objectives (all true → Succeeded)
///   Succeeded/Failed → terminal; sets WSM key "quest.{id}.succeeded" or "quest.{id}.failed"
///
/// On Start: subscribe to WorldStateManager.OnStateChanged, then evaluate current state
/// (handles save-load where WSM is restored without firing events).
/// On RestoreSaveData: trust saved status; do NOT re-evaluate against WSM (ordering race).
/// </summary>
public class QuestManager : MonoBehaviour, ISaveable
{
    public static QuestManager Instance { get; private set; }

    [SerializeField] private List<QuestSO> _quests = new List<QuestSO>();

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

    // ── ISaveable ────────────────────────────────────────────────────────────

    public string SaveId   => "quest.manager";
    public string SaveType => "QuestManager";

    public object CaptureSaveData()
    {
        var dto = new QuestManagerDTO();
        foreach (var kv in _runtime)
        {
            dto.ids.Add(kv.Key);
            dto.statuses.Add((int)kv.Value.status);
            dto.objectives.Add(PackBools(kv.Value.objectivesComplete));
            dto.fails.Add(PackBools(kv.Value.failConditionsTriggered));
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
            UnpackBools(i < dto.objectives.Count ? dto.objectives[i] : "", r.objectivesComplete);
            UnpackBools(i < dto.fails.Count     ? dto.fails[i]      : "", r.failConditionsTriggered);
        }
        // Do NOT call EvaluateAll() — WorldStateSaveAdapter may not have run yet.
    }

    // ── WSM event ────────────────────────────────────────────────────────────

    private void OnWSMChanged(string key, WorldStateValue oldVal, WorldStateValue newVal)
    {
        foreach (var quest in _quests)
        {
            if (quest == null || string.IsNullOrEmpty(quest.questId)) continue;
            if (!_runtime.TryGetValue(quest.questId, out var r)) continue;
            EvaluateQuest(quest, r);
        }
    }

    // ── Evaluation ───────────────────────────────────────────────────────────

    private void EvaluateAll()
    {
        foreach (var quest in _quests)
        {
            if (quest == null || string.IsNullOrEmpty(quest.questId)) continue;
            if (_runtime.TryGetValue(quest.questId, out var r))
                EvaluateQuest(quest, r);
        }
    }

    private void EvaluateQuest(QuestSO quest, QuestRuntimeState r)
    {
        switch (r.status)
        {
            case QuestStatus.Inactive:
                if (ShouldActivate(quest))
                    Transition(quest, r, QuestStatus.Active);
                break;

            case QuestStatus.Active:
                // Evaluate both arrays, then check state — decouple "changed" from "should transition"
                EvaluateConditions(quest.failConditions, r.failConditionsTriggered);
                EvaluateConditions(quest.objectives,     r.objectivesComplete);

                if (r.AnyFailTriggered())
                { Transition(quest, r, QuestStatus.Failed); break; }

                if (r.AllObjectivesMet())
                    Transition(quest, r, QuestStatus.Succeeded);
                break;

            // Terminal states — ignore further events
        }
    }

    private bool ShouldActivate(QuestSO quest)
    {
        if (quest.activeCondition == null || string.IsNullOrEmpty(quest.activeCondition.wsmKey))
            return true; // No activation condition → starts Active
        return quest.activeCondition.Evaluate();
    }

    /// <summary>
    /// Evaluates each condition, writes result into <paramref name="results"/>.
    /// Returns true if the array changed (re-evaluation happened).
    /// </summary>
    private bool EvaluateConditions(QuestConditionDefinition[] conditions, bool[] results)
    {
        if (conditions == null || conditions.Length == 0) return false;
        bool changed = false;
        for (int i = 0; i < conditions.Length && i < results.Length; i++)
        {
            bool was = results[i];
            results[i] = conditions[i]?.Evaluate() ?? false;
            if (results[i] != was) changed = true;
        }
        return changed;
    }

    private void Transition(QuestSO quest, QuestRuntimeState r, QuestStatus next)
    {
        r.status = next;
        string flag = next == QuestStatus.Succeeded ? $"quest.{quest.questId}.succeeded"
                    : next == QuestStatus.Failed    ? $"quest.{quest.questId}.failed"
                    : next == QuestStatus.Active    ? $"quest.{quest.questId}.active"
                    : null;
        if (flag != null) WorldStateManager.Instance.SetBool(flag, true);
        Debug.Log($"[QuestManager] '{quest.title}' → {next}");

        // If we just activated, immediately evaluate objectives/fail
        if (next == QuestStatus.Active) EvaluateQuest(quest, r);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public QuestStatus GetStatus(string questId) =>
        _runtime.TryGetValue(questId, out var r) ? r.status : QuestStatus.Inactive;

    public bool IsObjectiveComplete(string questId, int index)
    {
        if (!_runtime.TryGetValue(questId, out var r)) return false;
        return index >= 0 && index < r.objectivesComplete.Length && r.objectivesComplete[index];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void InitRuntime()
    {
        _runtime.Clear();
        foreach (var quest in _quests)
        {
            if (quest == null || string.IsNullOrEmpty(quest.questId)) continue;
            int failCount = quest.failConditions?.Length ?? 0;
            int objCount  = quest.objectives?.Length     ?? 0;
            _runtime[quest.questId] = new QuestRuntimeState(quest.questId, objCount, failCount);
        }
    }

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
        public List<string> ids       = new List<string>();
        public List<int>    statuses  = new List<int>();
        public List<string> objectives = new List<string>();
        public List<string> fails      = new List<string>();
    }
}
