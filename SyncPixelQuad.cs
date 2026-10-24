using UnityEngine;

public class SyncPixelQuad : MonoBehaviour
{
    GameObject quad;
    Material mat;
    Renderer quadRenderer;
    Camera cam;
    int lastFrameId = -1;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;
        Camera top = FindTopCamera();
        if (top != null) cam = top;

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SyncPixelQuad";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.parent = cam != null ? cam.transform : transform;
        quad.layer = 0;
        if (cam != null) cam.cullingMask |= (1 << 0);

        // White bug: Shader.Find("Unlit/Color") can be stripped from the build, so
        // new Material(null) = white default. Force a shader that is always present.
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Diffuse");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader);
        // Set both _Color and color so any shader picks it up
        mat.color = new Color(1f, 0f, 0f, 1f);
        try { mat.SetColor("_Color", new Color(1f, 0f, 0f, 1f)); } catch { }
        try { mat.SetColor("_MainColor", new Color(1f, 0f, 0f, 1f)); } catch { }
        mat.renderQueue = 4000;
        try { mat.SetInt("_ZTest", 8); mat.SetInt("_ZWrite", 0); } catch { }

        quadRenderer = quad.GetComponent<Renderer>();
        if (quadRenderer != null)
        {
            quadRenderer.material = mat;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
        }
        try { VRManagerLogger.LogInfo("[IMVR] SyncPixelQuad created shader=" + (shader != null ? shader.name : "null") + " cam=" + (cam != null ? cam.name : "null")); } catch { }
        PlaceQuad();
    }

    Camera FindTopCamera()
    {
        Camera top = null;
        float maxDepth = float.MinValue;
        Camera[] all = Camera.allCameras;
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].enabled && all[i].depth > maxDepth)
                {
                    maxDepth = all[i].depth;
                    top = all[i];
                }
            }
        }
        return top;
    }

    void PlaceQuad()
    {
        if (cam == null || quad == null) return;
        Camera top = FindTopCamera();
        if (top != null && top != cam)
        {
            cam = top;
            quad.transform.parent = cam.transform;
            quad.layer = 0;
            cam.cullingMask |= (1 << 0);
        }
        // 50x50 px at screen, touching absolute corner
        // Viewport (0,1) is exact top-left corner of screen
        float near = cam.nearClipPlane + 0.05f;
        // World size for 50px: viewport width at near * (50 / pixelWidth)
        float worldWidthAtNear = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * near * cam.aspect;
        float pixelWidth = cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width;
        float worldSize = worldWidthAtNear * (50f / pixelWidth);
        if (worldSize <= 0.001f) worldSize = 0.04f; // fallback ~50px

        Vector3 corner = cam.ViewportToWorldPoint(new Vector3(0f, 1f, near));
        quad.transform.position = corner;
        quad.transform.rotation = cam.transform.rotation;
        // Quad is centred on its position, so move it half-size inward so its
        // top-left edge touches the screen corner exactly, not inset by 0.06
        quad.transform.localPosition += new Vector3(worldSize * 0.5f, -worldSize * 0.5f, 0f);
        quad.transform.localScale = new Vector3(worldSize, worldSize, 1f);
    }

    void LateUpdate() { PlaceQuad(); }

    void Update()
    {
        if (quad == null) return;
        quad.SetActive(true);
        if (quadRenderer == null) quadRenderer = quad.GetComponent<Renderer>();
        XRAPI.State state;
        float red = 1f;
        if (XRAPI.GetBoolState(out state))
        {
            int frameId = state.frameId & 0xFF;
            red = frameId / 255f;
            if (frameId != lastFrameId)
            {
                lastFrameId = frameId;
                try { VRManagerLogger.LogInfo("[IMVR] SyncPixel frameId=" + frameId + " red=" + red); } catch { }
            }
        }
        else
        {
            red = (Time.frameCount % 256) / 255f;
        }
        // Set on BOTH mat and renderer.material - if one is stale, other still shows
        Color c = new Color(red, 0f, 0f, 1f);
        if (mat != null)
        {
            mat.color = c;
            try { mat.SetColor("_Color", c); } catch { }
        }
        if (quadRenderer != null && quadRenderer.material != null)
        {
            quadRenderer.material.color = c;
            try { quadRenderer.material.SetColor("_Color", c); } catch { }
        }
    }
}
