using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to any 3D model in the pause/main menu to make it a clickable button.
/// Hover: nudges the model on local X.
/// Click: flies it out on local X, then fires OnClicked so PauseMenu can cascade the rest.
/// FlyIn/FlyOut are called by PauseMenu to orchestrate the entrance/exit sequence.
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

    [Header("Fly in")]
    [SerializeField] private float _flyInOffsetX = -0.3f;
    [SerializeField] private float _flyInDuration = 0.25f;
    [SerializeField] private Ease _flyInEase = Ease.OutBack;

    [Header("Fly out (click or cascade)")]
    [SerializeField] private float _flyOutX = 0.2f;
    [SerializeField] private float _flyOutDuration = 0.18f;
    [SerializeField] private Ease _flyOutEase = Ease.InBack;

    // PauseMenu subscribes to this to trigger cascade on the other buttons
    public event Action<MenuButton3D> OnClicked;

    private Vector3 _baseLocalPos;
    private bool _isHovered;
    private bool _hasFledOut;

    private void Awake()
    {
        _baseLocalPos = transform.localPosition;
    }

    private void OnEnable()
    {
        // Snap to fly-in start so FlyIn() always starts from off-position
        transform.localPosition = new Vector3(
            _baseLocalPos.x + _flyInOffsetX,
            _baseLocalPos.y,
            _baseLocalPos.z);
        _isHovered = false;
        _hasFledOut = false;
    }

    // ── Called by PauseMenu to sequence the entrance ───────────────────────

    public void FlyIn(float delay = 0f)
    {
        transform.DOKill();
        transform.DOLocalMoveX(_baseLocalPos.x, _flyInDuration)
                 .SetDelay(delay)
                 .SetEase(_flyInEase)
                 .SetUpdate(true);
    }

    // ── Called by PauseMenu to cascade remaining buttons out ───────────────

    public void FlyOut(float delay = 0f)
    {
        if (_hasFledOut) return;
        _hasFledOut = true;
        _isHovered = false;

        transform.DOKill();
        transform.DOLocalMoveX(_flyOutX, _flyOutDuration)
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

    // ── Click (called by MenuInputHandler) ────────────────────────────────

    public void Click()
    {
        if (_hasFledOut) return;
        _hasFledOut = true;
        _isHovered = false;

        transform.DOKill();
        transform.DOLocalMoveX(_flyOutX, _flyOutDuration)
                 .SetEase(_flyOutEase)
                 .SetUpdate(true)
                 .OnComplete(FireAction);
    }

    private void FireAction()
    {
        // Notify PauseMenu first so it can start cascading the other buttons
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
