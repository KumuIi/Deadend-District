using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Collects data from all registered ISaveable systems, serializes to JSON, and writes
/// to the persistent data path. Each RunScopeTag writes its own envelope file so
/// ClearRun() can wipe run-scoped data without touching profile or world data.
///
/// Systems call Register/Unregister in their Start/OnDisable. The SaveSystem
/// never hard-references specific gameplay systems — all coupling is via ISaveable.
///
/// Scope file naming: save_{slot}_{scope}.json
/// e.g. save_slot0_Run.json, save_slot0_Profile.json, save_slot0_World.json
/// </summary>
public class SaveSystem : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────

    public static SaveSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── Registry ───────────────────────────────────────────────────────────

    private readonly List<ISaveable> _saveables = new List<ISaveable>();

    // Queued scopes to restore once all ISaveables have registered in Start().
    private readonly HashSet<RunScopeTag> _pendingRestoreScopes = new HashSet<RunScopeTag>();
    private string _pendingSlot;

    public void Register(ISaveable saveable)
    {
        if (!_saveables.Contains(saveable))
            _saveables.Add(saveable);
    }

    public void Unregister(ISaveable saveable) =>
        _saveables.Remove(saveable);

    // ── Scope-aware Save ───────────────────────────────────────────────────

    public void SaveProfile(string slotName = "slot0") => SaveScope(RunScopeTag.Profile, slotName);
    public void SaveRun(string slotName = "slot0")     => SaveScope(RunScopeTag.Run,     slotName);
    public void SaveWorld(string slotName = "slot0")   => SaveScope(RunScopeTag.World,   slotName);

    public void SaveAll(string slotName = "slot0")
    {
        SaveScope(RunScopeTag.Profile, slotName);
        SaveScope(RunScopeTag.Run,     slotName);
        SaveScope(RunScopeTag.World,   slotName);
    }

    /// <summary>Legacy entry point — saves all scopes. Prefer SaveAll/SaveRun/etc.</summary>
    public void Save(string slotName = "slot0") => SaveAll(slotName);

    private void SaveScope(RunScopeTag scope, string slotName)
    {
        var envelope = new SaveEnvelope
        {
            SceneId  = SceneManager.GetActiveScene().name,
            SaveTime = DateTime.UtcNow.ToString("o"),
        };

        foreach (var saveable in _saveables)
        {
            if (saveable.SaveScope != scope) continue;
            try
            {
                var dto   = saveable.CaptureSaveData();
                var entry = new SaveEntry
                {
                    SaveId   = saveable.SaveId,
                    SaveType = saveable.SaveType,
                    DataJson = JsonUtility.ToJson(dto),
                };
                envelope.Entries.Add(entry);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveSystem] Failed to capture '{saveable.SaveId}': {ex.Message}");
            }
        }

        string path = GetScopePath(slotName, scope);
        File.WriteAllText(path, JsonUtility.ToJson(envelope, prettyPrint: true));
        Debug.Log($"[SaveSystem] Saved {scope} scope to {path}");

        // Update flat sidecar for UI hover display
        SaveMetadataIO.Write(slotName);
    }

    // ── Scope-aware Load ───────────────────────────────────────────────────

    public void LoadProfile(string slotName = "slot0") => LoadScope(RunScopeTag.Profile, slotName);
    public void LoadRun(string slotName = "slot0")     => LoadScope(RunScopeTag.Run,     slotName);
    public void LoadWorld(string slotName = "slot0")   => LoadScope(RunScopeTag.World,   slotName);

    public void LoadAll(string slotName = "slot0")
    {
        // World FIRST: it's the fact substrate that quests/doors/etc. derive from. If Profile (quest
        // statuses) restored before World, any evaluation during the load window would read stale
        // in-memory facts — leaving a just-completed quest showing "done" until a second load.
        LoadScope(RunScopeTag.World,   slotName);
        LoadScope(RunScopeTag.Profile, slotName);
        LoadScope(RunScopeTag.Run,     slotName);
    }

    /// <summary>Legacy entry point — loads all scopes. Prefer LoadAll/LoadRun/etc.</summary>
    public void Load(string slotName = "slot0") => LoadAll(slotName);

    private void LoadScope(RunScopeTag scope, string slotName)
    {
        string path = GetScopePath(slotName, scope);
        if (!File.Exists(path))
        {
            Debug.Log($"[SaveSystem] No {scope} save at {path} — skipping.");
            return;
        }

        SaveEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveSystem] Failed to parse {scope} save: {ex.Message}");
            return;
        }

        if (envelope?.Entries == null)
        {
            Debug.LogWarning($"[SaveSystem] {scope} save file has no entries.");
            return;
        }

        var lookup = new Dictionary<string, SaveEntry>(envelope.Entries.Count);
        foreach (var entry in envelope.Entries)
            lookup[entry.SaveId] = entry;

        foreach (var saveable in _saveables)
        {
            if (saveable.SaveScope != scope) continue;
            if (!lookup.TryGetValue(saveable.SaveId, out var entry)) continue;
            try
            {
                saveable.RestoreSaveData(entry.DataJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveSystem] Failed to restore '{saveable.SaveId}': {ex.Message}");
            }
        }

        Debug.Log($"[SaveSystem] Loaded {scope} scope from {path} (scene: {envelope.SceneId})");
    }

    // ── ClearRun ───────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes the Run-scoped save envelope. Called by RunManager on death.
    /// Profile and World envelopes are untouched.
    /// </summary>
    public void ClearRun(string slotName = "slot0")
    {
        string path = GetScopePath(slotName, RunScopeTag.Run);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveSystem] Cleared Run scope save at {path}");
        }
    }

    // ── Deferred Restore ───────────────────────────────────────────────────

    /// <summary>
    /// Queue a scope restore to fire once all ISaveables have run Start() after a scene load.
    /// Use this when loading into a new scene — objects may not have registered yet.
    /// </summary>
    public void RestoreAfterSceneLoad(RunScopeTag scope, string slotName = "slot0")
    {
        _pendingSlot = slotName;
        _pendingRestoreScopes.Add(scope);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_pendingRestoreScopes.Count == 0) return;
        // Defer one frame so all Start() calls complete before restoring.
        StartCoroutine(FlushPendingRestores());
    }

    private System.Collections.IEnumerator FlushPendingRestores()
    {
        yield return null; // wait one frame for all Start() registrations
        foreach (var scope in _pendingRestoreScopes)
            LoadScope(scope, _pendingSlot ?? "slot0");
        _pendingRestoreScopes.Clear();
        _pendingSlot = null;
    }

    // ── Slot queries ───────────────────────────────────────────────────────

    public bool SlotExists(string slotName = "slot0") =>
        File.Exists(GetScopePath(slotName, RunScopeTag.Profile)) ||
        File.Exists(GetScopePath(slotName, RunScopeTag.World))   ||
        File.Exists(GetScopePath(slotName, RunScopeTag.Run));

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string GetScopePath(string slotName, RunScopeTag scope)
    {
        var safe = Regex.Replace(slotName, @"[^a-zA-Z0-9_\-]", "_");
        return Path.Combine(Application.persistentDataPath, $"save_{safe}_{scope}.json");
    }
}
