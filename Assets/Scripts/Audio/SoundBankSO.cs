using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Designer-authored map from a <see cref="GameSoundId"/> to the clip(s) that should play for it.
/// One asset (e.g. "MainSoundBank") is dropped onto the <see cref="AudioDirector"/>. Swapping a
/// sound is a clip drag in the Inspector — no recompile, no code edit.
///
/// Each entry can hold several clips; one is chosen at random per play so repeated events
/// (e.g. PlayerHurt) don't sound like a copy-paste loop.
///
/// Create via: Assets ▸ Create ▸ Deadend ▸ Audio ▸ Sound Bank.
/// </summary>
[CreateAssetMenu(menuName = "Deadend/Audio/Sound Bank", fileName = "SoundBank")]
public class SoundBankSO : ScriptableObject
{
    /// <summary>One game event → its clips and playback settings.</summary>
    [Serializable]
    public class Entry
    {
        public GameSoundId id;

        [Tooltip("One is picked at random each time. Leave empty to make this event silent.")]
        public AudioClip[] clips;

        [Range(0f, 1f)]
        public float volume = 1f;

        [Tooltip("If true the cue plays at a world position (3D). If false it plays as a flat 2D " +
                 "stinger — correct for global cues like quest-complete or the death sting.")]
        public bool spatial = false;
    }

    [SerializeField] private Entry[] _entries;

    // Built lazily from _entries so lookups are O(1). Rebuilt on enable (covers domain reload).
    private Dictionary<GameSoundId, Entry> _lookup;

    private void OnEnable() => Rebuild();

    private void Rebuild()
    {
        _lookup = new Dictionary<GameSoundId, Entry>();
        if (_entries == null) return;
        foreach (var e in _entries)
        {
            if (e == null) continue;
            // Last one wins on duplicates — keeps a designer typo from throwing.
            _lookup[e.id] = e;
        }
    }

    /// <summary>True and outputs the entry if this id is mapped to at least one clip.</summary>
    public bool TryGet(GameSoundId id, out Entry entry)
    {
        if (_lookup == null) Rebuild();
        return _lookup.TryGetValue(id, out entry) && entry.clips is { Length: > 0 };
    }
}
