using UnityEngine;

/// <summary>
/// Typed entry point on a flashlight prefab.
/// Assign both fields in the Inspector while in prefab edit mode —
/// drag the child LightSource and HoldPos Transform from within the same prefab.
/// FlashlightSlot reads this to get the LightSource without any cross-prefab drag-in.
/// </summary>
public class FlashlightView : MonoBehaviour
{
    [Tooltip("Drag the LightSource component from the child Light object (same prefab).")]
    public LightSource lightSource;

    [Tooltip("Empty Transform positioned at the hand grip point — used as the left-arm IK target.")]
    public Transform gripTarget;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (lightSource == null)
            Debug.LogWarning("[FlashlightView] lightSource is not assigned.", this);
        if (gripTarget == null)
            Debug.LogWarning("[FlashlightView] gripTarget is not assigned.", this);
    }
#endif
}
