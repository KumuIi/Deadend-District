using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the single, shared inventory context menu and the top-most Screen Space – Overlay canvas
/// it lives on. Replaces the old per-panel menus: with a trader/stash open as a second InventoryUI
/// (its own canvas), a per-panel menu could be intercepted by the other canvas's GraphicRaycaster
/// or rendered behind the 3D item meshes. A single overlay canvas with the highest sortingOrder
/// always wins the raycast and renders above everything, so "Sell" / "Buy" clicks register.
///
/// Zero scene wiring: the canvas + EventSystem-independent menu are created on first use. A plain
/// (non-DontDestroyOnLoad) GameObject is used so the overlay is recreated per scene — the static
/// reference goes fake-null on scene unload and the lazy getter rebuilds it.
/// </summary>
public sealed class InventoryContextMenuService : MonoBehaviour
{
    private const int OverlaySortingOrder = 32760; // just under Canvas.sortingOrder max (32767)

    private static InventoryContextMenuService _instance;
    private InventoryContextMenu _menu;

    /// <summary>Lazily creates (or recreates after a scene change) the overlay + menu.</summary>
    public static InventoryContextMenuService Instance
    {
        get
        {
            // Unity's overloaded == treats a destroyed object as null, so a scene change
            // (which destroys the old GameObject) transparently triggers a rebuild here.
            if (_instance == null)
            {
                var go = new GameObject("InventoryContextMenuOverlay");
                _instance = go.AddComponent<InventoryContextMenuService>();
                _instance.Build(go);
            }
            return _instance;
        }
    }

    private void Build(GameObject go)
    {
        var canvas         = go.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;

        // Keep menu sizing resolution-independent and matched to the rest of the UI.
        var scaler         = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        go.AddComponent<GraphicRaycaster>();

        _menu = new InventoryContextMenu(canvas);
    }

    /// <summary>Shows the menu for the given request. Any previously open menu is replaced.</summary>
    public void Show(ContextMenuRequest request) => _menu.Show(request);

    /// <summary>Hides the menu if open.</summary>
    public void Hide() => _menu.Hide();

    /// <summary>True while the menu is visible.</summary>
    public bool IsOpen => _menu != null && _menu.IsOpen;

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
