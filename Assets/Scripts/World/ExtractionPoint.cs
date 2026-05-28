using UnityEngine;

/// <summary>
/// Place in a sector scene on the Interactable physics layer.
/// Player presses E to extract when they are in an active run.
/// Routes through RunManager — never calls SceneManager directly.
///
/// Implementors: one per extraction zone in each sector scene.
/// </summary>
public class ExtractionPoint : MonoBehaviour, IInteractable
{
    public bool CanInteract(GameObject interactor) =>
        RunManager.Instance != null && RunManager.Instance.State == RunManager.RunState.InRun;

    public string GetPrompt(GameObject interactor) => "Extract";

    public void Interact(GameObject interactor) =>
        RunManager.Instance?.TriggerExtract();
}
