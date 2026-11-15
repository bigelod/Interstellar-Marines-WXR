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
// The composite camera is parked far outside the level, has an identity transform, and
// draws exactly one quad on its own layer. The quad's only transform is the Z roll; it
// cannot inherit the world camera's yaw/pitch the way an immediate-mode GL pass did.
// It is created once and survives level changes.
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
    static GameObject quadGo;
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

    // True whenever the low-res world composite should be running. A fixed native size
    // overrides the scale, so world_render_scale=1.0 must not disable the head-roll split
    // when world_width/world_height are set.
    public static bool CompositeWanted
    {
        get
        {
            return (VRManager.worldNativeWidth > 0 && VRManager.worldNativeHeight > 0)
                || VRManager.worldRenderScale < 0.999f;
        }
    }

    // True only while the composite is actually replacing the world on screen. The
    // full-resolution gun camera hangs off this so it is never active in menus/pause.
    public static bool CompositeActive
    {
        get
        {
            return blitCam != null && blitCam.enabled
                && rt != null && worldCam != null
                && worldCam.targetTexture == rt;
        }
    }

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
        // Far outside the level: the quad is layer 31, and WorldCamera's culling mask
        // happens to include that layer. At the map origin the world camera could draw
        // the quad by accident; ten kilometres below the floor it cannot.
        go.transform.position = new Vector3(0f, -10000f, 0f);

        blitCam = go.AddComponent<Camera>();
        blitCam.depth = 0.5f;                          // after WorldCamera (0), before HUD (1)
        // The world camera used to be the only camera that cleared the screen (Skybox).
        // Now it renders into our RT, so nobody clears the backbuffer unless this camera
        // does. Without the clear, any later screen-space effect starts from the previous
        // frame and can accumulate. Clear here, then blit the fresh world over it.
        blitCam.clearFlags = CameraClearFlags.SolidColor;
        blitCam.backgroundColor = Color.black;
        blitCam.cullingMask = (1 << 31);               // only the composite quad
        blitCam.orthographic = true;
        blitCam.orthographicSize = 1f;
        blitCam.nearClipPlane = 0.01f;
        blitCam.farClipPlane = 1f;
        blitCam.useOcclusionCulling = false;
        blitCam.allowHDR = false;
        blitCam.allowMSAA = false;
        try { blitCam.renderingPath = RenderingPath.Forward; } catch { }
        blitCam.enabled = false;

        // A real quad mesh in front of an orthographic camera. This is deliberately not
        // an immediate-mode GL draw: OnPostRender runs right after WorldCamera and was
        // inheriting that camera's yaw/pitch, so the "2D" panel tilted on X/Y as well as
        // rolling. A mesh under an identity-view orthographic camera can only be moved by
        // its own transform, which this code keeps to a Z rotation.
        quadGo = new GameObject("IMVR_WorldQuad");
        quadGo.layer = 31;
        quadGo.transform.SetParent(go.transform, false);
        quadGo.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        quadGo.transform.localRotation = Quaternion.identity;
        quadGo.transform.localScale = new Vector3(
            2f * ((float)Screen.width / Mathf.Max(1, Screen.height)), 2f, 1f);
        quadGo.AddComponent<VRWorldBlit>();
        quadGo.SetActive(false);

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

        bool native = VRManager.worldNativeWidth > 0 && VRManager.worldNativeHeight > 0;
        float scale = Mathf.Clamp(VRManager.worldRenderScale, 0.25f, 1f);
        if (!native && scale >= 0.999f)
        {
            Disable(cam);
            return;
        }

        Ensure();
        int w = native
            ? Mathf.Clamp(VRManager.worldNativeWidth, 64, 4096)
            : Mathf.Max(64, Mathf.RoundToInt(Screen.width * scale));
        int h = native
            ? Mathf.Clamp(VRManager.worldNativeHeight, 48, 4096)
            : Mathf.Max(48, Mathf.RoundToInt(Screen.height * scale));
        if (rt == null || rtW != w || rtH != h) Recreate(w, h);
        if (rt == null) return;

        if (worldCam != null && worldCam != cam && worldCam.targetTexture == rt)
        {
            worldCam.targetTexture = null;
        }
        worldCam = cam;
        SuppressCopyToScreen(cam);
        if (cam.targetTexture != rt) cam.targetTexture = rt;
        SetCompositeActive(true);
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
            && CompositeWanted;
        SetCompositeActive(active);
    }

    static void SetCompositeActive(bool active)
    {
        if (blitCam != null && blitCam.enabled != active) blitCam.enabled = active;
        // The quad is a child of the camera, not the camera itself. Leaving it active
        // while the camera is off would let other cameras (WorldCamera includes layer 31)
        // find it, so it follows the same on/off state.
        if (quadGo != null && quadGo.activeSelf != active) quadGo.SetActive(active);
    }

    static void Disable(Camera cam)
    {
        if (cam != null && rt != null && cam.targetTexture == rt) cam.targetTexture = null;
        SetCompositeActive(false);
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
        SetCompositeActive(false);
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

// The blit itself. A real mesh under the orthographic composite camera, with only a Z
// rotation on its own transform. This is deliberately not an immediate-mode GL draw:
// OnPostRender runs immediately after WorldCamera and inherited that camera's yaw/pitch
// view matrix, so the "2D" panel tilted on X/Y as well as rolling. A mesh in a camera
// whose transform is identity (and which is parked far outside the level) has no such
// coupling: its only motion is the localRotation set below.
public class VRWorldBlit : MonoBehaviour
{
    Material mat;

    void Awake()
    {
        gameObject.layer = 31;

        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = BuildQuad();

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
            mr.sharedMaterial = mat;
            try { VRManagerLogger.LogInfo("[IMVR] World scaler blit shader: " + s.name); } catch { }
        }
        else
        {
            try { VRManagerLogger.LogWarn("[IMVR] World scaler: no blit shader found"); } catch { }
        }

        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void LateUpdate()
    {
        if (mat != null) mat.mainTexture = VRWorldScaler.WorldRT;

        // Only Z. The camera's transform is identity, so nothing else can tilt this quad.
        transform.localRotation = Quaternion.Euler(0f, 0f, VRWorldScaler.CompositeRollDeg);

        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float over = Mathf.Clamp(VRManager.quadOverscale, 1f, 1.5f);
        transform.localScale = new Vector3(2f * aspect * over, 2f * over, 1f);
    }

    static Mesh BuildQuad()
    {
        Mesh m = new Mesh();
        m.name = "IMVR_WorldQuad";
        m.hideFlags = HideFlags.HideAndDontSave;
        m.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };
        m.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        // Both windings: whichever side the material culls, the visible face still draws.
        m.triangles = new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }
}
