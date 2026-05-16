using UnityEngine;

/// <summary>
/// Implement on any MonoBehaviour that the player (or an AI) can interact with.
/// Doors, pickups, terminals, extraction points, and quest triggers all use this contract.
/// </summary>
public interface IInteractable
{
    /// <summary>Returns true when this object is currently usable by <paramref name="interactor"/>.</summary>
    bool CanInteract(GameObject interactor);

    /// <summary>
    /// Returns the context-sensitive prompt string shown in the HUD crosshair.
    /// May vary based on interactor inventory, world flags, or faction.
    /// </summary>
    string GetPrompt(GameObject interactor);

    /// <summary>Executes the interaction. Only called when <see cref="CanInteract"/> returns true.</summary>
    void Interact(GameObject interactor);
}
