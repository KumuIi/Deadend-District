using System;

/// <summary>
/// Flat metadata written alongside every save as save_{slot}_meta.json.
/// Used only for UI display (save slot hover) — never drives gameplay logic.
/// Keep fields simple and serializable; no Unity types.
/// </summary>
[Serializable]
public class SaveSlotMetadata
{
    /// <summary>Scene name at time of save (e.g. "Hub", "Sector1").</summary>
    public string SceneId = "";

    /// <summary>UTC ISO-8601 timestamp of the last save.</summary>
    public string SaveTime = "";

    /// <summary>Total seconds played across all sessions (Profile-scoped).</summary>
    public float PlaySeconds = 0f;

    /// <summary>Credits held at time of save.</summary>
    public int Credits = 0;
}
