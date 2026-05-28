using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Accumulates total seconds played. Pauses while gameplay is blocked (menus, pause).
/// DontDestroyOnLoad singleton so it survives scene transitions.
/// ISaveable with Profile scope so playtime survives deaths.
///
/// Implementors: one instance on the GameSystems GameObject.
/// </summary>
public class PlaytimeTracker : MonoBehaviour, ISaveable
{
    public static PlaytimeTracker Instance { get; private set; }

    [SerializeField] private string _mainMenuScene = "MainMenu";

    public string      SaveId    => "player.playtime";
    public string      SaveType  => "Playtime";
    public RunScopeTag SaveScope => RunScopeTag.Profile;

    private float _seconds;

    public float TotalSeconds => _seconds;

    public string FormattedTime
    {
        get
        {
            int h = (int)(_seconds / 3600);
            int m = (int)((_seconds % 3600) / 60);
            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start() => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    private void Update()
    {
        if (GameInputState.GameplayBlocked) return;
        if (SceneManager.GetActiveScene().name == _mainMenuScene) return;
        if (RunManager.Instance == null) return;
        var state = RunManager.Instance.State;
        if (state == RunManager.RunState.Dead || state == RunManager.RunState.Extracting) return;
        _seconds += Time.deltaTime;
    }

    public object CaptureSaveData() => new PlaytimeDTO { seconds = _seconds };

    public void RestoreSaveData(object data)
    {
        var dto = JsonUtility.FromJson<PlaytimeDTO>((string)data);
        if (dto != null) _seconds = Mathf.Max(0f, dto.seconds);
    }

    [System.Serializable]
    private class PlaytimeDTO { public float seconds; }
}
