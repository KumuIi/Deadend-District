using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Primitive WorldStateManager writer. All other WorldState components compose this.
/// Call Write() from a UnityEvent, code, or set writeOnStart = true for immediate writes.
///
/// Supports Bool / Int / Float / String — matches WorldStateManager's full type set.
/// </summary>
public class WorldStateWriter : MonoBehaviour
{
    [Header("Key")]
    [SerializeField] private string        _key;
    [SerializeField] private QuestValueType _valueType = QuestValueType.Bool;

    [Header("Value")]
    [SerializeField] private bool   _boolValue;
    [SerializeField] private int    _intValue;
    [SerializeField] private float  _floatValue;
    [SerializeField] private string _stringValue;

    [Header("Options")]
    [Tooltip("Write immediately on Start.")]
    [SerializeField] private bool _writeOnStart;
    [Tooltip("Once written, ignore further Write() calls.")]
    [SerializeField] private bool _onlyOnce;

    [Header("Callback")]
    public UnityEvent OnWritten;

    private bool _written;

    private void Start()
    {
        if (_writeOnStart) Write();
    }

    public void Write()
    {
        if (_onlyOnce && _written) return;
        if (string.IsNullOrEmpty(_key))
        {
            Debug.LogWarning("[WorldStateWriter] Key is empty — write skipped.", this);
            return;
        }
        if (WorldStateManager.Instance == null)
        {
            Debug.LogWarning("[WorldStateWriter] WorldStateManager not found.", this);
            return;
        }

        switch (_valueType)
        {
            case QuestValueType.Bool:   WorldStateManager.Instance.SetBool(_key,   _boolValue);   break;
            case QuestValueType.Int:    WorldStateManager.Instance.SetInt(_key,    _intValue);    break;
            case QuestValueType.Float:  WorldStateManager.Instance.SetFloat(_key,  _floatValue);  break;
            case QuestValueType.String: WorldStateManager.Instance.SetString(_key, _stringValue); break;
        }

        _written = true;
        OnWritten?.Invoke();
    }

    /// <summary>Override value at runtime before writing (useful from code).</summary>
    public void WriteBool(bool value)   { _boolValue   = value; _valueType = QuestValueType.Bool;   Write(); }
    public void WriteInt(int value)     { _intValue    = value; _valueType = QuestValueType.Int;    Write(); }
    public void WriteFloat(float value) { _floatValue  = value; _valueType = QuestValueType.Float;  Write(); }
    public void WriteString(string value){ _stringValue = value; _valueType = QuestValueType.String; Write(); }
}
