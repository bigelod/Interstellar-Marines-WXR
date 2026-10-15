using UnityEngine;

public class SyncPixelQuad : MonoBehaviour
{
    GameObject quad;
    Material mat;
    Camera cam;
    int lastFrameId = -1;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SyncPixelQuad";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.parent = cam != null ? cam.transform : transform;

        // Top-left corner: viewport (0,1) at near clip - 10x10 px equivalent
        float near = cam != null ? cam.nearClipPlane + 0.05f : 0.3f;
        if (cam != null)
        {
            // Place in top-left corner of camera view
            Vector3 topLeft = cam.ViewportToWorldPoint(new Vector3(0f, 1f, near));
            quad.transform.position = topLeft;
            quad.transform.rotation = cam.transform.rotation;
            // Inset slightly and small scale for 10x10 px
            quad.transform.localPosition += new Vector3(0.06f, -0.06f, 0f);
            quad.transform.localScale = new Vector3(0.025f, 0.025f, 1f);
        }
        else
        {
            quad.transform.localPosition = new Vector3(-0.5f, 0.5f, 0.5f);
            quad.transform.localScale = new Vector3(0.05f, 0.05f, 1f);
        }

        // Unlit color material - use sRGB red shade
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Diffuse");
        mat = new Material(shader);
        mat.color = Color.black;
        quad.GetComponent<Renderer>().material = mat;
        quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quad.GetComponent<Renderer>().receiveShadows = false;
        // Ensure it renders on top, no culling
        quad.layer = cam != null ? cam.gameObject.layer : 0;
    }

    void Update()
    {
        if (mat == null) return;
        XRAPI.State state;
        if (XRAPI.GetBoolState(out state))
        {
            // FIX: Map RGB Red 0-255 based on OpenXRFrameID per WinlatorXR spec
            // WinlatorXR expects HMD_SYNC as shade of red in sRGB top-left 10x10 px
            // HaloCEWXR uses D3DCOLOR_ARGB(255, OpenXRFrameID, 0, RenderEyeValue)
            int frameId = state.frameId & 0xFF; // 0-255
            if (frameId != lastFrameId)
            {
                lastFrameId = frameId;
                // Map 0-255 -> 0.0-1.0 red channel, sRGB
                float red = frameId / 255f;
                // Use sRGB red - no gamma correction needed for Unlit/Color if in Gamma space
                // If Linear space, we need to convert to linear or use gamma
                mat.color = new Color(red, 0f, 0f, 1f);
                quad.SetActive(true);
            }
        }
        else
        {
            // Fallback: cycle red based on Time.frameCount % 256
            //int frameId = Time.frameCount % 256;
            //float red = frameId / 255f;
            //mat.color = new Color(red, 0f, 0f, 1f);
        }
    }
}
