/// <summary>
/// A factor that contributes to how visible the player is to enemies.
/// Implementors: active light sources near player, movement speed, crouch state,
///               dark clothing (Wave 5 armour), stealth cloak augment.
/// PlayerVisibility (Wave 3) aggregates all registered contributors.
/// </summary>
public interface IVisibilityContributor
{
    /// <summary>[0..1] — 0 = contributes nothing to visibility, 1 = fully visible contribution.</summary>
    float GetVisibilityFactor();

    /// <summary>Shown in the debug visibility HUD.</summary>
    string ContributorName { get; }
}
