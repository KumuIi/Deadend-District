using UnityEngine;

/// <summary>
/// Drives a door's open/closed presentation through an Animator. Live transitions can play
/// the swing animation; restores snap straight to the final state (no replay) so a door
/// reloaded already-open shows open immediately.
/// </summary>
public class AnimatorDoorVisual : DoorVisual
{
    [SerializeField] private Animator _animator;

    [Tooltip("BOOL set to match open/closed. Holds the state across restores.")]
    [SerializeField] private string _openBoolParam = "Unlocked";

    [Tooltip("Optional TRIGGER fired for the live opening animation (the swing). " +
             "Skipped on snap so a restored door doesn't replay.")]
    [SerializeField] private string _openTrigger = "Open";

    [Tooltip("Optional state snapped to (normalized time 1) when opening without animation.")]
    [SerializeField] private string _openStateName = "DoorOpen";

    [Tooltip("Optional state snapped to (normalized time 0) when closing without animation.")]
    [SerializeField] private string _closedStateName = "DoorClosed";

    public override void Apply(bool open, bool animate)
    {
        if (_animator == null) return;

        if (!string.IsNullOrEmpty(_openBoolParam)) _animator.SetBool(_openBoolParam, open);

        if (animate)
        {
            if (open && !string.IsNullOrEmpty(_openTrigger)) _animator.SetTrigger(_openTrigger);
            return;
        }

        // Snap to the final pose without playing the transition.
        string state = open ? _openStateName : _closedStateName;
        if (!string.IsNullOrEmpty(state))
            _animator.Play(state, 0, open ? 1f : 0f);
    }
}
