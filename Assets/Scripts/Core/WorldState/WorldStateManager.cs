using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for named world state: quest flags, door locks, NPC deaths,
/// faction standings, power states. Every gameplay system reads and writes by key.
///
/// Key naming convention (enforced by discipline, not code):
///   "door.factory_01.unlocked"
///   "quest.intro.met_trader"
///   "npc.guard_a.dead"
///   "world.generator_a.active"
///
/// Persisted via WorldStateSaveAdapter + SaveSystem.
/// </summary>
public class WorldStateManager : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────

    public static WorldStateManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── State store ────────────────────────────────────────────────────────

    private readonly Dictionary<string, WorldStateValue> _state
        = new Dictionary<string, WorldStateValue>(StringComparer.OrdinalIgnoreCase);

    // ── Events ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when any key changes. (key, oldValue, newValue).
    /// oldValue is null when the key is set for the first time.
    /// </summary>
    public event Action<string, WorldStateValue, WorldStateValue> OnStateChanged;

    /// <summary>
    /// Fired AFTER <see cref="LoadState"/> bulk-replaces the entire store (save load).
    /// Per-key <see cref="OnStateChanged"/> does NOT fire during a bulk load, so any
    /// component that mirrors world state into the scene (doors, power panels, gates)
    /// must re-read its keys on this event. Listeners should make their refresh idempotent.
    ///
    /// Why this exists: SaveSystem defers a scene-load restore by one frame (after every
    /// Start()), so reading state only in Start() misses values loaded a frame later.
    /// </summary>
    public event Action OnStateReplaced;

    // ── Bool API ───────────────────────────────────────────────────────────

    public void SetBool(string key, bool value) =>
        Set(key, WorldStateValue.FromBool(value));

    public bool GetBool(string key, bool fallback = false)
    {
        if (_state.TryGetValue(key, out var v) && v.Type == WorldStateValue.ValueType.Bool)
            return v.AsBool();
        return fallback;
    }

    // ── Int API ────────────────────────────────────────────────────────────

    public void SetInt(string key, int value) =>
        Set(key, WorldStateValue.FromInt(value));

    public int GetInt(string key, int fallback = 0)
    {
        if (_state.TryGetValue(key, out var v) && v.Type == WorldStateValue.ValueType.Int)
            return v.AsInt();
        return fallback;
    }

    // ── Float API ──────────────────────────────────────────────────────────

    public void SetFloat(string key, float value) =>
        Set(key, WorldStateValue.FromFloat(value));

    public float GetFloat(string key, float fallback = 0f)
    {
        if (_state.TryGetValue(key, out var v) && v.Type == WorldStateValue.ValueType.Float)
            return v.AsFloat();
        return fallback;
    }

    // ── String API ─────────────────────────────────────────────────────────

    public void SetString(string key, string value) =>
        Set(key, WorldStateValue.FromString(value));

    public string GetString(string key, string fallback = "")
    {
        if (_state.TryGetValue(key, out var v) && v.Type == WorldStateValue.ValueType.String)
            return v.AsString();
        return fallback;
    }

    /// <summary>Returns true if the key has ever been written (regardless of type).</summary>
    public bool HasKey(string key) => !string.IsNullOrEmpty(key) && _state.ContainsKey(key);

    // ── Bulk access (for SaveSystem) ───────────────────────────────────────

    /// <summary>Returns a shallow copy of the entire state for serialization.</summary>
    public Dictionary<string, WorldStateValue> GetAllState() =>
        new Dictionary<string, WorldStateValue>(_state);

    /// <summary>Replaces the entire state (call from SaveSystem on load). Does not fire events.</summary>
    public void LoadState(Dictionary<string, WorldStateValue> saved)
    {
        if (saved == null) return;
        _state.Clear();
        foreach (var kv in saved)
            _state[kv.Key] = kv.Value;

        OnStateReplaced?.Invoke();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void Set(string key, WorldStateValue newValue)
    {
        _state.TryGetValue(key, out var old);
        _state[key] = newValue;
        OnStateChanged?.Invoke(key, old, newValue);
    }
}
