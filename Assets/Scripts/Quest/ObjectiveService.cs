using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The invisible brain behind <see cref="ObjectiveSO"/>. Watches gameplay signals and flips each
/// objective's single bool fact (DoneKey) when satisfied, so quests just "watch a checkbox" and the
/// designer never wires WSM keys.
///
/// It tracks only objectives that a registered quest actually uses (derived from
/// QuestManager.Quests), and — crucially — only accrues progress while an OWNING quest is Active
/// (unless the objective opts into Lifetime). That stops "collect 3 items" from auto-completing the
/// moment you accept it because you already had loot.
///
/// Counts/timers live in WSM ints (objective.{id}.count) so they persist via WorldStateSaveAdapter.
/// Put this on the GameSystems object next to QuestManager / WorldStateManager.
/// </summary>
public sealed class ObjectiveService : MonoBehaviour, IRunLifecycleListener
{
    public static ObjectiveService Instance { get; private set; }

    private readonly HashSet<ObjectiveSO> _tracked = new HashSet<ObjectiveSO>();
    private readonly Dictionary<ObjectiveSO, List<QuestSO>> _owners = new Dictionary<ObjectiveSO, List<QuestSO>>();

    private float _surviveAccumulator;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        LootItemWorld.AnyPickup     += OnItemPickup;
        EnemyHealth.AnyDeath        += OnEnemyDeath;
        LockedDoor.AnyUnlocked      += OnDoorUnlocked;
        RechargeStation.AnyRecharge += OnRecharge;

