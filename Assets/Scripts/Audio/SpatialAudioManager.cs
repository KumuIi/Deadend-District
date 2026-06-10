using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// One-shot audio playback service (W5-01). Owns a small pool of <see cref="AudioSource"/>s so any
/// system can fire a sound without carrying its own source — call <see cref="PlayAt"/> for a 3D
/// world sound or <see cref="Play2D"/> for a flat UI/stinger.
///
/// Why a pool: spawning + destroying an AudioSource per shot churns the GC and can clip the tail of
/// short sounds. Instead we keep N reusable sources and hand out whichever one is free, growing the
/// pool only if every source is busy at once.
///
/// Routing: every play takes an <see cref="AudioMixerGroup"/> so the sound lands on the right mixer
/// channel (SFX / UI / …). Group refs are optional — if unassigned the sound still plays, just on the
/// default group, so nothing goes silent while the mixer is still being wired.
///
/// Implementors: one instance on the GameSystems GameObject (DontDestroyOnLoad).
/// </summary>
public class SpatialAudioManager : MonoBehaviour
{
    public static SpatialAudioManager Instance { get; private set; }

    [Header("Mixer Routing (optional until the mixer asset is wired)")]
    [SerializeField] private AudioMixer      _mixer;
    [SerializeField] private AudioMixerGroup _sfxGroup;
    [SerializeField] private AudioMixerGroup _uiGroup;
    [Tooltip("Exposed so the music/ambient directors can grab their own group without a second ref.")]
    [SerializeField] private AudioMixerGroup _musicGroup;
    [SerializeField] private AudioMixerGroup _ambientGroup;

    [Header("Pool")]
    [Tooltip("Voices created up front. The pool grows past this only if every voice is busy at once.")]
    [SerializeField] private int _initialPoolSize = 12;

    [Header("3D Falloff")]
    [Tooltip("Below this distance a world sound is at full volume.")]
    [SerializeField] private float _minDistance = 1.5f;
    [Tooltip("Beyond this distance a world sound is inaudible.")]
    [SerializeField] private float _maxDistance = 35f;

    public AudioMixerGroup MusicGroup   => _musicGroup;
    public AudioMixerGroup AmbientGroup => _ambientGroup;

    private readonly List<AudioSource> _pool = new List<AudioSource>();

    // ── Lifecycle ──────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < _initialPoolSize; i++)
            CreateVoice();
    }

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>Play a clip at a world position (3D, attenuated by distance).</summary>
    public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, AudioMixerGroup group = null)
    {
        if (clip == null) return;
        var src = GetFreeVoice();
        src.transform.position = position;
        Configure(src, clip, volume, spatial: true, group ? group : _sfxGroup);
        src.Play();
    }

    /// <summary>Play a flat, non-positional clip — correct for UI and global stingers.</summary>
    public void Play2D(AudioClip clip, float volume = 1f, AudioMixerGroup group = null)
    {
        if (clip == null) return;
        var src = GetFreeVoice();
        Configure(src, clip, volume, spatial: false, group ? group : _uiGroup);
        src.Play();
    }

    /// <summary>
    /// Convenience overload that plays a <see cref="SoundBankSO.Entry"/>: picks a random clip and
    /// honours the entry's spatial flag (PlayAt vs Play2D) and volume.
    /// </summary>
    public void PlayCue(SoundBankSO.Entry entry, Vector3 position)
    {
        if (entry?.clips == null || entry.clips.Length == 0) return;
        var clip = entry.clips[Random.Range(0, entry.clips.Length)];
        if (entry.spatial) PlayAt(clip, position, entry.volume, _sfxGroup);
        else               Play2D(clip, entry.volume, _uiGroup);
    }

    // ── Settings hooks ─────────────────────────────────────────────

    /// <summary>
    /// Set a mixer channel's volume from a 0..1 slider. The mixer works in decibels, so we convert
    /// linear→dB logarithmically (a straight linear map sounds wrong — perceived loudness is roughly
    /// logarithmic). <paramref name="exposedParam"/> is the name you exposed on the mixer, e.g.
    /// "MasterVolume". Returns false if the mixer or parameter isn't set up yet.
    /// </summary>
    public bool SetCategoryVolume01(string exposedParam, float linear01)
    {
        if (_mixer == null || string.IsNullOrEmpty(exposedParam)) return false;
        float dB = Mathf.Log10(Mathf.Clamp(linear01, 0.0001f, 1f)) * 20f;
        return _mixer.SetFloat(exposedParam, dB);
    }

    // ── Pool internals ─────────────────────────────────────────────

    private AudioSource CreateVoice()
    {
        var go = new GameObject($"Voice_{_pool.Count}");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.rolloffMode = AudioRolloffMode.Linear; // predictable falloff between min/max distance
        _pool.Add(src);
        return src;
    }

    private AudioSource GetFreeVoice()
    {
        foreach (var src in _pool)
            if (!src.isPlaying) return src;
        // Every voice busy — grow rather than cut an in-flight sound.
        return CreateVoice();
    }

    private void Configure(AudioSource src, AudioClip clip, float volume, bool spatial, AudioMixerGroup group)
    {
        src.clip                 = clip;
        src.volume               = Mathf.Clamp01(volume);
        src.spatialBlend         = spatial ? 1f : 0f;
        src.minDistance          = _minDistance;
        src.maxDistance          = _maxDistance;
        src.outputAudioMixerGroup = group;
        src.loop                 = false;
    }
}
