using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the run lifecycle state machine: InHub → InRun → Extracting/Dead → InHub.
/// All scene loading routes through SceneTransitionManager.
/// All save calls are here — nothing else calls SaveSystem directly for run events.
///
/// PlayerHealth registers via RegisterPlayer/UnregisterPlayer (called by PlayerRunRegistration).
/// IRunLifecycleListeners register via OnEnable/OnDisable on their own MonoBehaviours.
/// RunManager snapshots the listener list before dispatching to prevent mid-iteration mutation.
///
/// Implementors: one instance on the GameSystems GameObject (DontDestroyOnLoad).
/// </summary>
public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    public enum RunState { InHub, InRun, Extracting, Dead }

    [Header("Settings")]
    [SerializeField] private string _defaultSaveSlot = "slot0";
    [SerializeField] private float  _deathFadeDelay  = 1.5f;

    public RunState State { get; private set; } = RunState.InHub;

    /// <summary>
    /// The save slot currently in use. Set when the player loads or saves from the flashdrive menu.
    /// All RunManager save operations use this slot.
    /// </summary>
    public string ActiveSaveSlot { get; private set; }

    private void Start() => ActiveSaveSlot = _defaultSaveSlot;

    public void SetActiveSlot(string slot)
    {
        if (!string.IsNullOrEmpty(slot)) ActiveSaveSlot = slot;
    }

    // ── Player registration ────────────────────────────────────────────────

    private PlayerHealth _playerHealth;

    public void RegisterPlayer(PlayerHealth health)
    {
        if (_playerHealth == health) return;
        if (_playerHealth != null)
            _playerHealth.OnDeath.RemoveListener(OnPlayerDeath);

        _playerHealth = health;

        if (_playerHealth != null)
            _playerHealth.OnDeath.AddListener(OnPlayerDeath);
    }

    public void UnregisterPlayer(PlayerHealth health)
    {
        if (_playerHealth != health) return;
        _playerHealth.OnDeath.RemoveListener(OnPlayerDeath);
        _playerHealth = null;
    }

    // ── Lifecycle listener registry ────────────────────────────────────────

    private readonly List<IRunLifecycleListener> _listeners = new List<IRunLifecycleListener>();

    public void RegisterListener(IRunLifecycleListener l)
    {
        if (!_listeners.Contains(l)) _listeners.Add(l);
    }

    public void UnregisterListener(IRunLifecycleListener l) => _listeners.Remove(l);

    private void Broadcast(System.Action<IRunLifecycleListener> action)
    {
        // Snapshot so callbacks can safely register/unregister
        var snapshot = new List<IRunLifecycleListener>(_listeners);
        foreach (var l in snapshot) action(l);
    }

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Public run API ─────────────────────────────────────────────────────

    /// <summary>
    /// Start a run without any scene loading — for when the sector is part of
    /// the same scene or reached by a physical door. Just sets state to InRun.
    /// </summary>
    public void StartRunInPlace()
    {
        if (State != RunState.InHub)
        {
            Debug.LogWarning("[RunManager] StartRunInPlace called outside InHub state — ignored.");
            return;
        }
        State = RunState.InRun;
        SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);
        SaveSystem.Instance?.SaveWorld(ActiveSaveSlot);
        Broadcast(l => l.OnRunStarted());
        Debug.Log("[RunManager] Run started in place.");
    }

    /// <summary>Transition from hub into a separate sector scene.</summary>
    public void StartRun(string sectorName)
    {
        if (State != RunState.InHub)
        {
            Debug.LogWarning("[RunManager] StartRun called outside InHub state — ignored.");
            return;
        }

        var stm = SceneTransitionManager.Instance;
        if (stm == null || !stm.LoadSector(sectorName))
        {
            Debug.LogError("[RunManager] StartRun: LoadSector failed — aborting run start.");
            return;
        }
        // Set state AFTER load is confirmed to start — prevents InRun with no sector loaded
        State = RunState.InRun;
        SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);
        SaveSystem.Instance?.SaveWorld(ActiveSaveSlot);
        Broadcast(l => l.OnRunStarted());
    }

    /// <summary>Called by ExtractionPoint on successful extraction.</summary>
    public void TriggerExtract()
    {
        if (State != RunState.InRun) return;
        State = RunState.Extracting;

        SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);
        SaveSystem.Instance?.SaveRun(ActiveSaveSlot);
        Broadcast(l => l.OnRunExtracted());

        var stm = SceneTransitionManager.Instance;
        if (stm == null) { Debug.LogError("[RunManager] SceneTransitionManager missing — cannot load hub."); return; }
        stm.OnSceneTransitionFinished += OnReturnedToHubAfterExtract;
        if (!stm.LoadHub())
        {
            stm.OnSceneTransitionFinished -= OnReturnedToHubAfterExtract;
            State = RunState.InHub;
            Debug.LogError("[RunManager] TriggerExtract: LoadHub rejected — reset to InHub.");
        }
    }

    private void OnReturnedToHubAfterExtract()
    {
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterExtract;
        State = RunState.InHub;
        Broadcast(l => l.OnReturnedToHub());
        SaveSystem.Instance?.ClearRun(ActiveSaveSlot);
    }

    /// <summary>Called by PlayerHealth.OnDeath (via PlayerRunRegistration).</summary>
    private void OnPlayerDeath()
    {
        if (State == RunState.Dead) return;
        StartCoroutine(DeathSequence());
    }

    /// <summary>
    /// Force-triggers death. Use for out-of-combat deaths (fall, hazard).
    /// Guarded against double-trigger.
    /// </summary>
    public void TriggerDeath()
    {
        if (State == RunState.Dead) return;
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        State = RunState.Dead;

        // Fade out before clearing state so the player sees the screen go dark
        if (SceneTransitionManager.Instance != null)
            yield return SceneTransitionManager.Instance.FadeOut();
        else
            yield return new WaitForSecondsRealtime(_deathFadeDelay);

        Broadcast(l => l.OnRunDied());
        // InventorySaveAdapter implements IRunLifecycleListener and clears itself on OnRunDied

        SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);

        var stm = SceneTransitionManager.Instance;
        if (stm == null) { Debug.LogError("[RunManager] SceneTransitionManager missing — cannot load hub after death."); yield break; }
        stm.OnSceneTransitionFinished += OnReturnedToHubAfterDeath;
        if (!stm.LoadHub())
        {
            stm.OnSceneTransitionFinished -= OnReturnedToHubAfterDeath;
            State = RunState.InHub;
            Debug.LogError("[RunManager] DeathSequence: LoadHub rejected — reset to InHub.");
        }
    }

    private void OnReturnedToHubAfterDeath()
    {
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterDeath;
        State = RunState.InHub;
        Broadcast(l => l.OnReturnedToHub());
        SaveSystem.Instance?.ClearRun(ActiveSaveSlot);
    }

    private void OnDisable()
    {
        // Defensive cleanup — unsubscribe all transition callbacks if RunManager is disabled mid-transition
        if (SceneTransitionManager.Instance == null) return;
        SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterExtract;
        SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterDeath;
    }
}
