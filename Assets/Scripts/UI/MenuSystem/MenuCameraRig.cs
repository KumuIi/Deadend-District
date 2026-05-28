using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Singleton camera rig for the main menu scene.
/// MenuButton3D calls MoveTo() to shift the camera to a named 3D area.
///
/// Implementors: one instance in the MainMenu scene on the camera parent.
/// </summary>
public class MenuCameraRig : MonoBehaviour
{
    public static MenuCameraRig Instance { get; private set; }

    [SerializeField] private float _moveDuration = 0.8f;
    [SerializeField] private Ease _moveEase = Ease.InOutQuart;

    private Tween _activeTween;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Smoothly move and rotate the rig to match <paramref name="target"/>.
    /// <paramref name="onArrived"/> fires when the tween completes (may be null).
    /// </summary>
    public void MoveTo(Transform target, Action onArrived)
    {
        _activeTween?.Kill();

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(transform.DOMove(target.position, _moveDuration).SetEase(_moveEase));
        seq.Join(transform.DORotateQuaternion(target.rotation, _moveDuration).SetEase(_moveEase));
        if (onArrived != null)
            seq.OnComplete(() => onArrived());

        _activeTween = seq;
    }
}
