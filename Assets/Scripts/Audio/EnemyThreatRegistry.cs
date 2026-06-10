using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aggregates how threatened the player is, derived from every live <see cref="EnemyPerception"/>.
/// The <see cref="MusicDirector"/> reads <see cref="Evaluate"/> each tick to pick a music intensity
/// WITHOUT each enemy needing to know the music exists.
///
/// Enemies register themselves in <c>EnemyPerception.OnEnable</c> (alongside the StimulusSystem
/// registration already there) and drop out in <c>OnDisable</c> — the same lifetime as the AI, so a
/// dead or pooled-away enemy stops contributing automatically.
///
/// Static because it is a pure read-only aggregator with no scene state of its own; it never needs
/// to persist or be wired in the Inspector.
/// </summary>
public static class EnemyThreatRegistry
{
    public enum Threat { None, Tension, Combat }

    [Tooltip("Seconds a heard sound keeps a guard at 'tension' after it stops.")]
    private const float TensionMemory = 6f;

    private static readonly List<EnemyPerception> _perceptions = new List<EnemyPerception>();
    private static readonly List<AIPerception>     _aiPerceptions = new List<AIPerception>();

    public static void Register(EnemyPerception p)
    {
        if (p != null && !_perceptions.Contains(p)) _perceptions.Add(p);
    }

    public static void Unregister(EnemyPerception p) => _perceptions.Remove(p);

    // Overloads for the mimic / basic-AI perception, which has its own state machine.
    public static void Register(AIPerception p)
    {
        if (p != null && !_aiPerceptions.Contains(p)) _aiPerceptions.Add(p);
    }

    public static void Unregister(AIPerception p) => _aiPerceptions.Remove(p);

    /// <summary>
    /// Highest threat across all enemies:
    ///   Combat  — at least one enemy can currently see the player (a fight is on).
    ///   Tension — an enemy just lost sight and is searching, or heard something recently.
    ///   None    — nobody is alerted.
    /// Combat short-circuits the scan — a single hunter outranks any amount of mild suspicion.
    /// </summary>
    public static Threat Evaluate()
    {
        Threat highest = Threat.None;

        for (int i = _perceptions.Count - 1; i >= 0; i--)
        {
            var p = _perceptions[i];
            if (p == null) { _perceptions.RemoveAt(i); continue; }
            if (!p.isActiveAndEnabled) continue;

            if (p.CanSeeTarget)
                return Threat.Combat; // can't get higher — done

            bool searching     = p.IsInHotMode && p.LostSightTimer < p.LostSightTimeout;
            bool heardRecently = p.LastHeardTime > 0f && (Time.time - p.LastHeardTime) < TensionMemory;

            if (searching || heardRecently)
                highest = Threat.Tension;
        }

        // Mimic / basic AI: Alert & Combat states are a live hunt; Investigate is mere suspicion.
        for (int i = _aiPerceptions.Count - 1; i >= 0; i--)
        {
            var p = _aiPerceptions[i];
            if (p == null) { _aiPerceptions.RemoveAt(i); continue; }
            if (!p.isActiveAndEnabled) continue;

            if (p.State == AIPerception.AIState.Alert || p.State == AIPerception.AIState.Combat)
                return Threat.Combat;
            if (p.State == AIPerception.AIState.Investigate)
                highest = Threat.Tension;
        }

        return highest;
    }
}
