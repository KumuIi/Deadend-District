using System;

/// <summary>
/// Tagged-union value type for WorldStateManager entries.
/// Supports bool, int, float, and string — enough for quest flags, counters, timers, and names.
/// </summary>
[Serializable]
public sealed class WorldStateValue
{
    public enum ValueType { Bool, Int, Float, String }

    public readonly ValueType Type;

    private readonly bool   _bool;
    private readonly int    _int;
    private readonly float  _float;
    private readonly string _string;

    private WorldStateValue(ValueType type, bool b = false, int i = 0, float f = 0f, string s = null)
    {
        Type    = type;
        _bool   = b;
        _int    = i;
        _float  = f;
        _string = s;
    }

    public static WorldStateValue FromBool(bool v)     => new WorldStateValue(ValueType.Bool,   b: v);
    public static WorldStateValue FromInt(int v)       => new WorldStateValue(ValueType.Int,    i: v);
    public static WorldStateValue FromFloat(float v)   => new WorldStateValue(ValueType.Float,  f: v);
    public static WorldStateValue FromString(string v) => new WorldStateValue(ValueType.String, s: v);

    public bool   AsBool()   => _bool;
    public int    AsInt()    => _int;
    public float  AsFloat()  => _float;
    public string AsString() => _string;

    // Serialization helpers for SaveSystem
    public object RawValue() => Type switch
    {
        ValueType.Bool   => (object)_bool,
        ValueType.Int    => _int,
        ValueType.Float  => _float,
        ValueType.String => _string,
        _                => null,
    };

    public override string ToString() => $"[{Type}:{RawValue()}]";
}
