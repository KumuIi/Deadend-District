using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on the player camera. On hover/click it hit-tests the 3D menu buttons and dispatches
/// to MenuButton3D, SaveSlotButton3D, or FlashdriveButton depending on what's active.
///
/// Hit-testing does NOT use Physics.Raycast. The buttons are children of this camera sitting
/// only ~10cm from the lens, and they animate via DOTween. A static collider's pose in the
/// PhysX scene only refreshes on a physics step / Physics.SyncTransforms, so during fast camera
/// motion (and at timeScale 0) the PhysX hitbox lags the drawn mesh by a frame — hugely visible
/// at that distance. Instead we ray-test each collider (BoxCollider precisely, other shapes via
/// synced bounds) against its LIVE Transform, so the hitbox is always exactly where the mesh is
/// rendered this frame. Candidates come from MenuHitRegistry (no per-frame object scan).
/// Pipeline-independent (HDRP gives no OnPreCull).
///
/// Implementors: one instance on the player camera (used in both main menu and gameplay).
/// </summary>
[RequireComponent(typeof(Camera))]
public class MenuInputHandler : MonoBehaviour
{
    [SerializeField] private LayerMask               _clickMask = ~0;
    [SerializeField] private float                   _rayDistance = 100f;
    [SerializeField] private FlashdriveMenuController _flashdriveController;

    private Camera _cam;

    private MenuButton3D       _currentHoveredBtn;
    private SaveSlotButton3D   _currentHoveredSlot;
    private FlashdriveButton   _currentHoveredDrive;

    private void Awake() => _cam = GetComponent<Camera>();

    // Polls in LateUpdate, after DOTween (which animates the buttons) has written this frame's
    // transforms, so the manual hit-test below reads final, render-accurate positions.
    private void LateUpdate()
    {
        // Skip clear gameplay frames (cursor locked AND time running): the menu buttons are
        // inactive then, so there's nothing to hit and no reason to scan. Any menu — paused
        // gameplay or the main menu — unlocks the cursor and runs normally.
        if (Cursor.lockState == CursorLockMode.Locked && Time.timeScale != 0f) return;

        // Keep non-Box colliders' bounds current for the fallback path below (BoxColliders use a
        // sync-independent live test, so this is only insurance for other collider shapes).
        Physics.SyncTransforms();

        UpdateHover();
        if (Input.GetMouseButtonDown(0)) TryClick();
    }

    private void UpdateHover()
    {
        // Priority: MenuButton3D > FlashdriveButton > SaveSlotButton3D
        var btn   = Raycast<MenuButton3D>();
        var drive = btn == null ? Raycast<FlashdriveButton>() : null;
        var slot  = btn == null && drive == null ? Raycast<SaveSlotButton3D>() : null;

        if (btn != _currentHoveredBtn)
        {
            _currentHoveredBtn?.OnHoverExit();
            _currentHoveredBtn = btn;
            _currentHoveredBtn?.OnHoverEnter();
        }

        if (drive != _currentHoveredDrive)
        {
            if (_currentHoveredDrive != null)
            {
                _currentHoveredDrive.OnHoverExit();
                _flashdriveController?.OnDriveUnhovered();
            }
            _currentHoveredDrive = drive;
            if (_currentHoveredDrive != null)
            {
                _currentHoveredDrive.OnHoverEnter();
                _flashdriveController?.OnDriveHovered(_currentHoveredDrive, Input.mousePosition);
            }
        }
        else if (_currentHoveredDrive != null)
        {
            _flashdriveController?.UpdateTooltipPos(Input.mousePosition);
        }

        if (slot != _currentHoveredSlot)
        {
            _currentHoveredSlot?.OnHoverExit();
            _currentHoveredSlot = slot;
            _currentHoveredSlot?.OnHoverEnter();
        }
    }

    private void TryClick()
    {
        // Reuse the hover result computed by UpdateHover() this same frame — no second hit-test.
        if (_currentHoveredBtn   != null) { _currentHoveredBtn.Click();   return; }
        if (_currentHoveredDrive != null) { _currentHoveredDrive.Click(); return; }
        _currentHoveredSlot?.Click();
    }

    /// <summary>
    /// Returns the nearest active component of type T (from MenuHitRegistry) whose child collider
    /// the mouse ray enters, tested against LIVE transforms (no PhysX, so no collider-vs-render
    /// sync lag, and no per-frame object scan).
    /// </summary>
    private T Raycast<T>() where T : Component
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        T best = null;
        float bestDist = _rayDistance;

        IReadOnlyList<T> active = MenuHitRegistry<T>.Active;
        for (int i = 0; i < active.Count; i++)
        {
            T candidate = active[i];
            if (candidate == null) continue;   // defensive against a destroyed-but-not-unregistered entry

            foreach (var col in candidate.GetComponentsInChildren<Collider>())
            {
                if (((1 << col.gameObject.layer) & _clickMask) == 0) continue;
                if (RayHitsCollider(ray, col, out float dist) && dist <= bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }
        }
        return best;
    }

    private static bool RayHitsCollider(Ray ray, Collider col, out float dist)
    {
        // BoxCollider: precise oriented-box test against the LIVE transform — the bullets sit
        // ~10cm from the lens and animate, where a frame of PhysX sync lag is very visible.
        if (col is BoxCollider box) return RayHitsBox(ray, box, out dist);

        // Other shapes (save/flashdrive submenu, shown after the camera has settled): the synced
        // world bounds are accurate enough and lag-free at that point.
        return col.bounds.IntersectRay(ray, out dist);
    }

    /// <summary>Ray vs oriented BoxCollider using the collider's current world Transform.</summary>
    private static bool RayHitsBox(Ray ray, BoxCollider box, out float worldDist)
    {
        worldDist = 0f;
        Transform t = box.transform;

        // Ray into the collider's local space (InverseTransformVector keeps the local scale).
        Vector3 lo = t.InverseTransformPoint(ray.origin);
        Vector3 ld = t.InverseTransformVector(ray.direction);

        Vector3 e   = box.size * 0.5f;
        Vector3 min = box.center - e;
        Vector3 max = box.center + e;

        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(ld[a]) < 1e-9f)
            {
                if (lo[a] < min[a] || lo[a] > max[a]) return false;   // parallel & outside slab
            }
            else
            {
                float inv = 1f / ld[a];
                float t1 = (min[a] - lo[a]) * inv;
                float t2 = (max[a] - lo[a]) * inv;
                if (t1 > t2) { (t1, t2) = (t2, t1); }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
        }
        if (tmax < 0f) return false;                       // box entirely behind the ray
        float tHit = tmin >= 0f ? tmin : tmax;             // origin inside box → use far face

        Vector3 worldHit = t.TransformPoint(lo + ld * tHit);
        worldDist = Vector3.Distance(ray.origin, worldHit);
        return true;
    }
}
