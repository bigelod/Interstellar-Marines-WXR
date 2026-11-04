using UnityEngine;

// Taking a hit should be felt, not blinding. The game's own flash is both wrong for VR and
// wrong by accident, so in VR it is suppressed and replaced with two red bands in the
// player's peripheral vision - the approach that worked in Shogo-VR (commit ab012bc,
// "Hurt effect improvement").
//
// WHY THE ORIGINAL BLACKS OUT THE HEADSET
// MarineController.OnGameUI (Release/dnSpyResults/MarineController.cs:690) draws the flash
// as one full-screen GUI.Box tinted tookDamageOverlayColor, alpha 0.25:
//
//     color2.a = Mathf.Lerp(this.tookDamageOverlayColor.a, 0f, elapsed / duration);
//     GUI.color = color2;
//     GUI.Box(new Rect(0, 0, Screen.width, Screen.height), string.Empty, guistyle);
//
// IMGUI is not camera-bound: Unity runs the GUI pass once per CAMERA, and the 2 Nov
// hud_tree.txt lists seven enabled cameras. Seven coats of 0.25 alpha is 1 - 0.75^7, about
// 0.87 - very nearly opaque. On a monitor that reads as "the screen went red". Filling the
// whole field of view in a headset, it reads as the lights going out.
//
// So the game's sheet is clamped to alpha 0 rather than patching OnGameUI, which also
// draws the respawn prompt and the spawn countdown.
//
// WHAT REPLACES IT
// Two bands, one per side, full height, width scaling with hit intensity - peripheral
// vision is where a real flinch registers and it leaves the centre of the view clear.
// Differences from the Shogo version, which drove a side-by-side stereo frame through the
// engine's own FillRect:
//
//   - Mono, so two bands on one view rather than one per eye.
//   - Alpha-blended. Shogo's bands were opaque only because that 2D API had no alpha.
//   - Positioned against the VISIBLE area, not the framebuffer. WinlatorXR shows roughly
//     the central pain_bar_inset of the panel - the same reason the HUD had to be scaled
//     to 0.60 - so bands at the true screen edge would be outside the player's view
//     entirely. They sit at the edge of the visible region and grow inward.
//   - Drawn exactly ONCE per frame, from the mod's own overlay camera in
//     SyncPixelQuad.OnPostRender. That is the whole point: it is immune to the per-camera
//     multiplication that broke the original.
public static class VRPainOverlay
{
    static MarineController cached;
    static Material blendMat;
    static float intensity = 0f;

    // Fades with the game's own tookDamageOverlayDuration so the timing still matches the
    // hit sound and the rest of the feedback.
    public static float Intensity { get { return intensity; } }

    public static void Enforce()
    {
        intensity = 0f;
        if (!VRManagerState.inVR) return;
        try
        {
            if (cached == null)
            {
                MarineController[] all = Object.FindObjectsOfType<MarineController>();
                if (all == null) return;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].IsLocallyControlled) { cached = all[i]; break; }
                }
                if (cached == null) return;
                try {
                    VRManagerLogger.LogInfo("[IMVR] Pain overlay bound to '" + cached.gameObject.name
                        + "' - full-screen flash suppressed, peripheral bands active");
                } catch { }
            }

            // Kill the full-screen sheet at source. Zero alpha makes GUI.Box a no-op
            // whatever the camera count.
            Color c = cached.tookDamageOverlayColor;
            if (c.a != 0f) { c.a = 0f; cached.tookDamageOverlayColor = c; }

            float last = cached.LastTookDamageTime;
            float dur = cached.tookDamageOverlayDuration;
            if (last > -1f && dur > 0.001f)
            {
                float t = (Time.timeSinceLevelLoad - last) / dur;
                if (t >= 0f && t < 1f) intensity = 1f - t;
            }
        }
        catch { cached = null; }
    }

    // Called from SyncPixelQuad.OnPostRender, BEFORE the sync pixel is painted - at
    // pain_bar_inset = 1.0 the left band would reach x=0 and cover the corner the runtime
    // reads the frame id from, so the sync pixel has to go down last.
    public static void Draw()
    {
        if (intensity <= 0.001f) return;
        if (VRManager.painAlpha <= 0.001f) return;

        if (blendMat == null)
        {
            Shader s = Shader.Find("Hidden/Internal-Colored");
            if (s == null) return;
            blendMat = new Material(s);
            blendMat.hideFlags = HideFlags.HideAndDontSave;
            try {
                blendMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                blendMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                blendMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                blendMat.SetInt("_ZWrite", 0);
                blendMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            } catch { }
        }

        float w = Screen.width;
        float h = Screen.height;
        float inset = Mathf.Clamp(VRManager.painBarInset, 0.2f, 1f);
        float visW = w * inset;
        float visLeft = (w - visW) * 0.5f;
        float visRight = visLeft + visW;

        // Shogo scaled band width with the tint's brightest channel; here it scales with
        // the fade, so the band is widest the instant you are hit and shrinks as it clears.
        float band = visW * VRManager.painBarWidth * intensity;
        if (band < 4f) band = 4f;
        if (band > visW * 0.5f) band = visW * 0.5f;

        Color col = new Color(1f, 0f, 0f, VRManager.painAlpha * intensity);

        blendMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix();
        GL.Begin(GL.QUADS);
        GL.Color(col);
        // Left band: outer edge on the visible boundary, growing inward.
        Quad(visLeft, 0f, visLeft + band, h);
        // Right band: mirror.
        Quad(visRight - band, 0f, visRight, h);
        GL.End();
        GL.PopMatrix();
    }

    static void Quad(float x0, float y0, float x1, float y1)
    {
        GL.Vertex3(x0, y0, 0f);
        GL.Vertex3(x0, y1, 0f);
        GL.Vertex3(x1, y1, 0f);
        GL.Vertex3(x1, y0, 0f);
    }

    // The controller is new on every spawn, so drop the reference when the level changes.
    public static void Forget() { cached = null; }
}