        if (WorldStateManager.Instance != null)
        {
            WorldStateManager.Instance.OnStateChanged  += OnWsmChanged;
            WorldStateManager.Instance.OnStateReplaced += OnWsmReplaced;
        }
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestsChanged += OnQuestsChanged;
        RunManager.Instance?.RegisterListener(this);
    }

    private void OnDisable()
    {
        LootItemWorld.AnyPickup     -= OnItemPickup;
        EnemyHealth.AnyDeath        -= OnEnemyDeath;
        LockedDoor.AnyUnlocked      -= OnDoorUnlocked;
        RechargeStation.AnyRecharge -= OnRecharge;

        if (WorldStateManager.Instance != null)
        {
            WorldStateManager.Instance.OnStateChanged  -= OnWsmChanged;
            WorldStateManager.Instance.OnStateReplaced -= OnWsmReplaced;
        }
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestsChanged -= OnQuestsChanged;
        RunManager.Instance?.UnregisterListener(this);
    }

    private void Start()
    {
        // Safety net for Awake ordering: if RunManager's singleton wasn't set yet when our OnEnable
        // ran, our listener registration there was a silent no-op — and OnRunExtracted (which
        // completes ExtractRaid objectives) would never fire. Re-register here; RegisterListener is
        // Contains-guarded, so a double call is harmless. Mirrors AudioDirector / RunScoreUI.
        RunManager.Instance?.RegisterListener(this);

        BuildTrackedFromQuests();
        EvaluateThresholds(); // currency / custom-flag may already be satisfied
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>An objective is "tracked" only if some registered quest references it. Zero extra setup.</summary>
    private void BuildTrackedFromQuests()
    {
        _tracked.Clear();
        _owners.Clear();

        var qm = QuestManager.Instance;
        if (qm == null) return;

        foreach (var quest in qm.Quests)
        {
            if (quest == null || quest.objectives == null) continue;
            foreach (var od in quest.objectives)
            {
                if (od?.objective == null) continue;
                _tracked.Add(od.objective);
                if (!_owners.TryGetValue(od.objective, out var owners))
                    _owners[od.objective] = owners = new List<QuestSO>();
                if (!owners.Contains(quest)) owners.Add(quest);
            }
        }
    }

    // ── Accrual gating ─────────────────────────────────────────────────────

    private bool ShouldAccrue(ObjectiveSO obj)
    {
        if (obj == null || IsDone(obj)) return false;
        if (obj.accrual == ObjectiveAccrualMode.Lifetime) return true;

        var qm = QuestManager.Instance;
        if (qm == null || !_owners.TryGetValue(obj, out var owners)) return false;
        foreach (var q in owners)
            if (qm.GetStatus(q) == QuestStatus.Active) return true;
        return false;
    }

    private static bool IsDone(ObjectiveSO obj) =>
        WorldStateManager.Instance != null && WorldStateManager.Instance.GetBool(obj.DoneKey);

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnItemPickup(ItemSO item)
    {
        foreach (var obj in _tracked)
        {
            if (obj.type != ObjectiveType.CollectItems) continue;
            if (obj.itemFilter != null && obj.itemFilter != item) continue;
            if (ShouldAccrue(obj)) Increment(obj);
        }
    }

    private void OnEnemyDeath(EnemyHealth enemy)
    {
        if (enemy == null) return;
        foreach (var obj in _tracked)
        {
            if (obj.type != ObjectiveType.KillEnemies) continue;
            if (obj.useTeamFilter && enemy.TeamId != obj.killTeam) continue;
            if (ShouldAccrue(obj)) Increment(obj);
        }
    }

    private void OnDoorUnlocked(LockedDoor door)
    {
        bool isShortcut = door is ShortcutLock;
        foreach (var obj in _tracked)
        {
            // UnlockAnyDoor counts every door (incl. shortcuts); UnlockAnyShortcut counts only shortcuts.
            bool match = obj.type == ObjectiveType.UnlockAnyDoor
                      || (obj.type == ObjectiveType.UnlockAnyShortcut && isShortcut);
            if (match && ShouldAccrue(obj)) Increment(obj);
        }
    }

    private void OnRecharge()
    {
        foreach (var obj in _tracked)
        {
            if (obj.type != ObjectiveType.UseRechargeStation) continue;
            if (ShouldAccrue(obj)) Increment(obj);
        }
    }

    private void OnWsmChanged(string key, WorldStateValue oldV, WorldStateValue newV) => EvaluateThresholds();
    private void OnWsmReplaced() => EvaluateThresholds();
    private void OnQuestsChanged() => EvaluateThresholds();

    /// <summary>Currency + custom-flag are threshold/flag watches, not event counters.</summary>
    private void EvaluateThresholds()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return;

        foreach (var obj in _tracked)
        {
            if (IsDone(obj) || !ShouldAccrue(obj)) continue;
            bool met = obj.type switch
            {
                ObjectiveType.ReachCurrency => wsm.GetInt("economy.credits") >= obj.Target,
                ObjectiveType.CustomFlag    => !string.IsNullOrEmpty(obj.customKey) && wsm.GetBool(obj.customKey),
                _                           => false
            };
            if (met) wsm.SetBool(obj.DoneKey, true);
        }
    }

    /// <summary>Called by <see cref="ObjectiveTrigger"/> when the player enters a zone.</summary>
    public void MarkZoneReached(ObjectiveSO obj)
    {
        if (obj == null || obj.type != ObjectiveType.ReachZone) return;
        if (ShouldAccrue(obj)) WorldStateManager.Instance?.SetBool(obj.DoneKey, true);
    }

    private void Increment(ObjectiveSO obj, int by = 1)
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return;
        int cur = wsm.GetInt(obj.CountKey) + by;
        wsm.SetInt(obj.CountKey, cur);
        if (cur >= obj.Target) wsm.SetBool(obj.DoneKey, true);
    }

    // ── Survive timer ──────────────────────────────────────────────────────

    private void Update()
    {
        if (_tracked.Count == 0) return;

        bool inRun = RunManager.Instance == null || RunManager.Instance.State == RunManager.RunState.InRun;
        if (!inRun) { _surviveAccumulator = 0f; return; }

        _surviveAccumulator += Time.deltaTime;
        if (_surviveAccumulator < 1f) return;
        int whole = Mathf.FloorToInt(_surviveAccumulator);
        _surviveAccumulator -= whole;

        foreach (var obj in _tracked)
        {
            if (obj.type != ObjectiveType.SurviveSeconds) continue;
            if (ShouldAccrue(obj)) Increment(obj, whole);
        }
    }

    // ── Progress query (for the tracker) ───────────────────────────────────

    public (int current, int target) GetProgress(ObjectiveSO obj)
    {
        int target = obj.Target;
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return (0, target);
        if (wsm.GetBool(obj.DoneKey)) return (target, target);

        return obj.type switch
        {
            ObjectiveType.ReachCurrency => (Mathf.Clamp(wsm.GetInt("economy.credits"), 0, target), target),
            ObjectiveType.ReachZone     => (0, 1),
            ObjectiveType.ExtractRaid   => (0, 1),
            ObjectiveType.CustomFlag    => (0, 1),
            _                           => (Mathf.Clamp(wsm.GetInt(obj.CountKey), 0, target), target)
        };
    }

    // ── IRunLifecycleListener ──────────────────────────────────────────────

    public void OnRunStarted()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return;
        foreach (var obj in _tracked)
        {
            if (!obj.resetEachRun) continue;
            wsm.SetInt(obj.CountKey, 0);     // clear count before the done flag
            wsm.SetBool(obj.DoneKey, false);
        }
    }

    public void OnRunExtracted()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return;
        foreach (var obj in _tracked)
            if (obj.type == ObjectiveType.ExtractRaid && ShouldAccrue(obj))
                wsm.SetBool(obj.DoneKey, true);
    }

    public void OnRunDied()      { }
    public void OnReturnedToHub(){ }
}
