using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility: resizes each MenuButton3D's BoxCollider so it wraps that button's visible
/// mesh. The bullet models have their pivot at one end while the geometry extends ~13cm away,
/// so the authored collider (sitting at the pivot) doesn't cover where the bullet is drawn —
/// clicking the visible bullet misses. This bakes a fitted collider into the prefab/scene.
///
/// Usage: open the [Player] prefab (or a scene containing the buttons), then
/// Tools ▸ Deadend ▸ Fit Menu Button Colliders. Operates on the current selection if any
/// MenuButton3D are selected, otherwise on every MenuButton3D loaded.
/// </summary>
public static class MenuButtonColliderFitter
{
    // Extra slack added around the mesh so clicks near the edge still register (10% per side).
    private const float Padding = 1.10f;
    // Minimum local thickness on the thin axes so a near-flat bullet still has a grabbable box.
    private const float MinThickness = 0.02f;

    [MenuItem("Tools/Deadend/Fit Menu Button Colliders")]
    private static void FitColliders()
    {
        MenuButton3D[] buttons = GetTargets();
        if (buttons.Length == 0)
        {
            EditorUtility.DisplayDialog("Fit Menu Button Colliders",
                "No MenuButton3D found. Open the [Player] prefab or a scene with the pause menu, " +
                "then run this again.", "OK");
            return;
        }

        int fitted = 0, skipped = 0;
        foreach (var btn in buttons)
        {
            if (FitOne(btn)) fitted++;
            else skipped++;
        }

        Debug.Log($"[MenuButtonColliderFitter] Fitted {fitted} collider(s), skipped {skipped}. " +
                  "Save the prefab/scene to keep the change.");
    }

    private static MenuButton3D[] GetTargets()
    {
        // Prefer an explicit selection so the artist can fit just one button.
        var selected = Selection.GetFiltered<MenuButton3D>(SelectionMode.Editable | SelectionMode.Deep);
        if (selected.Length > 0) return selected;
        return Object.FindObjectsByType<MenuButton3D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private static bool FitOne(MenuButton3D btn)
    {
        var box = btn.GetComponentInChildren<BoxCollider>(true);
        var mf  = btn.GetComponentInChildren<MeshFilter>(true);
        if (box == null || mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning($"[MenuButtonColliderFitter] '{btn.name}' missing BoxCollider or MeshFilter — skipped.", btn);
            return false;
        }

        // Mesh bounds are in the MeshFilter's local space. Map its 8 corners into the BoxCollider's
        // local space (handles the two children having different rotations/scales) and take the AABB.
        Bounds mb = mf.sharedMesh.bounds;
        Transform mt = mf.transform, bt = box.transform;

        bool first = true;
        Vector3 min = Vector3.zero, max = Vector3.zero;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = mb.center + Vector3.Scale(mb.extents, Corner(i));
            Vector3 inBox  = bt.InverseTransformPoint(mt.TransformPoint(corner));
            if (first) { min = max = inBox; first = false; }
            else { min = Vector3.Min(min, inBox); max = Vector3.Max(max, inBox); }
        }

        Vector3 center = (min + max) * 0.5f;
        Vector3 size   = (max - min) * Padding;
        size = new Vector3(
            Mathf.Max(size.x, MinThickness),
            Mathf.Max(size.y, MinThickness),
            Mathf.Max(size.z, MinThickness));

        Undo.RecordObject(box, "Fit Menu Button Collider");
        box.center = center;
        box.size   = size;
        EditorUtility.SetDirty(box);
        return true;
    }

    private static Vector3 Corner(int i) => new Vector3(
        (i & 1) == 0 ? -1f : 1f,
        (i & 2) == 0 ? -1f : 1f,
        (i & 4) == 0 ? -1f : 1f);
}
