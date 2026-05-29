using UnityEngine;

/// <summary>
/// The hub world object the player interacts with to open the stash.
/// Place on a chest/locker GameObject on the Interactable physics layer (so PlayerInteractor
/// finds it). All logic is delegated to the StashSystem singleton.
///
/// Implementors: one per stash access point — typically a single chest in the hub.
/// </summary>
public class StashChest : MonoBehaviour, IInteractable
{
    public bool CanInteract(GameObject interactor) =>
        StashSystem.Instance != null &&
        StashSystem.Instance.CanAccess() &&
        !StashSystem.Instance.IsOpen;

    public string GetPrompt(GameObject interactor) => "Open Stash";

    public void Interact(GameObject interactor) => StashSystem.Instance?.Open(interactor);
}
