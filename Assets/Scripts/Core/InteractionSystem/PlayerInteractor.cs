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

    [Header("=== Debug ===")]
    [Tooltip("Logs an interaction diagnostic on every F-press (ray hit, layer, mask, blocked). " +
             "Off by default — fires two extra raycasts per press; enable only while diagnosing.")]
    [SerializeField] private bool _debugLogs = false;

    // ── Public state ───────────────────────────────────────────────────────

    /// <summary>The IInteractable currently in the player's crosshair, or null.</summary>
    public IInteractable Current { get; private set; }

    /// <summary>Prompt string for the currently focused interactable, or empty.</summary>
    public string CurrentPrompt { get; private set; } = string.Empty;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Update()
    {
        ScanForInteractable();

        if (GameInputState.InteractPressed)
        {
            if (_debugLogs) LogInteractDiagnostic();

            if (!GameInputState.GameplayBlocked)
                TryInteract();
        }
    }

    /// <summary>
    /// On F-press, reports why interaction did or didn't fire: gameplay-blocked state, the masked
    /// ray result, and an UNMASKED ray so a wrong-layer collider shows up as inMask=false.
    /// </summary>
    private void LogInteractDiagnostic()
    {
        if (!_camera) { Debug.LogWarning("[PlayerInteractor] F pressed but no _camera assigned."); return; }

        Debug.Log($"[PlayerInteractor] F pressed. GameplayBlocked={GameInputState.GameplayBlocked}, " +
                  $"Current={(Current == null ? "none" : Current.GetType().Name)}, Prompt='{CurrentPrompt}'");

        // The real interaction ray (mask = InteractI only).
        if (Physics.Raycast(_camera.position, _camera.forward, out RaycastHit masked, _range, _interactionMask))
            Debug.Log($"[PlayerInteractor]   masked ray HIT '{masked.collider.name}' " +
                      $"layer={LayerMask.LayerToName(masked.collider.gameObject.layer)} at {masked.distance:0.00}m");
        else
            Debug.Log($"[PlayerInteractor]   masked ray hit NOTHING within {_range}m (mask value={_interactionMask.value}).");

        // Unmasked ray: reveals a collider that's there but on the wrong layer.
        if (Physics.Raycast(_camera.position, _camera.forward, out RaycastHit any, _range))
        {
            int  layer  = any.collider.gameObject.layer;
            bool inMask = (_interactionMask.value & (1 << layer)) != 0;
            var  inter  = any.collider.GetComponentInParent<IInteractable>();
            Debug.Log($"[PlayerInteractor]   ANY ray hit '{any.collider.name}' " +
                      $"layer='{LayerMask.LayerToName(layer)}'({layer}) inMask={inMask} " +
                      $"IInteractable={(inter == null ? "NONE" : inter.GetType().Name)}");
        }
        else
        {
            Debug.Log("[PlayerInteractor]   ANY ray hit nothing — not aimed at a collider within range.");
        }
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
