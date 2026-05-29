using UnityEngine;

/// <summary>
/// Polls keyboard input and delegates to InventoryUI.
/// Separated from InventoryUI so input bindings can be changed or
/// swapped for a new input system without touching grid/view logic.
/// </summary>
[RequireComponent(typeof(InventoryUI))]
public sealed class InventoryInputHandler : MonoBehaviour
{
    [Header("=== Controls ===")]
    public KeyCode openKey   = KeyCode.Tab;
    public KeyCode rotateKey = KeyCode.R;

    private InventoryUI _ui;

    private void Awake() => _ui = GetComponent<InventoryUI>();

    private void Update()
    {
        if (Input.GetKeyDown(openKey))
            _ui.SetOpen(!_ui.IsOpen);

        if (Input.GetKeyDown(rotateKey))
            InventoryUI.BroadcastRotate();
    }
}
