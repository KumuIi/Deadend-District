using UnityEngine;

/// <summary>
/// Singleton that renders an ItemSO's model prefab into a Texture2D for inventory display.
///
/// Scene setup: add this component to any GameObject in the scene. Everything
/// (camera, light, staging root) is created automatically in Awake — no manual
/// child objects or layer configuration required.
///
/// Models are staged 10 000 units away from world origin, so the preview camera
/// never sees gameplay geometry even with cullingMask = Everything.
/// </summary>
public class ItemPreviewRenderer : MonoBehaviour
{
    public static ItemPreviewRenderer Instance { get; private set; }

    [Header("Framing")]
    public Color backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1f);
    [Tooltip("Extra space around the model as a fraction of its bounding size")]
    [Range(0f, 0.5f)]
    public float boundsPadding = 0.12f;

    [Header("Lighting")]
    public Vector3 lightEuler     = new Vector3(45f, -30f, 0f);
    public float   lightIntensity = 1.4f;

    private Camera    _cam;
    private Transform _root;

    // Far from any gameplay geometry — nothing else should be here
    private static readonly Vector3 StagingOrigin = new Vector3(10000f, 10000f, 10000f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Staging root — models are instantiated here during capture, then destroyed
        var rootGO = new GameObject("PreviewRoot");
        rootGO.transform.SetParent(transform);
        rootGO.transform.position = StagingOrigin;
        _root = rootGO.transform;

        // Perspective capture camera — perspective gives depth cues that orthographic flattens out
        var camGO = new GameObject("PreviewCamera");
        camGO.transform.SetParent(transform);
        _cam = camGO.AddComponent<Camera>();
        _cam.orthographic    = false;
        _cam.fieldOfView     = 40f; // narrow FOV = less distortion, more "telephoto" look
        _cam.clearFlags      = CameraClearFlags.SolidColor;
        _cam.backgroundColor = backgroundColor;
        _cam.cullingMask     = ~0; // all layers — safe because staging area is empty
        _cam.nearClipPlane   = 0.01f;
        _cam.farClipPlane    = 2000f;
        _cam.enabled         = false; // only render on explicit Render() calls

        // Directional light aimed at the staging area
        var lightGO = new GameObject("PreviewLight");
        lightGO.transform.SetParent(transform);
        lightGO.transform.rotation = Quaternion.Euler(lightEuler);
        var light = lightGO.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = lightIntensity;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Renders item.modelPrefab into a new Texture2D sized pixelWidth × pixelHeight.
    /// Falls back to a solid-colour swatch if no model is assigned.
    /// Caller owns the returned texture and must Destroy() it when done.
    /// </summary>
    public Texture2D Capture(ItemSO item, int pixelWidth, int pixelHeight)
    {
        if (item.modelPrefab == null)
            return MakeSolidTexture(pixelWidth, pixelHeight, item.itemColor);

        var rt = new RenderTexture(pixelWidth, pixelHeight, 24);
        _cam.targetTexture = rt;

        GameObject instance = Instantiate(item.modelPrefab, _root);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        FrameModel(instance, pixelWidth, pixelHeight);

        _cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, pixelWidth, pixelHeight), 0, 0);
        tex.Apply();
        RenderTexture.active   = null;
        _cam.targetTexture     = null;

        Destroy(instance);
        Destroy(rt);
        return tex;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    void FrameModel(GameObject instance, int pixW, int pixH)
    {
        Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
        foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(r.bounds);

        // Guard against empty renderers (no MeshRenderers in prefab)
        if (bounds.extents == Vector3.zero)
            bounds = new Bounds(instance.transform.position, Vector3.one * 0.5f);

        // Isometric-ish angle: above and slightly to the side
        Vector3 dir = new Vector3(-0.6f, 1.0f, -1.2f).normalized;

        // For perspective: distance = halfExtent / tan(halfFOV), with padding
        float halfExtent = bounds.extents.magnitude * (1f + boundsPadding);
        float fovRad     = _cam.fieldOfView * Mathf.Deg2Rad * 0.5f;
        float dist       = halfExtent / Mathf.Tan(fovRad);

        _cam.transform.position = bounds.center + dir * dist;
        _cam.transform.LookAt(bounds.center);
        _cam.nearClipPlane = dist * 0.05f;
        _cam.farClipPlane  = dist * 10f;
    }

    static Texture2D MakeSolidTexture(int w, int h, Color colour)
    {
        // Force alpha=1 — transparent fallback makes items invisible and unclickable in UGUI
        Color32 c  = new Color(colour.r, colour.g, colour.b, 1f);
        var tex    = new Texture2D(w, h);
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
