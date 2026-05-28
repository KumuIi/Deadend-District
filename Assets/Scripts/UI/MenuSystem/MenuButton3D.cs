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

    public event Action<MenuButton3D> OnClicked;

    private Vector3 _baseLocalPos;
    private Vector3 _baseWorldPos;
    private Vector3 _flyDir;
    private bool _baseInitialized;
    private bool _isHovered;
    private bool _hasFledOut;

    private void OnEnable()
    {
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
        transform.DOMove(_baseWorldPos, _flyInDuration)
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
        transform.DOMove(_baseWorldPos - _flyDir * _flyOutDistance, _flyOutDuration)
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
        _hasFledOut = true;
        _isHovered = false;

        transform.DOKill();
        transform.DOMove(_baseWorldPos - _flyDir * _flyOutDistance, _flyOutDuration)
                 .SetEase(_flyOutEase)
                 .SetUpdate(true)
                 .OnComplete(FireAction);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ResetToBase()
    {
        transform.DOKill();
        transform.localPosition = _baseLocalPos;

        _baseWorldPos = transform.position;
        _flyDir = transform.right;

        // Start offset on +flyDir side so bullet enters from that side
        transform.position = _baseWorldPos + _flyDir * _flyInDistance;

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
