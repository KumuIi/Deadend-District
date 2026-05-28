using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to any 3D mesh in the main menu scene to make it a clickable button.
/// Requires a Collider on the same GameObject. MenuInputHandler drives clicks.
///
/// Implementors: any 3D object in the MainMenu scene acting as a menu button.
/// </summary>
public class MenuButton3D : MonoBehaviour
{
    [SerializeField] private UnityEvent _onClick;
    [SerializeField] private Transform _cameraTarget;
    [SerializeField] private bool _invokeAfterCameraArrives = false;

    [Header("Hover")]
    [SerializeField] private float _hoverScale = 1.08f;
    [SerializeField] private float _hoverDuration = 0.15f;

    private Vector3 _baseScale;
    private bool _isHovered;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    public void OnHoverEnter()
    {
        if (_isHovered) return;
        _isHovered = true;
        transform.DOScale(_baseScale * _hoverScale, _hoverDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnHoverExit()
    {
        if (!_isHovered) return;
        _isHovered = false;
        transform.DOScale(_baseScale, _hoverDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void Click()
    {
        transform.DOScale(_baseScale, _hoverDuration * 0.5f).SetEase(Ease.OutQuad).SetUpdate(true);

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
