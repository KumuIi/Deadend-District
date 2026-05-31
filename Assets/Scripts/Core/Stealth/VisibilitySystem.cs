using UnityEngine;

/// <summary>
/// "VisibilitySystem" is a *pattern*, not a component: anything that affects how visible
/// the player is implements <see cref="IVisibilityContributor"/> and registers with the
/// player's <see cref="PlayerVisibility"/>. This static class is just the read-side
/// convenience AI uses, so callers don't each have to null-check the singleton.
/// </summary>
public static class VisibilitySystem
{
    /// <summary>
    /// Current aggregate player visibility in [0..1]. Returns <paramref name="fallback"/>
    /// when no PlayerVisibility exists yet (scene still loading, no player spawned).
    /// </summary>
    public static float PlayerScore(float fallback = 1f) =>
        PlayerVisibility.Instance != null ? PlayerVisibility.Instance.Score : fallback;
}
