using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Routes Stimulus events to registered IStimulusListener objects.
///
/// Listeners self-register in OnEnable/OnDisable. Each Broadcast() takes a snapshot
/// of the listener list before iterating, so listeners can safely register or unregister
/// inside their OnStimulus callbacks without corrupting the iteration.
///
/// Performance note: the current implementation uses a flat List which is fine
/// for scenes with fewer than ~20 AI agents. When AI count grows beyond that,
/// replace _listeners with a spatial hash grid partitioned by cell size = max
/// stimulus radius. The IStimulusListener interface and Broadcast() signature
/// stay the same — only the internal dispatch changes.
/// TODO: replace _listeners with spatial hash when AI scene count > 20.
/// </summary>
public class StimulusSystem : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────

    public static StimulusSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Listener registry ──────────────────────────────────────────────────

    private readonly List<IStimulusListener> _listeners = new List<IStimulusListener>();

    public void Register(IStimulusListener listener)
    {
        if (!_listeners.Contains(listener))
            _listeners.Add(listener);
    }

    public void Unregister(IStimulusListener listener) =>
        _listeners.Remove(listener);

    // ── Broadcast ──────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcast a stimulus. All registered, enabled listeners whose type filter matches
    /// and whose position is within stimulus.Radius receive OnStimulus().
    /// Uses a local snapshot so reentrant Broadcast() calls from inside OnStimulus()
    /// do not corrupt the outer iteration.
    /// </summary>
    // Pooled snapshot lists — zero steady-state allocation. Each Broadcast() pops (or
    // creates) its own list, so reentrant Broadcast() calls from inside OnStimulus()
    // still get isolated copies and cannot corrupt the outer iteration.
    private readonly Stack<List<IStimulusListener>> _snapshotPool = new Stack<List<IStimulusListener>>();

    public void Broadcast(in Stimulus stimulus)
    {
        float sqrRadius = stimulus.Radius * stimulus.Radius;

        // Snapshot from the pool — reentrant Broadcast() calls each get their own copy.
        var snapshot = _snapshotPool.Count > 0 ? _snapshotPool.Pop()
                                               : new List<IStimulusListener>();
        snapshot.AddRange(_listeners);

        try
        {
            foreach (var listener in snapshot)
            {
                if (listener == null) continue;

                var mb = listener as MonoBehaviour;
                if (mb == null || !mb || !mb.enabled) continue;

                var listenTo = listener.ListensTo;
                if (listenTo == null || !TypeMatches(listenTo, stimulus.Type)) continue;

                float sqrDist = (mb.transform.position - stimulus.Position).sqrMagnitude;
                if (sqrDist <= sqrRadius)
                    listener.OnStimulus(in stimulus);
            }
        }
        finally
        {
            // Always return the list, even if a listener throws.
            snapshot.Clear();
            _snapshotPool.Push(snapshot);
        }
    }

    private static bool TypeMatches(StimulusType[] filter, StimulusType type)
    {
        foreach (var t in filter)
            if (t == type) return true;
        return false;
    }
}
