using UnityEngine;

/// <summary>
/// Shared UI positioning helpers used by inventory panels (tooltip, context menu, etc.).
/// </summary>
public static class CanvasUtils
{
    /// <summary>
    /// Moves <paramref name="rt"/> so its pivot sits exactly under <paramref name="screenPos"/>,
    /// correct for any Canvas render mode (Overlay, Screen Space Camera, World Space).
    /// </summary>
    public static void MoveToScreenPoint(RectTransform rt, Canvas canvas, Vector2 screenPos)
    {
        var canvasRT = (RectTransform)canvas.transform;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRT, screenPos, cam, out Vector2 local))
            return;

        Rect r        = canvasRT.rect;
        rt.anchorMin  = new Vector2(
            Mathf.InverseLerp(r.xMin, r.xMax, local.x),
            Mathf.InverseLerp(r.yMin, r.yMax, local.y));
        rt.anchorMax         = rt.anchorMin;
        rt.anchoredPosition  = Vector2.zero;
    }
}
