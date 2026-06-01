using UnityEngine;

/// <summary>
/// A climbable ladder (W3-06). Implements <see cref="IInteractable"/>: the player presses
/// E near either end to mount, then <see cref="PlayerMotor"/> drives the climb.
///
/// Setup:
///   • Place an empty child at the bottom and top of the ladder and wire them as
///     <see cref="_bottomPoint"/> / <see cref="_topPoint"/>. Their XZ defines the climb axis;
///     their Y defines the dismount limits.
///   • Orient this transform so local +Z (blue arrow) points away from the wall toward the
///     player — that is the default jump-off / top-exit direction. Override per-ladder with
///     the direction fields if the ledge is elsewhere.
/// </summary>
public sealed class Ladder : MonoBehaviour, IInteractable
{
    [Header("Climb Axis")]
    [Tooltip("Empty transform at the foot of the ladder. XZ = climb line, Y = bottom dismount.")]
    [SerializeField] private Transform _bottomPoint;
    [Tooltip("Empty transform at the head of the ladder. Y = top dismount.")]
    [SerializeField] private Transform _topPoint;

    [Header("Mounting")]
    [Tooltip("Player must be within this distance (m) of an end point to mount.")]
    [SerializeField] private float _mountRange = 1.5f;

    [Header("Dismount / Jump-off Directions")]
    [Tooltip("Horizontal push direction when the player jumps off the ladder. " +
             "Leave zero to use this transform's forward (+Z).")]
    [SerializeField] private Vector3 _jumpOffDirection = Vector3.zero;
    [Tooltip("Horizontal nudge applied when stepping off the top onto a ledge. " +
             "Leave zero to use this transform's forward (+Z).")]
    [SerializeField] private Vector3 _topExitDirection = Vector3.zero;

    // ── Public geometry (read by PlayerMotor) ───────────────────────────────

    public float   TopY    => _topPoint    != null ? _topPoint.position.y    : transform.position.y;
    public float   BottomY => _bottomPoint != null ? _bottomPoint.position.y : transform.position.y;

    /// <summary>World position whose XZ is the ladder's centre climb line.</summary>
    public Vector3 AxisXZ  => _bottomPoint != null ? _bottomPoint.position : transform.position;

    public Vector3 JumpOffDirection => Horizontal(_jumpOffDirection, transform.forward);
    public Vector3 TopExitDirection => Horizontal(_topExitDirection, transform.forward);

    // ── IInteractable ───────────────────────────────────────────────────────

    public bool CanInteract(GameObject interactor)
        => interactor != null && DistanceToNearestEnd(interactor.transform.position) <= _mountRange;

    public string GetPrompt(GameObject interactor)
    {
        if (interactor == null) return "Climb";
        // Nearer the bottom → climbing up; nearer the top → descending.
        Vector3 p = interactor.transform.position;
        return DistToTop(p) < DistToBottom(p) ? "Descend" : "Climb";
    }

    public void Interact(GameObject interactor)
    {
        if (interactor == null) return;
        var motor = interactor.GetComponentInParent<PlayerMotor>();
        if (motor != null) motor.EnterLadderMode(this);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns <paramref name="primary"/> flattened+normalized, or a flattened fallback if it is zero.</summary>
    private static Vector3 Horizontal(Vector3 primary, Vector3 fallback)
    {
        Vector3 v = new Vector3(primary.x, 0f, primary.z);
        if (v.sqrMagnitude < 0.0001f) v = new Vector3(fallback.x, 0f, fallback.z);
        return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
    }

    private float DistanceToNearestEnd(Vector3 p) => Mathf.Min(DistToBottom(p), DistToTop(p));
    private float DistToBottom(Vector3 p) => _bottomPoint != null ? Vector3.Distance(p, _bottomPoint.position) : float.MaxValue;
    private float DistToTop(Vector3 p)    => _topPoint    != null ? Vector3.Distance(p, _topPoint.position)    : float.MaxValue;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_bottomPoint == null || _topPoint == null)
            Debug.LogWarning($"[Ladder] {name}: assign both Bottom Point and Top Point.", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (_bottomPoint == null || _topPoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(_bottomPoint.position, _topPoint.position);
        Gizmos.DrawWireSphere(_bottomPoint.position, _mountRange);
        Gizmos.DrawWireSphere(_topPoint.position, _mountRange);
    }
#endif
}
