using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reads and writes the flat save_{slot}_meta.json sidecar file.
/// Called by SaveSystem after every save scope write, and by SaveSlotButton3D on hover.
/// Never touches SaveSystem's main envelope files.
/// </summary>
public static class SaveMetadataIO
{
    // ── Write ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot current runtime state into a sidecar file.
    /// Called from SaveSystem after any scope is saved.
    /// </summary>
    public static void Write(string slotName)
    {
        // Preserve previous credits if WSM is absent (e.g. called from a scene without WorldStateManager)
        // to avoid overwriting valid metadata with zeros.
        var previous = Read(slotName);
        int credits = WorldStateManager.Instance != null
            ? WorldStateManager.Instance.GetInt("economy.credits")
            : (previous?.Credits ?? 0);

        var meta = new SaveSlotMetadata
        {
            SceneId     = SceneManager.GetActiveScene().name,
            SaveTime    = DateTime.UtcNow.ToString("o"),
            PlaySeconds = PlaytimeTracker.Instance != null ? PlaytimeTracker.Instance.TotalSeconds : (previous?.PlaySeconds ?? 0f),
            Credits     = credits,
        };

        string path = GetPath(slotName);
        File.WriteAllText(path, JsonUtility.ToJson(meta, prettyPrint: false));
    }

    // ── Read ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads sidecar metadata for <paramref name="slotName"/> without touching SaveSystem.
    /// Returns null if no sidecar exists for this slot.
    /// </summary>
    public static SaveSlotMetadata Read(string slotName)
    {
        string path = GetPath(slotName);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonUtility.FromJson<SaveSlotMetadata>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveMetadataIO] Failed to read '{path}': {ex.Message}");
            return null;
        }
    }

    public static bool Exists(string slotName) => File.Exists(GetPath(slotName));

    /// <summary>
    /// Scans all save sidecar files and returns the slot name with the most recent SaveTime.
    /// Returns null if no saves exist.
    /// </summary>
    public static string FindMostRecentSlot()
    {
        string[] files;
        try { files = Directory.GetFiles(Application.persistentDataPath, "save_*_meta.json"); }
        catch { return null; }

        string   bestSlot = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (var file in files)
        {
            string baseName = Path.GetFileNameWithoutExtension(file); // "save_slot0_meta"
            if (!baseName.StartsWith("save_") || !baseName.EndsWith("_meta")) continue;
            string slotName = baseName.Substring(5, baseName.Length - 10); // strip "save_" and "_meta"

            var meta = Read(slotName);
            if (meta == null) continue;

            if (DateTime.TryParse(meta.SaveTime, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt) && dt > bestTime)
            {
                bestTime = dt;
                bestSlot = slotName;
            }
        }

        return bestSlot;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string GetPath(string slotName)
    {
        var safe = Regex.Replace(slotName, @"[^a-zA-Z0-9_\-]", "_");
        return Path.Combine(Application.persistentDataPath, $"save_{safe}_meta.json");
    }
}
