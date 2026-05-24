using System;
using UnityEngine;

/// <summary>
/// Countdown timer that writes a WorldStateManager key on expiry.
/// ISaveable — persists remaining time and running state across save/load.
/// Requires a unique, stable _saveId per instance (inspector-assigned).
///
/// Call StartTimer() from code or a UnityEvent to begin. Pause/resume via IsRunning.
/// </summary>
[RequireComponent(typeof(WorldStateWriter))]
public class WorldStateTimer : MonoBehaviour, ISaveable
{
    [SerializeField] private float  _duration = 60f;
    [SerializeField] private bool   _startOnAwake;
    [Tooltip("Stable unique id for save/load. Must be different for every timer in the scene.")]
    [SerializeField] private string _saveId = "timer.unnamed";

    private WorldStateWriter _writer;
    private float            _remaining;
    private bool             _running;
    private bool             _expired;

    public float Remaining => _remaining;
    public bool  IsRunning => _running;
    public bool  IsExpired => _expired;

    // ── ISaveable ────────────────────────────────────────────────────────────

    public string      SaveId    => _saveId;
    public string      SaveType  => "WorldStateTimer";
    public RunScopeTag SaveScope => RunScopeTag.World;

    private void Start()
    {
        _writer = GetComponent<WorldStateWriter>();
        SaveSystem.Instance?.Register(this);   // guaranteed non-null by start order
        if (_startOnAwake) StartTimer();
    }

    private void OnEnable()  => SaveSystem.Instance?.Register(this);  // re-register after disable
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData() =>
        new TimerDTO { remaining = _remaining, running = _running, expired = _expired };

    public void RestoreSaveData(object data)
    {
        var dto = JsonUtility.FromJson<TimerDTO>((string)data);
        if (dto == null) return;
        _remaining = dto.remaining;
        _running   = dto.running;
        _expired   = dto.expired;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void StartTimer()
    {
        if (_expired) return;
        _remaining = _duration;
        _running   = true;
    }

    public void PauseTimer()  => _running = false;
    public void ResumeTimer() => _running = true;

    public void ResetTimer()
    {
        _remaining = _duration;
        _running   = false;
        _expired   = false;
    }

    // ── Unity ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_running || _expired) return;

        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
        {
            _remaining = 0f;
            _running   = false;
            _expired   = true;
            _writer?.Write();
        }
    }

    [Serializable]
    private class TimerDTO { public float remaining; public bool running; public bool expired; }
}
