using UnityEngine;

/// <summary>
/// A one-way SHORTCUT lock — a <see cref="LockedDoor"/> whose "credential" is simply being on the
/// correct side of the door. No key required: approach from the openable side and unlock with bare
/// hands ("Open Shortcut"); from the wrong side it refuses and reads "You're on the wrong side".
///
/// Once opened it persists exactly like any other lock (the unlock flag is World-scoped in WSM),
/// so the shortcut stays open across runs. After unlocking, the paired <see cref="Door"/> leaf
/// swings as normal — and a door is openable from BOTH sides once unlocked, which is the whole
/// point of a shortcut: one-way to open, two-way thereafter.
///
/// The openable side is the half-space the door's local +forward points into (flip with
/// <see cref="_invertSide"/>). A scene gizmo draws a GREEN arrow on the openable side and a RED
/// stub on the blocked side so you can orient it in the editor without guessing.
/// </summary>
public class ShortcutLock : LockedDoor
{
    /// <summary>Which of the object's local axes points toward the openable side.</summary>
    private enum SideAxis { Forward, Back, Right, Left, Up, Down }

    [Header("=== Shortcut Side ===")]
    [Tooltip("Which of THIS object's local axes points toward the openable side. Pick the one that " +
             "points horizontally through the doorway — no need to rotate the object (which would " +
             "rotate its hitbox too). The green gizmo arrow shows your current choice; flip to the " +
             "opposite axis (e.g. Right ↔ Left) if it points the wrong way.")]
    [SerializeField] private SideAxis _openableAxis = SideAxis.Forward;

    [Tooltip("Length of the editor side-indicator arrow, in metres.")]
    [SerializeField] private float _gizmoLength = 1.5f;

    /// <summary>World-space direction that points toward the openable side, from the chosen local axis.</summary>
    private Vector3 OpenableNormal
    {
        get
        {
            switch (_openableAxis)
            {
                case SideAxis.Forward: return transform.forward;
                case SideAxis.Back:    return -transform.forward;
                case SideAxis.Right:   return transform.right;
                case SideAxis.Left:    return -transform.right;
                case SideAxis.Up:      return transform.up;
                case SideAxis.Down:    return -transform.up;
                default:               return transform.forward;
            }
        }
    }

    /// <summary>True when the interactor stands on the openable side of the door plane.</summary>
    private bool OnOpenableSide(GameObject interactor)
    {
        if (interactor == null) return false;
        Vector3 toInteractor = interactor.transform.position - transform.position;
        return Vector3.Dot(OpenableNormal, toInteractor) > 0f;
    }

    // The "credential" is your position: right side unlocks with bare hands, wrong side is denied.
    protected override UnlockAttempt BeginUnlock(GameObject interactor)
        => OnOpenableSide(interactor) ? UnlockAttempt.Succeeded : UnlockAttempt.Failed;

    // Re-evaluated every frame by PlayerInteractor, so the prompt flips as you walk around the door.
    protected override string GetLockedPrompt(GameObject interactor)
        => OnOpenableSide(interactor) ? "Open Shortcut" : "You're on the wrong side";

#if UNITY_EDITOR
    // Always-on gizmo (not just when selected) so every shortcut's openable side is visible at a glance.
    private void OnDrawGizmos()
    {
        Vector3 origin = transform.position;
        Vector3 open   = OpenableNormal.normalized;
        if (open.sqrMagnitude < 0.001f) return;

        // GREEN arrow → the side you CAN open from.
        Gizmos.color = Color.green;
        Vector3 tip = origin + open * _gizmoLength;
        Gizmos.DrawLine(origin, tip);
        Vector3 side = Vector3.Cross(Vector3.up, open).normalized * (_gizmoLength * 0.15f);
        Vector3 back = tip - open * (_gizmoLength * 0.25f);
        Gizmos.DrawLine(tip, back + side);
        Gizmos.DrawLine(tip, back - side);

        // RED stub → the blocked side ("wrong side").
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin - open * (_gizmoLength * 0.4f));
    }
#endif
}
