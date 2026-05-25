using UnityEngine;

public enum StimulusType
{
    Sound,      // gunshots, footsteps, breaking glass
    Sight,      // line-of-sight detection (AI only polls, but broadcasts can still happen)
    Damage,     // something was hit/hurt
    Explosion,  // grenade, barrel, breaching charge
    Hunt,       // darkness timer expired — MonsterAI only, radius 999. Guards do NOT listen to this.
}

/// <summary>
/// Value object describing a single sensory event in the world.
/// Broadcast via StimulusSystem.Instance.Broadcast().
///
/// Source vs Instigator distinction:
///   Source     = the physical object generating the stimulus (bullet impact, grenade, speaker)
///   Instigator = the actor who caused it (player, AI, scripted event)
/// This lets AI say "ignore sounds I caused myself" cleanly.
/// </summary>
public readonly struct Stimulus
{
    public readonly StimulusType Type;
    public readonly Vector3      Position;
    public readonly float        Radius;
    public readonly float        Intensity;
    public readonly GameObject   Source;
    public readonly GameObject   Instigator;
    public readonly float        Timestamp;

    public Stimulus(
        StimulusType type,
        Vector3      position,
        float        radius,
        float        intensity,
        GameObject   source     = null,
        GameObject   instigator = null)
    {
        Type       = type;
        Position   = position;
        Radius     = radius;
        Intensity  = intensity;
        Source     = source;
        Instigator = instigator;
        Timestamp  = UnityEngine.Time.time;
    }
}
