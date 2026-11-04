using UnityEngine;

// The red sync pixel: WinlatorXR reads the top-left corner of the captured frame to learn
// which frame id the game actually presented, and uses it to match poses to frames. If it
// stops being drawn, the runtime is reading a stale corner.
//
// It used to die on respawn. Two reasons, and both came from borrowing the game's
// cameras:
//
//   1. The component was added to whichever camera had the highest depth, and the quad was
//      parented to that camera's transform. On 2 Nov that camera was PostBloomEffect,
//      whose own parent is MainMenu(Clone) - so the whole thing was destroyed with the
//      menu, and a level change or respawn took it out.
//   2. `static bool alreadyCreated` was set on the first instance and never reset, so once
//      the original died nothing would ever build a replacement. The pixel was gone for
//      the rest of the session.
//
// So it owns everything now: its own DontDestroyOnLoad GameObject, created once at plugin
// startup, and its own Camera so it does not care what the game does with its camera
// stack. That camera clears NOTHING and renders NO layers - it exists purely to give us an
// OnPostRender callback after every game camera has drawn, at depth 1000. The pixel is
// then painted in screen space with immediate-mode GL: a solid rect at exact pixel
// coordinates, no texture sampling, no blending, no filtering - which is what the XrApi
// spec asks for, since any blend or filter corrupts the value the runtime reads back.
public class SyncPixelQuad : MonoBehaviour
{
    // XRAPI_V0_4_DESC asks for 10x10 at the top-left, and that is what this draws. It sat
    // at 150 for a long while on the theory that WinlatorXR letterboxes non-square render
    // targets, so a large patch would stay readable wherever the true corner landed. On
    // device on 3 Nov 2026 the runtime read the frame id perfectly well AND the player
    // could see a huge red square in the corner of their view, so the theory bought
    // nothing. Size comes from config (sync_pixel_size) purely so it can be raised again
    // without a rebuild if a future runtime does lose it.
    static int SizePx() { int n = VRManager.syncPixelSize; return n < 2 ? 2 : n; }

    static SyncPixelQuad instance;
    Material mat;
    int lastFrameId = -1;
    float red = 1f;

    // Called once from Plugin.Awake. Self-contained: no game object, camera or scene is
    // touched, so nothing the game destroys can take it down.
    public static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("IMVR_SyncPixel");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<SyncPixelQuad>();
    }

    void Awake()
    {
        instance = this;

        Camera cam = gameObject.AddComponent<Camera>();
        cam.depth = 1000f;                          // after every game camera
        cam.clearFlags = CameraClearFlags.Nothing;  // never wipe the presented frame
        cam.cullingMask = 0;                        // draws no scene objects at all
        cam.useOcclusionCulling = false;
        cam.orthographic = true;
        cam.orthographicSize = 1f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 1f;
        try { cam.renderingPath = RenderingPath.Forward; } catch { }

        // Hidden/Internal-Colored is the built-in immediate-mode shader and is always
        // present - no Shader.Find fallback chain needed, and no chance of the white
        // default material the old quad path could land on.
        Shader s = Shader.Find("Hidden/Internal-Colored");
        mat = new Material(s);
        mat.hideFlags = HideFlags.HideAndDontSave;
        try {
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero); // no blending
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        } catch { }

        try { VRManagerLogger.LogInfo("[IMVR] SyncPixel global camera created (depth 1000, clears nothing, " + SizePx() + "px, shader=" + (s != null ? s.name : "null") + ")"); } catch { }
    }

    void Update()
    {
        // Latest frame id from whatever the Rx thread last received. Read here rather than
        // in OnPostRender so the value is the one that was current for this frame.
        XRAPI.State state;
        if (XRAPI.GetBoolState(out state))
        {
            int frameId = state.frameId & 0xFF;
            red = frameId / 255f;
            if (frameId != lastFrameId)
            {
                lastFrameId = frameId;
                if (VRManagerState.verboseDebug)
                {
                    try { VRManagerLogger.LogInfo("[IMVR] SyncPixel frameId=" + frameId + " red=" + red); } catch { }
                }
            }
        }
        else
        {
            // No packets yet (MODE_VR=0 in menus). Keep it changing so the runtime can
            // still self-calibrate its colour mapping the moment it starts listening.
            red = (Time.frameCount % 256) / 255f;
        }
    }

    void OnPostRender()
    {
        // Peripheral damage bands go down FIRST - this camera is the only place in the
        // build that draws exactly once per frame, which is precisely what the game's own
        // once-per-camera IMGUI flash got wrong. See VRPainOverlay.cs.
        try { VRPainOverlay.Draw(); } catch { }

        if (mat == null) return;
        // Green must read 0 - the runtime uses it to confirm it is looking at our pixel
        // and not at scene content that happens to be red.
        mat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix();               // (0,0) bottom-left, so the top edge is Screen.height
        GL.Begin(GL.QUADS);
        GL.Color(new Color(red, 0f, 0f, 1f));
        float size = SizePx();
        float top = Screen.height;
        float bot = Screen.height - size;
        GL.Vertex3(0f, bot, 0f);
        GL.Vertex3(0f, top, 0f);
        GL.Vertex3(size, top, 0f);
        GL.Vertex3(size, bot, 0f);
        GL.End();
        GL.PopMatrix();
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
