using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to any 3D model in the pause/main menu to make it a clickable button.
/// Hover: nudges the model along its own local X axis.
/// FlyIn: enters from +flyDir side, lands at base.
/// FlyOut: exits to -flyDir side (opposite of entry — bullets fly "through").
/// Requires a Collider on this GameObject or a child.
///
/// Implementors: one per 3D button model in PauseRoot or MainMenu scene.
/// </summary>
public class MenuButton3D : MonoBehaviour
{
    [SerializeField] private UnityEvent _onClick;
    [SerializeField] private Transform _cameraTarget;
    [SerializeField] private bool _invokeAfterCameraArrives = false;

    [Header("Hover (local X nudge)")]
    [SerializeField] private float _hoverOffset = 0.0125f;
    [SerializeField] private float _hoverDuration = 0.15f;

    [Header("Fly in (enters from +right side)")]
    [SerializeField] private float _flyInDistance = 0.3f;
    [SerializeField] private float _flyInDuration = 0.25f;
    [SerializeField] private Ease _flyInEase = Ease.OutBack;

    [Header("Fly out (exits to -right side, opposite of entry)")]
    [SerializeField] private float _flyOutDistance = 0.2f;
    [SerializeField] private float _flyOutDuration = 0.18f;
    [SerializeField] private Ease _flyOutEase = Ease.InBack;

    [Header("Shake (rejected click — e.g. saving outside the hub)")]
    [Tooltip("Sideways shake distance along the button's local X when a click is blocked.")]
    [SerializeField] private float _shakeStrength = 0.04f;
    [SerializeField] private float _shakeDuration = 0.4f;
    [SerializeField] private int   _shakeVibrato  = 20;

    public event Action<MenuButton3D> OnClicked;

    /// <summary>
    /// Optional gate evaluated when the button is clicked. If set and it returns false,
    /// the click is rejected: the button shakes in place instead of flying out / firing.
    /// PauseMenu uses this to block saving outside the hub.
    /// </summary>
    public Func<bool> ClickGuard { get; set; }

    private Vector3 _baseLocalPos;
    // Fly direction is the button's LOCAL +X (same axis the hover nudge uses). Animating in
    // local space keeps the button rigidly parented to the camera, so the rendered mesh and the
    // Physics.Raycast hitbox always project to the same screen point regardless of how the
    // camera moves or interpolates. World-space DOMove used to pin the button to a world point
    // captured at open, decoupling it from the camera — that produced a velocity-proportional
    // gap between the drawn mesh (render pose) and the ray (raw pose) when opening while moving.
    private static readonly Vector3 FlyAxis = Vector3.right;
    private bool _baseInitialized;
    private bool _isHovered;
    private bool _hasFledOut;

    private void OnEnable()
    {
        MenuHitRegistry<MenuButton3D>.Register(this);

        // Lazy-init: capture base local position the first time we're enabled,
        // before any offsets are applied. Avoids Awake execution-order issues
        // with PauseMenu.Awake deactivating the GO before Awake fires here.
        if (!_baseInitialized)
        {
            _baseLocalPos = transform.localPosition;
            _baseInitialized = true;
        }

        ResetToBase();
    }

    private void OnDisable() => MenuHitRegistry<MenuButton3D>.Unregister(this);

    // ── Called by PauseMenu.Open() ─────────────────────────────────────────

    /// <summary>
    /// Resets all state and flies in. Use instead of FlyIn() directly so rapid
    /// open/close cycles don't leave _hasFledOut=true blocking clicks.
    /// </summary>
    public void ResetAndFlyIn(float delay = 0f)
    {
        ResetToBase();
        FlyIn(delay);
    }

    public void FlyIn(float delay = 0f)
    {
        transform.DOKill();
        transform.DOLocalMove(_baseLocalPos, _flyInDuration)
                 .SetDelay(delay)
                 .SetEase(_flyInEase)
                 .SetUpdate(true);
    }

    // ── Called by PauseMenu cascade ────────────────────────────────────────

    public void FlyOut(float delay = 0f)
    {
        if (_hasFledOut) return;
        _hasFledOut = true;
        _isHovered = false;

        transform.DOKill();
        transform.DOLocalMove(_baseLocalPos - FlyAxis * _flyOutDistance, _flyOutDuration)
                 .SetDelay(delay)
                 .SetEase(_flyOutEase)
                 .SetUpdate(true);
    }

    // ── Hover ──────────────────────────────────────────────────────────────

    public void OnHoverEnter()
    {
        if (_isHovered || _hasFledOut) return;
        _isHovered = true;
        transform.DOLocalMoveX(_baseLocalPos.x + _hoverOffset, _hoverDuration)
                 .SetEase(Ease.OutQuad)
                 .SetUpdate(true);
    }

    public void OnHoverExit()
    {
        if (!_isHovered || _hasFledOut) return;
        _isHovered = false;
        transform.DOLocalMoveX(_baseLocalPos.x, _hoverDuration)
                 .SetEase(Ease.OutQuad)
                 .SetUpdate(true);
    }

    // ── Click ──────────────────────────────────────────────────────────────

    public void Click()
    {
        if (_hasFledOut) return;

        // Rejected click (e.g. saving outside the hub): shake in place, don't fly out or fire.
        if (ClickGuard != null && !ClickGuard())
        {
            Shake();
            return;
        }

        _hasFledOut = true;
        _isHovered = false;

        transform.DOKill();
        transform.DOLocalMove(_baseLocalPos - FlyAxis * _flyOutDistance, _flyOutDuration)
                 .SetEase(_flyOutEase)
                 .SetUpdate(true)
                 .OnComplete(FireAction);
    }

    /// <summary>
    /// Plays a sideways shake to signal a rejected click. Does not fire the action or fly
    /// the button out — the menu stays intact. Uses unscaled time so it works while paused.
    /// </summary>
    public void Shake()
    {
        transform.DOKill();
        transform.localPosition = _baseLocalPos;   // shake around the resting pose
        _isHovered = false;

        transform.DOShakePosition(_shakeDuration, transform.right * _shakeStrength,
                                  _shakeVibrato, randomness: 0f, snapping: false, fadeOut: true)
                 .SetUpdate(true);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ResetToBase()
    {
        transform.DOKill();

        // Start offset on the +X side (local) so the bullet enters from the right; FlyIn animates
        // it back to _baseLocalPos. Local space keeps the button rigid with the camera.
        transform.localPosition = _baseLocalPos + FlyAxis * _flyInDistance;

        _isHovered = false;
        _hasFledOut = false;
    }

    private void FireAction()
    {
        OnClicked?.Invoke(this);

        if (_cameraTarget != null && MenuCameraRig.Instance != null)
        {
            if (_invokeAfterCameraArrives)
                MenuCameraRig.Instance.MoveTo(_cameraTarget, () => _onClick.Invoke());
            else
            {
                MenuCameraRig.Instance.MoveTo(_cameraTarget, null);
                _onClick.Invoke();
            }
        }
        else
        {
            _onClick.Invoke();
        }
    }
}
