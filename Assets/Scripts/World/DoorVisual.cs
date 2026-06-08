using UnityEngine;

/// <summary>
/// The visual/kinematic half of a <see cref="Door"/> leaf. The <see cref="Door"/> owns open
/// STATE (WorldStateManager flag) and physics (blocking collider); a DoorVisual owns only HOW
/// the leaf presents open vs closed. Swap implementations (hinge swing, slide, Animator) without
/// touching door logic.
///
/// Contract:
///   Apply(open, animate=true)  → live transition (DOTween swing, animator trigger, …)
///   Apply(open, animate=false) → snap instantly to the final pose (used on save-load restore,
///                                so a door reloaded already-open shows open with no replay).
/// Implementations must make Apply idempotent and safe to call before the first frame.
/// </summary>
public abstract class DoorVisual : MonoBehaviour
{
    public abstract void Apply(bool open, bool animate);
}
