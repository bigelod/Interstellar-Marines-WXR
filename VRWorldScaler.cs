using UnityEngine;

// Low-resolution world, full-resolution HUD.
//
// XOPS-WXR does this in D3D: the game keeps its native 640x480 game render target, that
// frame is copied to a texture and stretched across the real back buffer, and the text
// HUD is drawn afterwards at the window's real resolution. The expensive part (the 3D
// world) pays for 640x480; the text stays sharp.
//
// The same split works in Unity without touching the HUD pipeline:
//   * WorldCamera renders into a small RenderTexture (Screen * world_render_scale).
//   * A depth-0.5 camera blits that texture across the screen.
//   * HelmetCamera (glass/HUD, depth 1) and CAM Camera (uGUI, depth 1) still render at
//     the real window resolution, so the visor and its text are untouched.
//
// The composite camera owns nothing in the scene: it clears to black, draws no layers,
// and only blits the RT. It is created once and survives level changes.
//
// The game's own CopyToScreenRT is switched off while this is active; left on, it copies
// the deferred lighting result into RenderTexture.active, which is this RT, and feeds the
// frame back into itself until the screen is white. See the field comment below.
//
// world_render_scale = 1.0 disables the mechanism and restores direct-to-screen rendering.
public static class VRWorldScaler
{
    static RenderTexture rt;
    static Camera blitCam;
    static Camera worldCam;
    static int rtW;
    static int rtH;
    static bool loggedFirstUse;

    // The game's CopyToScreenRT image effect resolves deferred lighting into
    // RenderTexture.active and then blits to the camera destination. When the world camera
    // has a targetTexture, RenderTexture.active IS that texture, so its first Graphics.Blit
    // reads and writes the same RT every frame. That is the feedback loop that grew into a
    // white screen: the world (and its bloom) was being composited onto itself. It is not
    // needed while we own the destination - our blit camera does that copy - so it is
    // disabled for the duration and restored whenever the scaler is off.
    static Camera copyCam;
    static CopyToScreenRT copyToScreen;
    static bool copyToScreenWeDisabled;

    public static RenderTexture WorldRT { get { return rt; } }

    // Screen-space roll applied to the world composite, in degrees. VRHeadTracker strips
    // this from the camera and stores the head's roll; the world texture is rotated here
    // so the world and the gun (rendered into it) roll while the HUD, drawn afterwards,
    // stays fixed. headRollSign flips the direction on device.
    public static float CompositeRollDeg
    {
        get
        {
            if (!VRManager.headRoll) return 0f;
            return VRManagerState.headRollRad * Mathf.Rad2Deg * VRManager.headRollSign;
        }
    }

    public static void Ensure()
    {
        if (blitCam != null) return;

        GameObject go = new GameObject("IMVR_WorldBlit");
        Object.DontDestroyOnLoad(go);

        blitCam = go.AddComponent<Camera>();
        blitCam.depth = 0.5f;                          // after WorldCamera (0), before HUD (1)
        // The world camera used to be the only camera that cleared the screen (Skybox).
        // Now it renders into our RT, so nobody clears the backbuffer unless this camera
        // does. Without the clear, any later screen-space effect starts from the previous
        // frame and can accumulate. Clear here, then blit the fresh world over it.
        blitCam.clearFlags = CameraClearFlags.SolidColor;
        blitCam.backgroundColor = Color.black;
        blitCam.cullingMask = 0;                       // draws no scene objects
        blitCam.orthographic = true;
        blitCam.orthographicSize = 1f;
        blitCam.nearClipPlane = 0.01f;
        blitCam.farClipPlane = 1f;
        blitCam.useOcclusionCulling = false;
        blitCam.allowHDR = false;
        blitCam.allowMSAA = false;
        try { blitCam.renderingPath = RenderingPath.Forward; } catch { }
        blitCam.enabled = false;

        go.AddComponent<VRWorldBlit>();
        try { VRManagerLogger.LogInfo("[IMVR] World scaler ready (low-res world, full-res HUD)"); } catch { }
    }

