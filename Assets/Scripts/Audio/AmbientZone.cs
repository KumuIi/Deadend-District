using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// A volume of space with its own looping ambience (wind, dripping water, factory hum, a distant dog).
/// While the player is inside the trigger the loops fade UP; on exit they fade back DOWN — they never
/// hard-cut, so walking a corridor cross-fades one soundscape into the next.
///
/// Zones are independent and overlap freely: stand where a "sewer drip" zone and a "factory hum" zone
/// meet and you hear both, each at its own level. This is the ambient half of the layering design —
/// it runs entirely separate from <see cref="MusicDirector"/>, so combat music never silences the room.
///
/// Loops are authored as child AudioSources (drag them into <see cref="_loops"/>). Each source's
/// volume set in the Inspector is treated as its "inside" level; the zone fades between that and 0.
///
/// Setup: a trigger Collider on this object + one or more looping AudioSource children. Tag the player
/// "Player" (or set <see cref="_playerTag"/>).
/// </summary>
[RequireComponent(typeof(Collider))]
public class AmbientZone : MonoBehaviour
{
    [Header("Ambience")]
    [Tooltip("Looping AudioSources to fade with this zone. Their authored volume is the 'inside' level.")]
    [SerializeField] private AudioSource[] _loops;
    [Tooltip("Route loops here. If null, falls back to SpatialAudioManager.AmbientGroup.")]
    [SerializeField] private AudioMixerGroup _ambientGroup;

    [Header("Detection")]
    [SerializeField] private string _playerTag = "Player";

    [Header("Feel")]
    [Tooltip("Volume units per second the loops fade in/out. Lower = slower, gentler transitions.")]
    [SerializeField] private float _fadeSpeed = 0.5f;

    private float[] _insideVolume; // authored "full" level captured at Awake
    private bool    _playerInside;

    // ── Lifecycle ──────────────────────────────────────────────────

    private void Awake()
    {
        AudioMixerGroup group = _ambientGroup != null
            ? _ambientGroup
            : SpatialAudioManager.Instance != null ? SpatialAudioManager.Instance.AmbientGroup : null;

        if (_loops == null) return;
        _insideVolume = new float[_loops.Length];
        for (int i = 0; i < _loops.Length; i++)
        {
            var src = _loops[i];
            if (src == null) continue;
            _insideVolume[i] = src.volume; // remember the designer's intended level
            src.loop                  = true;
            src.playOnAwake           = false;
            src.volume                = 0f;  // start silent; fade up only when entered
            if (group != null) src.outputAudioMixerGroup = group;
            src.Play();                       // always running; volume gates audibility
        }
    }

    private void Update()
    {
        if (_loops == null) return;
        float step = _fadeSpeed * Time.deltaTime;
        for (int i = 0; i < _loops.Length; i++)
        {
            var src = _loops[i];
            if (src == null) continue;
            float target = _playerInside ? _insideVolume[i] : 0f;
            src.volume = Mathf.MoveTowards(src.volume, target, step);
        }
    }

    // ── Triggers ───────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(_playerTag)) _playerInside = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(_playerTag)) _playerInside = false;
    }
}
