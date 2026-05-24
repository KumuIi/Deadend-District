/// <summary>
/// Contract for any system that participates in save/load.
/// Register with SaveSystem in Start() (not OnEnable — SaveSystem.Instance must be initialized first), unregister in OnDisable.
///
/// SaveId must be stable across scenes and code changes — do NOT use
/// GameObject instance IDs, scene hierarchy paths, or random GUIDs.
/// Use meaningful string literals: "player.inventory", "player.health", "world.state".
/// </summary>
public interface ISaveable
{
    /// <summary>Unique, human-readable identifier for this saveable's data slot.</summary>
    string SaveId { get; }

    /// <summary>Type tag used to route data back to the correct adapter on load.</summary>
    string SaveType { get; }

    /// <summary>
    /// Determines when this data is reset relative to run lifecycle.
    /// Profile = survives all runs. Run = clears on death/extract. World = major persistent flags. Temp = resets on sector reload.
    /// </summary>
    RunScopeTag SaveScope { get; }

    /// <summary>Called by SaveSystem when writing a save file. Return a serializable DTO.</summary>
    object CaptureSaveData();

    /// <summary>Called by SaveSystem when loading. Receives the previously captured DTO.</summary>
    void RestoreSaveData(object data);
}
