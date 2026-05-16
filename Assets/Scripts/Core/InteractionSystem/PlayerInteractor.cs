using UnityEngine;

/// <summary>
/// Sits on the Player root. Each frame it raycasts from the camera for IInteractable objects
/// and drives the crosshair prompt. On E-press it calls Interact().
///
/// Uses GameInputState (centralized input) so it integrates with the existing input stack
/// and respects GameplayBlocked.
///
/// Future: AI interactors can call IInteractable.Interact() directly without this component.
/// </summary>
[DefaultExecutionOrder(100)]
public class PlayerInteractor : MonoBehaviour
{
    [Header("=== References ===")]
    [SerializeField] private Transform _camera;

    [Header("=== Settings ===")]
    [SerializeField] private float     _range = 2.5f;
    [SerializeField] private LayerMask _interactionMask;

    // ── Public state ───────────────────────────────────────────────────────

    /// <summary>The IInteractable currently in the player's crosshair, or null.</summary>
    public IInteractable Current { get; private set; }

    /// <summary>Prompt string for the currently focused interactable, or empty.</summary>
    public string CurrentPrompt { get; private set; } = string.Empty;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Update()
    {
        ScanForInteractable();

        if (GameInputState.InteractPressed && !GameInputState.GameplayBlocked)
            TryInteract();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void ScanForInteractable()
    {
        if (!_camera) { Current = null; CurrentPrompt = string.Empty; return; }

        if (Physics.Raycast(_camera.position, _camera.forward,
            out RaycastHit hit, _range, _interactionMask))
        {
            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.CanInteract(gameObject))
            {
                Current       = interactable;
                CurrentPrompt = interactable.GetPrompt(gameObject);
                return;
            }
        }

        Current       = null;
        CurrentPrompt = string.Empty;
    }

    private void TryInteract()
    {
        Current?.Interact(gameObject);
    }
}
