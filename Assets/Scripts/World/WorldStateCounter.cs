using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Increments an integer key in WorldStateManager and fires an event at a threshold.
/// Has no local count cache — WSM is always the source of truth, so save/load is free.
///
/// Call Increment() from a UnityEvent or code (e.g. enemy death, item collected).
/// Example: set _wsmKey = "combat.raiders_killed", _threshold = 5.
/// </summary>
public class WorldStateCounter : MonoBehaviour
{
    [SerializeField] private string _wsmKey;
    [Tooltip("Starting value written to WSM on Start if the key doesn't exist yet.")]
    [SerializeField] private int    _initialValue;
    [Tooltip("Fire OnThresholdReached when count reaches this. 0 = disabled.")]
    [SerializeField] private int    _threshold;

    public UnityEvent OnThresholdReached;

    private void Start()
    {
        if (string.IsNullOrEmpty(_wsmKey) || WorldStateManager.Instance == null) return;
        // Only write initial value if the key hasn't been set yet (e.g. new game).
        if (!WorldStateManager.Instance.HasKey(_wsmKey))
            WorldStateManager.Instance.SetInt(_wsmKey, _initialValue);
    }

    public void Increment(int amount = 1)
    {
        int next = ReadCount() + amount;
        WriteCount(next);
    }

    public void Decrement(int amount = 1) => Increment(-amount);

    public void ResetCount()              => WriteCount(_initialValue);

    public int CurrentCount               => ReadCount();

    private int ReadCount()
    {
        if (string.IsNullOrEmpty(_wsmKey) || WorldStateManager.Instance == null) return _initialValue;
        return WorldStateManager.Instance.GetInt(_wsmKey, _initialValue);
    }

    private void WriteCount(int value)
    {
        if (string.IsNullOrEmpty(_wsmKey) || WorldStateManager.Instance == null) return;
        int previous = WorldStateManager.Instance.GetInt(_wsmKey, _initialValue);
        WorldStateManager.Instance.SetInt(_wsmKey, value);
        // Only fire on crossing the threshold, not on every write above it.
        if (_threshold > 0 && previous < _threshold && value >= _threshold)
            OnThresholdReached?.Invoke();
    }
}
