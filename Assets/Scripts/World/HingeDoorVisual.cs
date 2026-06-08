using DG.Tweening;
using UnityEngine;

/// <summary>
/// Swings a hinge transform open/closed with DOTween — the classic "rotate the door 90°
/// about its pivot" effect. Put this on (or point it at) the pivot/hinge object that the
/// door leaf is parented under; the leaf's authored local rotation is treated as CLOSED,
/// and OPEN = closed + <see cref="_openEuler"/>.
///
/// Setup: make an empty at the hinge edge, child the door mesh to it, add this component.
/// Leave <see cref="_hinge"/> empty to rotate this object's own transform.
/// </summary>
public class HingeDoorVisual : DoorVisual
{
    [Header("=== Hinge ===")]
    [Tooltip("Transform that rotates. Leave empty to use this GameObject's own transform.")]
    [SerializeField] private Transform _hinge;

    [Tooltip("Local-space rotation added to the CLOSED pose to reach OPEN. " +
             "(0,90,0) swings 90° about local Y like a normal door; negate Y to swing the other way.")]
    [SerializeField] private Vector3 _openEuler = new Vector3(0f, 90f, 0f);

    [Header("=== Motion ===")]
    [SerializeField] private float _duration = 0.6f;
    [SerializeField] private Ease  _ease     = Ease.OutCubic;

    private Quaternion _closedLocalRot;
    private Transform  Hinge => _hinge != null ? _hinge : transform;

    // Capture the authored pose as CLOSED before anything can move it. Awake runs before
    // Door.Start()/RefreshPose(), so the baseline is always the scene-authored rotation.
    private void Awake() => _closedLocalRot = Hinge.localRotation;

    public override void Apply(bool open, bool animate)
    {
        Quaternion target = open
            ? _closedLocalRot * Quaternion.Euler(_openEuler)
            : _closedLocalRot;

        var hinge = Hinge;
        hinge.DOKill(); // cancel any in-flight swing before retargeting (project convention)

        if (animate && Application.isPlaying)
            hinge.DOLocalRotateQuaternion(target, _duration).SetEase(_ease);
        else
            hinge.localRotation = target; // snap: restore, or edit-time
    }
}
