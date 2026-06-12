using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BaseMeshEffect that shears a uGUI graphic horizontally into a parallelogram.
/// Attach to any Image (not TMP — use FontStyles.Italic for text instead).
/// The bottom vertices shift left by half the skew, the top vertices shift right,
/// producing a Persona-style angular shape.
/// </summary>
[RequireComponent(typeof(Graphic))]
public sealed class SkewEffect : BaseMeshEffect
{
    [SerializeField] private float _skewPixels = 10f;

    /// <summary>Horizontal skew in pixels. Positive = top goes right.</summary>
    public float SkewPixels
    {
        get => _skewPixels;
        set
        {
            _skewPixels = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh == null || vh.currentVertCount == 0) return;

        Rect r = graphic.rectTransform.rect;
        float height = r.height;
        if (height <= 0f) return;

        float half    = _skewPixels * 0.5f;
        float yMin    = r.yMin;

        var vert = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vert, i);
            float t  = (vert.position.y - yMin) / height; // 0 at bottom, 1 at top
            vert.position.x += Mathf.Lerp(-half, half, t);
            vh.SetUIVertex(vert, i);
        }
    }
}
