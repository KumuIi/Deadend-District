using UnityEngine;

/// <summary>
/// Marks the point on the player that enemies must have a clear line of sight to
/// in order to "see" the player — i.e. the head/eyes. Put this on the player's
/// head bone or camera holder.
///
/// Publishes itself statically so <see cref="EnemyPerception"/> can target it
/// without a GetComponent walk through the player hierarchy (the guard finds the
/// player at runtime, so it can't be Inspector-wired per-guard). Single-player,
/// so a single static reference is sufficient; the last enabled instance wins.
/// </summary>
public class PlayerSightPoint : MonoBehaviour
{
    /// <summary>The active player sight point, or null if none is enabled.</summary>
    public static Transform Current { get; private set; }

    private void OnEnable() => Current = transform;

    private void OnDisable()
    {
        if (Current == transform) Current = null;
    }
}
