using UnityEngine;

/// <summary>
/// Makes a sprite always face the player's view — the classic "billboard" used for icons, markers,
/// pickups, foliage cards, etc. Drop it on an empty GameObject, assign a sprite, done.
///
/// Scene setup:
///   1. Create an empty GameObject where you want the marker.
///   2. Add this component — a SpriteRenderer is added automatically (RequireComponent).
///   3. Assign your sprite to the "Sprite" slot here (or straight onto the SpriteRenderer).
///   4. (Optional) Leave Target empty to track the main camera, or drag a specific camera/transform.
///
/// Runs in LateUpdate so it re-aims after the camera has finished moving this frame.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Billboard : MonoBehaviour
{
    [Header("=== Sprite ===")]
    [Tooltip("The image to show. Pushed onto the SpriteRenderer; leave empty to keep whatever the " +
             "SpriteRenderer already has.")]
    [SerializeField] private Sprite _sprite;

    [Tooltip("The SpriteRenderer this billboard drives. Auto-filled when the component is added.")]
    [SerializeField] private SpriteRenderer _renderer;

    [Header("=== Facing ===")]
    [Tooltip("Who to face. Leave empty to track the main camera automatically.")]
    [SerializeField] private Transform _target;

    [Tooltip("Only spin around the vertical axis — keeps the sprite upright (good for ground markers / " +
             "trees). Off = also tilt to match the camera when it looks up or down.")]
    [SerializeField] private bool _lockYAxis = false;

    [Tooltip("Flip 180° if your sprite shows up mirrored / back-to-front.")]
    [SerializeField] private bool _flip = false;

    // Cached when _target is empty so we don't call the (slow) Camera.main lookup every frame.
    private Transform _cachedCameraTransform;

    private void Reset()
    {
        // Edit-time wiring so we honor the "use serialized refs, don't GetComponent in Awake" rule.
        _renderer = GetComponent<SpriteRenderer>();
    }

    private void OnValidate()
    {
        // Keep the inspector preview in sync while authoring.
        if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
        if (_sprite != null && _renderer != null) _renderer.sprite = _sprite;
    }

    private void Awake()
    {
        if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
        if (_sprite != null && _renderer != null) _renderer.sprite = _sprite;
    }

    private void LateUpdate()
    {
        var face = ResolveTarget();
        if (face == null) return;

        // A SpriteRenderer's visible side looks toward -Z, so aligning our forward with the camera's
        // forward leaves the sprite squarely facing the viewer (and avoids the mirror you get from a
        // plain LookAt). _flip handles authored-backwards art.
        Vector3 forward = face.forward;

        if (_lockYAxis)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return; // looking straight down — nothing sensible to do
        }

        if (_flip) forward = -forward;

        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    /// <summary>The transform to face: the explicit target, else the (cached) main camera.</summary>
    private Transform ResolveTarget()
    {
        if (_target != null) return _target;

        if (_cachedCameraTransform == null && Camera.main != null)
            _cachedCameraTransform = Camera.main.transform;
        return _cachedCameraTransform;
    }
}
