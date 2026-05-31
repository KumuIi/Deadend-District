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

    private const string SlotPrefKey = "LastSaveSlot";

    private void Start()
    {
        // Restore the slot the player last explicitly chose; fall back to inspector default.
        ActiveSaveSlot = PlayerPrefs.GetString(SlotPrefKey, _defaultSaveSlot);
    }

    public void SetActiveSlot(string slot)
    {
        if (string.IsNullOrEmpty(slot)) return;
        ActiveSaveSlot = slot;
        PlayerPrefs.SetString(SlotPrefKey, slot);
        PlayerPrefs.Save();
    }

    // ── Player registration ────────────────────────────────────────────────

    private PlayerHealth _playerHealth;

    /// <summary>The currently-registered player, or null in the hub before registration.
    /// AI prefer this over FindObjectOfType (rulebook) — the player registers on spawn.</summary>
    public PlayerHealth PlayerHealth => _playerHealth;

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
        // MANUAL-SAVE MODEL: the lifecycle no longer autosaves to the player's slot.
        // Saving is the player's responsibility (flashdrive menu). Re-enable these for
        // Tarkov-style permanent death (commits state to disk on entry, no save-scum).
        // SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);
        // SaveSystem.Instance?.SaveWorld(ActiveSaveSlot);
        // SaveSystem.Instance?.ClearRun(ActiveSaveSlot);
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
        // MANUAL-SAVE MODEL: the lifecycle no longer autosaves to the player's slot.
        // Saving is the player's responsibility (flashdrive menu). Re-enable these for
        // Tarkov-style permanent death (commits state to disk on entry, no save-scum).
        // NOTE: if you re-enable ClearRun here, it deletes the player's manual Run snapshot
        // for ActiveSaveSlot — route it to a dedicated working slot instead (see notes).
        // SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);
        // SaveSystem.Instance?.SaveWorld(ActiveSaveSlot);
        // SaveSystem.Instance?.ClearRun(ActiveSaveSlot);
        Broadcast(l => l.OnRunStarted());
    }

    /// <summary>Called by ExtractionPoint on successful extraction.</summary>
    public void TriggerExtract()
    {
        if (State != RunState.InRun) return;
        State = RunState.Extracting;

        // MANUAL-SAVE MODEL: extraction no longer autosaves. The player keeps their
        // extracted inventory in memory and saves manually when they choose.
        // Re-enable for Tarkov-style auto-commit of extracted loot.
        // SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);
        // SaveSystem.Instance?.SaveRun(ActiveSaveSlot);
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
        // MANUAL-SAVE MODEL: no autosave/clear on return. Re-enable for Tarkov-style permadeath.
        // SaveSystem.Instance?.ClearRun(ActiveSaveSlot);
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

        // Death clears the inventory in MEMORY only: InventorySaveAdapter implements
        // IRunLifecycleListener and empties itself on OnRunDied. Nothing is written to disk,
        // so reloading a save slot restores the pre-raid inventory (manual-save model).
        Broadcast(l => l.OnRunDied());

        // MANUAL-SAVE MODEL: do NOT persist the death. Re-enable to commit the loss to disk
        // for Tarkov-style permanent death (player cannot reload to recover lost loot).
        // SaveSystem.Instance?.SaveProfile(ActiveSaveSlot);

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
        // MANUAL-SAVE MODEL: no autosave/clear on return. Re-enable for Tarkov-style permadeath.
        // SaveSystem.Instance?.ClearRun(ActiveSaveSlot);
    }

    // ── Load ───────────────────────────────────────────────────────────────

    private string _pendingLoadSlot;

    /// <summary>
    /// Loads a save slot. In the hub, restores in place. During a run, returns to the hub
    /// first — saves are hub-only, so a load always lands in the hub — then restores once
    /// the transition completes. Called by the flashdrive load menu.
    /// </summary>
    public void LoadSlot(string slot)
    {
        if (string.IsNullOrEmpty(slot)) return;
        SetActiveSlot(slot);

        if (State == RunState.InHub)
        {
            SaveSystem.Instance?.LoadAll(slot);
            return;
        }

        // Mid-run: abandon the run, transition back to the hub, then restore on arrival.
        var stm = SceneTransitionManager.Instance;
        if (stm == null)
        {
            Debug.LogError("[RunManager] LoadSlot: SceneTransitionManager missing — restoring in place.");
            State = RunState.InHub;
            SaveSystem.Instance?.LoadAll(slot);
            return;
        }

        _pendingLoadSlot = slot;
        stm.OnSceneTransitionFinished += OnReturnedToHubAfterLoad;
        if (!stm.LoadHub())
        {
            stm.OnSceneTransitionFinished -= OnReturnedToHubAfterLoad;
            _pendingLoadSlot = null;
            Debug.LogWarning("[RunManager] LoadSlot: LoadHub rejected (transition in progress) — load aborted.");
        }
    }

    private void OnReturnedToHubAfterLoad()
    {
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterLoad;

        State = RunState.InHub;
        Broadcast(l => l.OnReturnedToHub());

        // Hub is permanent (the player rig persists, so adapters are still registered) —
        // restore directly. This runs after the transition's spawn teleport, so the saved
        // position wins. LoadAll restores Profile + World + Run.
        var slot = _pendingLoadSlot;
        _pendingLoadSlot = null;
        if (!string.IsNullOrEmpty(slot))
            SaveSystem.Instance?.LoadAll(slot);
    }

    private void OnDisable()
    {
        // Defensive cleanup — unsubscribe all transition callbacks if RunManager is disabled mid-transition
        if (SceneTransitionManager.Instance == null) return;
        SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterExtract;
        SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterDeath;
        SceneTransitionManager.Instance.OnSceneTransitionFinished -= OnReturnedToHubAfterLoad;
    }
}
