using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridges WorldStateManager into the ISaveable system.
/// Attach to the same GameObject as WorldStateManager.
/// </summary>
public class WorldStateSaveAdapter : MonoBehaviour, ISaveable
{
    public string      SaveId    => "world.state";
    public string      SaveType  => "WorldState";
    public RunScopeTag SaveScope => RunScopeTag.World;

    private void Start()
    {
        // Register in Start, not OnEnable — guarantees SaveSystem.Instance
        // exists (initialized in Awake) before adapters attempt to register.
        SaveSystem.Instance?.Register(this);
    }

    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        var mgr = WorldStateManager.Instance;
        if (mgr == null) throw new InvalidOperationException("WorldStateManager not found.");

        var dto = new WorldStateSaveData();
        foreach (var kv in mgr.GetAllState())
        {
            var v = kv.Value;
            dto.entries.Add(new WorldStateEntry
            {
                key       = kv.Key,
                valueType = (int)v.Type,
                boolVal   = v.Type == WorldStateValue.ValueType.Bool   ? v.AsBool()   : false,
                intVal    = v.Type == WorldStateValue.ValueType.Int    ? v.AsInt()    : 0,
                floatVal  = v.Type == WorldStateValue.ValueType.Float  ? v.AsFloat()  : 0f,
                stringVal = v.Type == WorldStateValue.ValueType.String ? v.AsString() : "",
            });
        }
        return dto;
    }

    public void RestoreSaveData(object data)
    {
        var mgr = WorldStateManager.Instance;
        if (mgr == null) return;

        var dto = JsonUtility.FromJson<WorldStateSaveData>((string)data);
        if (dto?.entries == null) return;

        var rebuilt = new Dictionary<string, WorldStateValue>(dto.entries.Count);
        foreach (var e in dto.entries)
        {
            WorldStateValue v = (WorldStateValue.ValueType)e.valueType switch
            {
                WorldStateValue.ValueType.Bool   => WorldStateValue.FromBool(e.boolVal),
                WorldStateValue.ValueType.Int    => WorldStateValue.FromInt(e.intVal),
                WorldStateValue.ValueType.Float  => WorldStateValue.FromFloat(e.floatVal),
                WorldStateValue.ValueType.String => WorldStateValue.FromString(e.stringVal),
                _                               => WorldStateValue.FromBool(false),
            };
            rebuilt[e.key] = v;
        }

        mgr.LoadState(rebuilt);
    }
}

[Serializable]
public class WorldStateSaveData
{
    public List<WorldStateEntry> entries = new List<WorldStateEntry>();
}

[Serializable]
public class WorldStateEntry
{
    public string key;
    public int    valueType;
    public bool   boolVal;
    public int    intVal;
    public float  floatVal;
    public string stringVal;
}
