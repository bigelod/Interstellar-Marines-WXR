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

        // Top-left corner: viewport (0,1) at near clip
        float near = cam != null ? cam.nearClipPlane + 0.05f : 0.3f;
        Vector3 topLeft = new Vector3(0f, 1f, near);
        if (cam != null)
        {
            topLeft = cam.ViewportToWorldPoint(topLeft);
            // Convert to local space of camera
            quad.transform.position = topLeft;
            quad.transform.rotation = cam.transform.rotation;
            quad.transform.localPosition += new Vector3(0.15f, -0.15f, 0f); // inset from edge
            quad.transform.localScale = new Vector3(0.03f, 0.03f, 1f);
        }
        else
        {
            quad.transform.localPosition = new Vector3(-0.5f, 0.5f, 0.5f);
            quad.transform.localScale = new Vector3(0.05f, 0.05f, 1f);
        }

        // Unlit color material
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Diffuse");
        mat = new Material(shader);
        mat.color = Color.red;
        quad.GetComponent<Renderer>().material = mat;
        quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quad.GetComponent<Renderer>().receiveShadows = false;

        // Keep on top
        quad.layer = cam != null ? cam.gameObject.layer : 0;
    }

    void Update()
    {
        if (mat == null) return;
        XRAPI.State state;
        if (XRAPI.GetBoolState(out state))
        {
            // Flash based on frameId from SixDOFinator (even=red, odd=black) - standard sync pixel
            if (state.frameId != lastFrameId)
            {
                lastFrameId = state.frameId;
                bool red = (state.frameId % 2) == 0;
                mat.color = red ? Color.red : Color.black;
                // Also toggle visibility every frame for HWXR style
                quad.SetActive(true);
            }
        }
        else
        {
            // Fallback: toggle based on Time.frameCount
            bool red = (Time.frameCount % 2) == 0;
            mat.color = red ? Color.red : Color.black;
        }
    }
}
