using UnityEngine;

/// <summary>
/// Hub-side counterpart to ExtractionPoint: the door/portal that STARTS a run.
/// Place in the hub on the Interactable physics layer at each sector entrance.
///
/// Interacting calls RunManager.StartRun(sector), which both loads the sector scene and
/// flips the run state to InRun in one step — so the player doesn't need to "start a run"
/// separately before travelling. Only usable from the hub.
///
/// ExtractionPoint = leave a sector (extract / hop). RunEntryPoint = enter a sector from the hub.
///
/// Implementors: one per sector entrance in the hub scene.
/// </summary>
public class RunEntryPoint : MonoBehaviour, IInteractable
{
    [Tooltip("Scene name of the sector to enter (must be in Build Settings).")]
    [SerializeField] private string _sectorScene = "";

    public bool CanInteract(GameObject interactor) =>
        RunManager.Instance != null
        && RunManager.Instance.State == RunManager.RunState.InHub
        && !string.IsNullOrEmpty(_sectorScene);

    public string GetPrompt(GameObject interactor) => $"Enter {_sectorScene}";

    public void Interact(GameObject interactor) =>
        RunManager.Instance?.StartRun(_sectorScene);
}