    // Called from VRHeadTracker.LateUpdate, with the camera at its final pose.
    public static void Apply(Camera cam)
    {
        if (cam == null) return;

        // Only in actual VR gameplay. In menus/pause the game cameras own the screen and
        // a stale low-res world must not be composited over them.
        if (!VRManagerState.inVR || Marine.LocalMarine == null)
        {
            Disable(cam);
            return;
        }

        float scale = Mathf.Clamp(VRManager.worldRenderScale, 0.25f, 1f);
        if (scale >= 0.999f)
        {
            Disable(cam);
            return;
        }

        Ensure();
        int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * scale));
        int h = Mathf.Max(48, Mathf.RoundToInt(Screen.height * scale));
        if (rt == null || rtW != w || rtH != h) Recreate(w, h);
        if (rt == null) return;

        if (worldCam != null && worldCam != cam && worldCam.targetTexture == rt)
        {
            worldCam.targetTexture = null;
        }
        worldCam = cam;
        SuppressCopyToScreen(cam);
        if (cam.targetTexture != rt) cam.targetTexture = rt;
        if (blitCam != null && !blitCam.enabled) blitCam.enabled = true;
    }

    // Called from VRInputRunner.Update after the mode for this frame is known.
    public static void Tick()
    {
        Ensure();
        if (blitCam == null) return;

        bool active = VRManagerState.inVR
            && Marine.LocalMarine != null
            && rt != null
            && worldCam != null
            && worldCam.targetTexture == rt
            && VRManager.worldRenderScale < 0.999f;
        if (blitCam.enabled != active) blitCam.enabled = active;
    }

    static void Disable(Camera cam)
    {
        if (cam != null && rt != null && cam.targetTexture == rt) cam.targetTexture = null;
        if (blitCam != null && blitCam.enabled) blitCam.enabled = false;
        if (worldCam == cam) worldCam = null;
        RestoreCopyToScreen();
    }

    // Turn the game's deferred-lighting screen copy off only while the world camera is
    // writing into our RT. Restored in Disable(), including on pause and at 1.0, so the
    // normal flat pipeline is exactly as the game shipped it.
    static void SuppressCopyToScreen(Camera cam)
    {
        if (cam == null) return;

        if (copyCam != cam)
        {
            RestoreCopyToScreen();
            copyCam = cam;
            copyToScreen = cam.GetComponent<CopyToScreenRT>();
            if (copyToScreen != null && copyToScreen.enabled)
            {
                copyToScreen.enabled = false;
                copyToScreenWeDisabled = true;
                try {
                    VRManagerLogger.LogInfo("[IMVR] World scaler: CopyToScreenRT suppressed on '" + cam.name
                        + "' (path=" + cam.actualRenderingPath + "; its deferred copy would read and write the same RT)");
                } catch { }
            }
            return;
        }

        if (copyToScreen != null && copyToScreen.enabled)
        {
            copyToScreen.enabled = false;
            copyToScreenWeDisabled = true;
        }
    }

    static void RestoreCopyToScreen()
    {
        if (copyToScreen != null && copyToScreenWeDisabled && !copyToScreen.enabled)
        {
            try { copyToScreen.enabled = true; } catch { }
        }
        copyCam = null;
        copyToScreen = null;
        copyToScreenWeDisabled = false;
    }

    static void Recreate(int w, int h)
    {
        if (blitCam != null) blitCam.enabled = false;
        if (worldCam != null && worldCam.targetTexture == rt) worldCam.targetTexture = null;
        if (rt != null)
        {
            try { rt.Release(); } catch { }
            Object.Destroy(rt);
            rt = null;
        }

        rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default);
        rt.name = "IMVR_WorldRT";
        rt.filterMode = FilterMode.Bilinear; // smooth the upscale; the HUD is unaffected
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.antiAliasing = 1;                 // MSAA is already off for VR
        rt.hideFlags = HideFlags.DontSave;
        if (!rt.Create())
        {
            try { VRManagerLogger.LogWarn("[IMVR] World RT " + w + "x" + h + " Create() failed"); } catch { }
        }
        rtW = w;
        rtH = h;

        if (!loggedFirstUse)
        {
            loggedFirstUse = true;
            try { VRManagerLogger.LogInfo("[IMVR] World RT " + w + "x" + h + " in a " + Screen.width + "x" + Screen.height + " window"); } catch { }
        }
        else
        {
            try { VRManagerLogger.LogInfo("[IMVR] World RT resized to " + w + "x" + h); } catch { }
        }
    }
}

