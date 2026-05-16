using System;
using System.Collections.Generic;

/// <summary>Root JSON object written to disk for each save slot.</summary>
[Serializable]
public class SaveEnvelope
{
    /// <summary>
    /// Increment this when the save format changes in a breaking way.
    /// A future migration system can key off this to run upgrade paths.
    /// </summary>
    public int    Version  = 1;
    public string SceneId  = "";
    public string SaveTime = "";

    public List<SaveEntry> Entries = new List<SaveEntry>();
}

/// <summary>One saveable system's data blob inside a SaveEnvelope.</summary>
[Serializable]
public class SaveEntry
{
    public string SaveId;
    public string SaveType;
    /// <summary>JSON-encoded DTO string. Double-encoded so the envelope itself is still valid JSON.</summary>
    public string DataJson;
}
