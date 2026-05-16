using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Collects data from all registered ISaveable systems, serializes to JSON, and writes
/// to the persistent data path. Handles multiple save slots via slotName parameter.
///
/// Systems call Register/Unregister in their OnEnable/OnDisable. The SaveSystem
/// never hard-references specific gameplay systems — all coupling is via ISaveable.
///
/// Future: add async I/O, cloud sync, slot metadata (screenshot, playtime).
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
    }

    // ── Registry ───────────────────────────────────────────────────────────

    private readonly List<ISaveable> _saveables = new List<ISaveable>();

    public void Register(ISaveable saveable)
    {
        if (!_saveables.Contains(saveable))
            _saveables.Add(saveable);
    }

    public void Unregister(ISaveable saveable) =>
        _saveables.Remove(saveable);

    // ── Save ───────────────────────────────────────────────────────────────

    public void Save(string slotName = "slot0")
    {
        var envelope = new SaveEnvelope
        {
            SceneId  = SceneManager.GetActiveScene().name,
            SaveTime = DateTime.UtcNow.ToString("o"),
        };

        foreach (var saveable in _saveables)
        {
            try
            {
                var dto     = saveable.CaptureSaveData();
                var entry   = new SaveEntry
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

        string path = GetSavePath(slotName);
        File.WriteAllText(path, JsonUtility.ToJson(envelope, prettyPrint: true));
        Debug.Log($"[SaveSystem] Saved to {path}");
    }

    // ── Load ───────────────────────────────────────────────────────────────

    public void Load(string slotName = "slot0")
    {
        string path = GetSavePath(slotName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveSystem] No save file at {path}");
            return;
        }

        SaveEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveSystem] Failed to parse save file: {ex.Message}");
            return;
        }

        if (envelope == null || envelope.Entries == null)
        {
            Debug.LogWarning("[SaveSystem] Save file has no entries.");
            return;
        }

        // Build a lookup by SaveId for fast routing
        var lookup = new Dictionary<string, SaveEntry>(envelope.Entries.Count);
        foreach (var entry in envelope.Entries)
            lookup[entry.SaveId] = entry;

        foreach (var saveable in _saveables)
        {
            if (!lookup.TryGetValue(saveable.SaveId, out var entry)) continue;

            try
            {
                // Saveables declare their own DTO type via RestoreSaveData —
                // they must JsonUtility.FromJson<T> internally if needed.
                saveable.RestoreSaveData(entry.DataJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveSystem] Failed to restore '{saveable.SaveId}': {ex.Message}");
            }
        }

        Debug.Log($"[SaveSystem] Loaded from {path} (scene: {envelope.SceneId}, version: {envelope.Version})");
    }

    public bool SlotExists(string slotName = "slot0") =>
        File.Exists(GetSavePath(slotName));

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string GetSavePath(string slotName)
    {
        // Sanitize slotName to prevent path traversal if it ever becomes user-controlled.
        var safe = Regex.Replace(slotName, @"[^a-zA-Z0-9_\-]", "_");
        return Path.Combine(Application.persistentDataPath, $"save_{safe}.json");
    }
}