// The blit itself. The world is drawn as a textured quad so the composite can be
// rolled by the head: rotating the quad is the Unity equivalent of XOPS-WXR rotating
// the presented 640x480 game sprite in Present. The camera clears to black first, so
// the corners opened up by the rotation are black, not stale frame.
public class VRWorldBlit : MonoBehaviour
{
    Material mat;

    void Awake()
    {
        // Hidden/BlitCopy is the material Graphics.Blit itself uses: opaque, Cull Off,
        // ZTest Always, and guaranteed to be present in a player build. Unlit/Texture is
        // the fallback for a stripped build; Sprites/Default is the last resort.
        Shader s = Shader.Find("Hidden/BlitCopy");
        if (s == null) s = Shader.Find("Unlit/Texture");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s != null)
        {
            mat = new Material(s);
            mat.hideFlags = HideFlags.HideAndDontSave;
            try { VRManagerLogger.LogInfo("[IMVR] World scaler blit shader: " + s.name); } catch { }
        }
        else
        {
            try { VRManagerLogger.LogWarn("[IMVR] World scaler: no blit shader found"); } catch { }
        }
    }

    void OnPostRender()
    {
        RenderTexture src = VRWorldScaler.WorldRT;
        if (src == null) return;
        // Never read from the texture we are writing to. That is a feedback loop, not a copy.
        if (RenderTexture.active == src) return;

        float roll = VRWorldScaler.CompositeRollDeg;
        if (Mathf.Abs(roll) < 0.05f || mat == null)
        {
            Graphics.Blit(src, (RenderTexture)null);
            return;
        }

        // Rotate the world image about the screen centre with immediate-mode GL. The
        // camera itself has no roll; this is what puts it back on the world only,
        // leaving the HUD cameras upright. This is the same GL path SyncPixelQuad uses,
        // so it does not depend on any RenderTexture.active state left by the game.
        mat.mainTexture = src;
        mat.SetPass(0);
        GL.Clear(true, true, Color.black);

        float w = Screen.width;
        float h = Screen.height;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, w, 0f, h);
        GL.MultMatrix(Matrix4x4.TRS(
            new Vector3(w * 0.5f, h * 0.5f, 0f),
            Quaternion.Euler(0f, 0f, roll),
            Vector3.one));
        DrawQuad(w * 0.5f, h * 0.5f, false);
        // Back-face order too: Unlit/Texture culls, and we do not want the image to
        // disappear if the fallback shader's winding differs from BlitCopy's.
        DrawQuad(w * 0.5f, h * 0.5f, true);
        GL.PopMatrix();
    }

    static void DrawQuad(float hw, float hh, bool reverse)
    {
        GL.Begin(GL.QUADS);
        if (!reverse)
        {
            GL.TexCoord2(0f, 0f); GL.Vertex3(-hw, -hh, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3( hw, -hh, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3( hw,  hh, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(-hw,  hh, 0f);
        }
        else
        {
            GL.TexCoord2(0f, 1f); GL.Vertex3(-hw,  hh, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3( hw,  hh, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3( hw, -hh, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(-hw, -hh, 0f);
        }
        GL.End();
    }
}
