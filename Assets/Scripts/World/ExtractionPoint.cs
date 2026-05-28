using UnityEngine;

/// <summary>
/// Place in any sector on the Interactable physics layer.
/// Leave _destinationScene empty to extract back to hub (RunManager.TriggerExtract).
/// Fill it in to transition to another sector mid-run (SceneTransitionManager.LoadSector).
///
/// Implementors: one per extraction/transition zone in each sector scene.
/// </summary>
public class ExtractionPoint : MonoBehaviour, IInteractable
{
    [Tooltip("Leave empty to extract to hub. Set to a scene name to transition to another sector.")]
    [SerializeField] private string _destinationScene = "";

    public bool CanInteract(GameObject interactor) =>
        RunManager.Instance != null && RunManager.Instance.State == RunManager.RunState.InRun;

    public string GetPrompt(GameObject interactor) =>
        string.IsNullOrEmpty(_destinationScene) ? "Extract" : $"Go to {_destinationScene}";

    public void Interact(GameObject interactor)
    {
        if (string.IsNullOrEmpty(_destinationScene))
            RunManager.Instance?.TriggerExtract();
        else
            SceneTransitionManager.Instance?.LoadSector(_destinationScene);
    }
}
