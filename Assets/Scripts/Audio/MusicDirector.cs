using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Adaptive, vertically-layered music (W5-23). Instead of hard-cutting between tracks, four looping
/// stems play on their own voices and each fades toward a target volume every frame:
///
///   Hub      — the safe "home" theme. Up only in the hub.
///   Explore  — the run/sewer bed. The base layer of any run; stays audible the whole time.
///   Tension  — a low pulse layered ON TOP of Explore when a guard is searching / heard you.
///   Combat   — the danger layer, fades in when a guard locks on. Explore DUCKS underneath it
///              (drops to <see cref="_exploreDuckVolume"/>) but never goes silent — so the world
///              keeps breathing under the fight, per the layering design.
///
/// Intensity comes from <see cref="RunManager.State"/> (hub vs run) and <see cref="EnemyThreatRegistry"/>
/// (none / tension / combat). Nothing in gameplay code needs to know music exists.
///
/// Assign the four clips in the Inspector; an empty slot just means that layer is silent (no error).
/// Implementors: one instance on the GameSystems GameObject.
/// </summary>
public class MusicDirector : MonoBehaviour
{
    [Header("Stems (assign your tracks; empty = silent layer)")]
    [SerializeField] private AudioClip _hubTheme;
    [SerializeField] private AudioClip _exploreTheme;   // the "sewer" bed
    [SerializeField] private AudioClip _tensionLayer;
    [SerializeField] private AudioClip _combatLayer;

    [Header("Routing")]
    [Tooltip("Music mixer group. If left null, falls back to SpatialAudioManager.MusicGroup.")]
    [SerializeField] private AudioMixerGroup _musicGroup;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] private float _hubVolume     = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _exploreVolume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float _tensionVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _combatVolume  = 0.85f;
    [Tooltip("Explore bed level while Combat is active — ducked, NOT silenced, so ambience persists.")]
    [SerializeField, Range(0f, 1f)] private float _exploreDuckVolume = 0.25f;

    [Header("Feel")]
    [Tooltip("Volume units per second each layer moves toward its target. Lower = slower, dreamier fades.")]
    [SerializeField] private float _fadeSpeed = 0.6f;
    [Tooltip("How often (seconds) to re-evaluate threat. Cheap — no need to poll every frame.")]
    [SerializeField] private float _evalInterval = 0.25f;

    [Header("Stinger on combat transitions (optional)")]
    [Tooltip("AudioDirector that owns the SoundBank — used to fire CombatStart/CombatEnd cues.")]
    [SerializeField] private AudioDirector _audioDirector;

    // ── Internal stem voices ───────────────────────────────────────

    private Layer _hub, _explore, _tension, _combat;
    private float _evalTimer;
    private EnemyThreatRegistry.Threat _lastThreat = EnemyThreatRegistry.Threat.None;

    /// <summary>A single looping music stem: its source plus the volume it is gliding toward.</summary>
    private class Layer
    {
        public AudioSource source;
        public float target;
    }

    // ── Lifecycle ──────────────────────────────────────────────────

    private void Awake()
    {
        AudioMixerGroup group = _musicGroup != null
            ? _musicGroup
            : SpatialAudioManager.Instance != null ? SpatialAudioManager.Instance.MusicGroup : null;

        _hub     = MakeLayer("Hub",     _hubTheme,     group);
        _explore = MakeLayer("Explore", _exploreTheme, group);
        _tension = MakeLayer("Tension", _tensionLayer, group);
        _combat  = MakeLayer("Combat",  _combatLayer,  group);
    }

    private Layer MakeLayer(string label, AudioClip clip, AudioMixerGroup group)
    {
        var go = new GameObject($"Music_{label}");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip                  = clip;
        src.loop                  = true;
        src.playOnAwake           = false;
        src.spatialBlend          = 0f;       // music is always 2D
        src.volume                = 0f;       // everyone starts silent, fades up
        src.outputAudioMixerGroup = group;
        if (clip != null) src.Play();          // all stems run in sync; volume gates what you hear
        return new Layer { source = src, target = 0f };
    }

    private void Update()
    {
        _evalTimer += Time.unscaledDeltaTime; // music keeps mixing even while the game is paused
        if (_evalTimer >= _evalInterval)
        {
            _evalTimer = 0f;
            EvaluateTargets();
        }

        float step = _fadeSpeed * Time.unscaledDeltaTime;
        Glide(_hub,     step);
        Glide(_explore, step);
        Glide(_tension, step);
        Glide(_combat,  step);
    }

    // ── Mix logic ──────────────────────────────────────────────────

    private void EvaluateTargets()
    {
        var run = RunManager.Instance;
        bool inHub = run == null || run.State == RunManager.RunState.InHub;

        if (inHub)
        {
            SetTargets(hub: _hubVolume, explore: 0f, tension: 0f, combat: 0f);
            ReportThreat(EnemyThreatRegistry.Threat.None);
            return;
        }

        if (run.State == RunManager.RunState.Dead)
        {
            // Let the death stinger own the moment — pull every layer down.
            SetTargets(hub: 0f, explore: 0f, tension: 0f, combat: 0f);
            ReportThreat(EnemyThreatRegistry.Threat.None);
            return;
        }

        // In a run (or extracting): explore bed is the constant base; threat layers ride on top.
        var threat = EnemyThreatRegistry.Evaluate();
        switch (threat)
        {
            case EnemyThreatRegistry.Threat.Combat:
                SetTargets(hub: 0f, explore: _exploreDuckVolume, tension: 0f, combat: _combatVolume);
                break;
            case EnemyThreatRegistry.Threat.Tension:
                SetTargets(hub: 0f, explore: _exploreVolume, tension: _tensionVolume, combat: 0f);
                break;
            default:
                SetTargets(hub: 0f, explore: _exploreVolume, tension: 0f, combat: 0f);
                break;
        }
        ReportThreat(threat);
    }

    private void SetTargets(float hub, float explore, float tension, float combat)
    {
        _hub.target     = hub;
        _explore.target = explore;
        _tension.target = tension;
        _combat.target  = combat;
    }

    /// <summary>Fire a one-shot cue when crossing into or out of Combat, so the shift has a punch.</summary>
    private void ReportThreat(EnemyThreatRegistry.Threat threat)
    {
        if (threat == _lastThreat) return;

        bool wasCombat = _lastThreat == EnemyThreatRegistry.Threat.Combat;
        bool nowCombat = threat == EnemyThreatRegistry.Threat.Combat;

        if (_audioDirector != null)
        {
            if (nowCombat && !wasCombat) _audioDirector.Play(GameSoundId.CombatStart);
            else if (wasCombat && !nowCombat) _audioDirector.Play(GameSoundId.CombatEnd);
        }

        _lastThreat = threat;
    }

    private static void Glide(Layer layer, float step)
    {
        if (layer?.source == null || layer.source.clip == null) return;
        layer.source.volume = Mathf.MoveTowards(layer.source.volume, layer.target, step);
    }
}
