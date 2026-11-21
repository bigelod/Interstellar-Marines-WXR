using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// In-game tuning menu, because editing config.txt on a headset is miserable.
// Docs/general_wxr/CLAUDE.md: "Expose anything a tester might change in an in-game
// menu, not an INI."
//
// ===== WHY THIS IS CLICKABLE AND NOT KEY-DRIVEN =====
// It only shows while the game is paused, and that is deliberate: while paused the mod
// sends MODE_VR = 0, which hands screen control back to the runtime's own pointer
// emulation. So the mouse works. The arrow-key tuner added earlier produced no log
// lines at all in the 1 Nov test, which suggests the thumbstick does not emit arrows on
// this setup - a pointer is the input channel that is known to work in this state.
//
// Drawn with IMGUI on purpose. The game's own HUD is uGUI bent through two materials,
// three cameras and two render textures; adding a tuning panel to that pipeline would
// risk the one subsystem that was hardest to get working. IMGUI draws straight to the
// backbuffer after every camera, so it cannot disturb the bend.
//
// ===== WHY TWO COLUMNS =====
// It used to be one column of nineteen rows at 5.5% of screen height each, which is
// 1.05 screens tall - so the bottom of it, Close button included, was off the panel and
// then off the visible area again. The list only grows, so the layout has to be one
// that scales: two columns at 4.0% fit twenty-six rows in about 62% of the height,
// inside the 62% the headset actually shows. Scrolling was the alternative and was rejected - dragging
// a scrollbar through the runtime's pointer emulation is far worse than clicking a
// button, and nothing here benefits from being hidden.
public class VRTuneMenu : MonoBehaviour
{
    static bool expanded = false;

    GUIStyle label, button, box, title, value;
    bool stylesReady = false;
    int lastStyleHeight = -1;

    // The panel must land inside the part of the frame the headset actually shows. The
    // owner confirmed on 1 Nov that the green HUD sits exactly on the top and bottom
    // edges of the visible area, so the HUD's own scale is a measured proxy for it.
    const float SAFE = 0.62f;
    const int ROWS_PER_COLUMN = 13;

    // Offered as whole presets rather than two independent width and height rows,
    // because most of the pairs those would let you build are not worth rendering.
    // Lower is a real win here: every pixel is rendered, captured, encoded and then
    // repainted across the panel, so it is paid for more than once.
    // Built at startup from the container's max mode: 0 = Auto/Max, 1 = Off, then
    // presets matching the detected 1:1 / 16:9 / 4:3 aspect, capped at the max.
    static int[] RES_W = new int[] { 0, -1 };
    static int[] RES_H = new int[] { 0, -1 };
    static bool resBuilt = false;

    bool ShouldShow()
    {
        // Never over a cutscene.
        try { if (GameUI.Instance != null && GameUI.Instance.MovieMode) return false; } catch { }

        // Any non-gameplay state: main menu, Settings, lobby, pause. inVR is false in all
        // of them, because that is exactly when the mod sends MODE_VR = 0 - which is also
        // when the runtime gives the pointer back, so a clickable panel works.
        if (!VRManagerState.inVR) return true;

        try { if (GameManager.IsPaused) return true; } catch { }
        try { if (Time.timeScale == 0f) return true; } catch { }
        return false;
    }

    void BuildStyles()
    {
        // Smaller than the old single-column font: a half-width column has roughly half
        // the room for a label, and a clipped label is worse than a small one.
        int fs = Mathf.Max(10, Mathf.RoundToInt(Screen.height / 44f));

        box = new GUIStyle(GUI.skin.box);

        label = new GUIStyle(GUI.skin.label);
        label.fontSize = fs;
        label.alignment = TextAnchor.MiddleLeft;
        label.normal.textColor = new Color(0.6f, 1f, 0.7f);

        value = new GUIStyle(label);
        value.alignment = TextAnchor.MiddleCenter;
        value.normal.textColor = Color.white;

        button = new GUIStyle(GUI.skin.button);
        button.fontSize = fs;

        title = new GUIStyle(label);
        title.fontSize = Mathf.RoundToInt(fs * 1.25f);
        title.alignment = TextAnchor.MiddleCenter;

        stylesReady = true;
        lastStyleHeight = Screen.height;
    }

    Rect SafeRect()
    {
        float w = Screen.width * SAFE;
        float h = Screen.height * SAFE;
        return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
    }

    void OnGUI()
    {
        if (!ShouldShow()) { return; }
        // The resolution row can change Screen.height underneath us, and the font sizes
        // are derived from it, so rebuild rather than keep a panel sized for the old one.
        if (!stylesReady || lastStyleHeight != Screen.height) BuildStyles();

        // Draw above any other IMGUI. Lower depth = composited later = on top.
        GUI.depth = -1000;

        Rect safe = SafeRect();

        if (!expanded)
        {
            // Small tab, so the game's own pause menu stays usable
            if (GUI.Button(new Rect(safe.x, safe.y, safe.width * 0.26f, Screen.height * 0.055f), "VR Settings", button))
            {
                expanded = true;
            }
            return;
        }

        // 13 rows fit by dropping to 4.0% per row; the panel stays inside the measured
        // 62% visible area with the Close button included.
        float rowH = Screen.height * 0.040f;
        // Full safe width now, not 62% of it - the columns need the room, and this is
        // the region that was measured as visible anyway.
        Rect panel = new Rect(safe.x, safe.y, safe.width, Mathf.Min(rowH * (ROWS_PER_COLUMN + 2.5f), safe.height));
        GUI.Box(panel, GUIContent.none, box);

        float pad = panel.width * 0.02f;
        float colW = (panel.width - pad * 3f) * 0.5f;
        float colAx = panel.x + pad;
        float colBx = colAx + colW + pad;
        float top = panel.y + rowH * 0.2f;

        GUI.Label(new Rect(panel.x, top, panel.width, rowH), "IMVR tuning - changes save to config.txt", title);
        float y0 = top + rowH * 1.2f;

        // ---- Column A: where you are and how you move ----
        float y = y0;
        VRManager.eyeHeight = Row(R(colAx, y, colW, rowH), "Eye height", VRManager.eyeHeight, 0.02f, -0.20f, 0.80f, "eye_height", "0.00"); y += rowH;
        VRManager.eyeForward = Row(R(colAx, y, colW, rowH), "Eye forward", VRManager.eyeForward, 0.02f, -0.30f, 0.50f, "eye_forward", "0.00"); y += rowH;
        VRManager.handHeight = Row(R(colAx, y, colW, rowH), "Hand height", VRManager.handHeight, 0.02f, -0.30f, 0.50f, "hand_height", "0.00"); y += rowH;
        VRManager.nearClip = Row(R(colAx, y, colW, rowH), "Near clip", VRManager.nearClip, 0.01f, 0.01f, 0.40f, "near_clip", "0.00"); y += rowH;
        // Fog follows this automatically - see VRVisualTweaks.ApplyFog.
        VRManager.farClip = Row(R(colAx, y, colW, rowH), "Far clip (fog)", VRManager.farClip, 5f, 10f, 400f, "far_clip", "0"); y += rowH;

        // Vertical FOV of the game camera itself. Separate from the two outbound values
        // below; applied in VRHeadTracker.OnPreRender.
        VRManager.cameraFov = Row(R(colAx, y, colW, rowH), "Camera FOV", VRManager.cameraFov, 1f, 40f, 140f, "camera_fov", "0.#"); y += rowH;

        // Constants we send on the 7278 packet. Not auto, not read from the game or the
        // runtime; the VR menu is the only thing that changes them.
        VRManager.fovH = Row(R(colAx, y, colW, rowH), "FOV H", VRManager.fovH, 1f, 10f, 180f, "fov_h", "0.#"); y += rowH;
        VRManager.fovV = Row(R(colAx, y, colW, rowH), "FOV V", VRManager.fovV, 1f, 10f, 180f, "fov_v", "0.#"); y += rowH;
        XRAPI.SetConfiguredFov(VRManager.fovH, VRManager.fovV);

        VRManager.turnSpeed = Row(R(colAx, y, colW, rowH), "Turn speed", VRManager.turnSpeed, 5f, 10f, 240f, "turn_speed", "0"); y += rowH;

        float dz = VRManager.deadzone;
        float dz2 = Row(R(colAx, y, colW, rowH), "Deadzone", dz, 0.02f, 0.00f, 0.60f, "deadzone", "0.00"); y += rowH;
        if (Mathf.Abs(dz2 - dz) > 0.0001f) { VRManager.deadzone = dz2; VRInput.SetConfig(dz2); }

        VRManager.lightHeight = Row(R(colAx, y, colW, rowH), "Lamp height", VRManager.lightHeight, 0.02f, -0.30f, 0.60f, "light_height", "0.00"); y += rowH;
        VRManager.lightForward = Row(R(colAx, y, colW, rowH), "Lamp forward", VRManager.lightForward, 0.02f, -0.30f, 0.60f, "light_forward", "0.00"); y += rowH;

        // XOPS-style head roll: -1 / +1 flips the world composite's rotation. It toggles
        // rather than slides because only the direction is ambiguous on a new headset.
        float roll = VRManager.headRollSign;
        float roll2 = Row(R(colAx, y, colW, rowH), "Roll sign", roll, 2f, -1f, 1f, "roll_sign", "0"); y += rowH;
        if (roll2 != roll) VRManager.headRollSign = (roll2 < 0f) ? -1f : 1f;

        // ---- Column B: what it looks like and what it costs ----
        y = y0;
        ResolutionRow(R(colBx, y, colW, rowH)); y += rowH;

        // World render scale: below 1.0 is the performance win, 1.0 is direct rendering,
        // above 1.0 supersamples the world for a stronger headset.
        VRManager.worldRenderScale = Row(R(colBx, y, colW, rowH), "World res", VRManager.worldRenderScale, 0.05f, 0.25f, 2.00f, "world_render_scale", "0.00"); y += rowH;

        // Draw the composite slightly larger than the window so a head roll does not
        // expose black corners. Crops the world edges by the same factor.
        VRManager.quadOverscale = Row(R(colBx, y, colW, rowH), "Overscan", VRManager.quadOverscale, 0.05f, 1.00f, 1.50f, "quad_overscale", "0.00"); y += rowH;

        float hs = VRGlassAnchor.GetHudScale();
        float hs2 = Row(R(colBx, y, colW, rowH), "HUD scale", hs, 0.02f, 0.20f, 1.00f, "hudScale", "0.00"); y += rowH;
        if (Mathf.Abs(hs2 - hs) > 0.0001f) VRGlassAnchor.SetHudScale(hs2);

        // Supersample factor for the HUD's own render target: same size on screen, more
        // pixels behind the glyphs. Two render targets and two blits scale with it, so
        // it is the first thing to wind back if frames are short. See HUDCamera.Update.
        VRManager.hudSupersample = Row(R(colBx, y, colW, rowH), "HUD sharpness", VRManager.hudSupersample, 0.25f, 0.50f, 4.00f, "hud_supersample", "0.00"); y += rowH;

        // Only the helmet lamp casts shadows at all (VRLightTamer), so these two govern
        // exactly one light - but it is on screen the whole time.
        VRManager.shadowDistance = Row(R(colBx, y, colW, rowH), "Shadow dist", VRManager.shadowDistance, 5f, 0f, 100f, "shadow_distance", "0"); y += rowH;
        float sres = VRManager.shadowResolution;
        float sres2 = Row(R(colBx, y, colW, rowH), "Shadow res", sres, 1f, 0f, 3f, "shadow_resolution", "0"); y += rowH;
        if (sres2 != sres) VRManager.shadowResolution = Mathf.RoundToInt(sres2);

        // 1 = Unity's occlusion culling on (fast, and the default again as of 3 Nov).
        // VRClipGuard still forces it off per frame while the camera is inside geometry,
        // which is the only time this game's culling can switch the lights out.
        float oc = VRManager.occlusionCulling ? 1f : 0f;
        float oc2 = Row(R(colBx, y, colW, rowH), "Occl. cull", oc, 1f, 0f, 1f, "occlusion_culling", "0"); y += rowH;
        if (oc2 != oc) VRManager.occlusionCulling = (oc2 >= 0.5f);

        // Takes effect on the next spawn - the occluder is only found during helmet setup.
        VRManager.occluderScale = Row(R(colBx, y, colW, rowH), "Occluder", VRManager.occluderScale, 0.25f, 0.25f, 4.00f, "occluder_scale", "0.00"); y += rowH;

        // Peripheral damage bands. 0 opacity disables them.
        VRManager.painAlpha = Row(R(colBx, y, colW, rowH), "Pain opacity", VRManager.painAlpha, 0.05f, 0.00f, 1.00f, "pain_alpha", "0.00"); y += rowH;
        VRManager.painBarWidth = Row(R(colBx, y, colW, rowH), "Pain width", VRManager.painBarWidth, 0.01f, 0.00f, 0.40f, "pain_bar_width", "0.00"); y += rowH;
        VRManager.painBarInset = Row(R(colBx, y, colW, rowH), "Pain inset", VRManager.painBarInset, 0.02f, 0.20f, 1.00f, "pain_bar_inset", "0.00"); y += rowH;

        float closeY = y0 + rowH * ROWS_PER_COLUMN + rowH * 0.15f;
        if (GUI.Button(new Rect(colAx, closeY, colW * 0.4f, rowH * 0.85f), "Close", button))
        {
            expanded = false;
        }
    }

    static Rect R(float x, float y, float w, float h) { return new Rect(x, y, w, h); }

    // Resolution presets come from the container's own max mode and aspect. 1:1, 16:9
    // and 4:3 are the three panels this project targets; anything else falls back to the
    // 4:3 list but is still capped at the detected max.
    void BuildResolutionPresets()
    {
        if (resBuilt) return;
        resBuilt = true;

        if (VRManager.containerMaxWidth <= 0 || VRManager.containerMaxHeight <= 0)
            VRManager.DetectContainerResolution();

        int maxW = Mathf.Max(320, VRManager.containerMaxWidth);
        int maxH = Mathf.Max(240, VRManager.containerMaxHeight);
        int aspect = VRManager.containerAspect;

        int[] widths;
        if (aspect == 1)       widths = new int[] { 512, 640, 768, 800, 1024, 1280, 1440, 1600, 1920 };
        else if (aspect == 16) widths = new int[] { 640, 854, 960, 1280, 1366, 1600, 1920, 2560 };
        else                   widths = new int[] { 640, 800, 960, 1024, 1152, 1280, 1400, 1600, 1920 };

        List<int> ws = new List<int>();
        List<int> hs = new List<int>();
        for (int i = 0; i < widths.Length; i++)
        {
            int w = widths[i];
            int h = (aspect == 1) ? w
                  : (aspect == 16) ? Mathf.RoundToInt(w * 9f / 16f)
                  : (w * 3 / 4);
            if (w > maxW || h > maxH) continue;
            ws.Add(w);
            hs.Add(h);
        }

        bool hasMax = false;
        for (int i = 0; i < ws.Count; i++)
            if (ws[i] == maxW && hs[i] == maxH) { hasMax = true; break; }
        if (!hasMax) { ws.Add(maxW); hs.Add(maxH); }

        // Presets ascending by area; Off at 0 and Auto at 1 stay first.
        RES_W = new int[2 + ws.Count];
        RES_H = new int[2 + hs.Count];
        RES_W[0] = -1; RES_H[0] = -1;     // Off / leave the window alone
        RES_W[1] = 0; RES_H[1] = 0;       // Auto / container max
        for (int i = 0; i < ws.Count; i++) { RES_W[i + 2] = ws[i]; RES_H[i + 2] = hs[i]; }

        for (int i = 2; i < RES_W.Length; i++)
        {
            for (int j = i + 1; j < RES_W.Length; j++)
            {
                long a = (long)RES_W[i] * RES_H[i];
                long b = (long)RES_W[j] * RES_H[j];
                if (b < a)
                {
                    int tw = RES_W[i]; RES_W[i] = RES_W[j]; RES_W[j] = tw;
                    int th = RES_H[i]; RES_H[i] = RES_H[j]; RES_H[j] = th;
                }
            }
        }
    }

    // Resolution is a preset list rather than two free numbers, and it applies straight
    // away: Screen.SetResolution reallocates the backbuffer and, through HUDCamera.Update,
    // the HUD's two render targets, so it must not be left to the 2s config poll to pick
    // up - that would reallocate them again on a timer.
    void ResolutionRow(Rect r)
    {
        BuildResolutionPresets();
        int idx = CurrentResIndex();
        int before = idx;

        GUI.Label(new Rect(r.x, r.y, r.width * 0.46f, r.height), "Resolution", label);
        float bw = r.width * 0.12f;
        float bx = r.x + r.width * 0.46f;

        if (GUI.Button(new Rect(bx, r.y + r.height * 0.1f, bw, r.height * 0.8f), "-", button)) idx--;
        int w = RES_W[Mathf.Clamp(idx, 0, RES_W.Length - 1)];
        int h = RES_H[Mathf.Clamp(idx, 0, RES_H.Length - 1)];
        string text = (w < 0) ? "Off" : (w == 0 ? "Auto" : w + "x" + h);
        GUI.Label(new Rect(bx + bw, r.y, r.width * 0.30f, r.height), text, value);
        if (GUI.Button(new Rect(bx + bw + r.width * 0.30f, r.y + r.height * 0.1f, bw, r.height * 0.8f), "+", button)) idx++;

        idx = Mathf.Clamp(idx, 0, RES_W.Length - 1);
        if (idx == before) return;

        VRManager.renderWidth = RES_W[idx];
        VRManager.renderHeight = RES_H[idx];
        VRConfigFile.Set("render_width", VRManager.renderWidth.ToString(CultureInfo.InvariantCulture));
        VRConfigFile.Set("render_height", VRManager.renderHeight.ToString(CultureInfo.InvariantCulture));
        VRManager.ApplyRenderResolution();
    }

    // Nearest preset by pixel count when the configured pair is not in the list, so a
    // hand-edited config.txt still lands somewhere sensible instead of snapping to 0.
    int CurrentResIndex()
    {
        if (VRManager.renderWidth < 0 || VRManager.renderHeight < 0) return 0; // Off
        if (VRManager.renderWidth == 0 && VRManager.renderHeight == 0) return 1; // Auto

        for (int i = 2; i < RES_W.Length; i++)
        {
            if (RES_W[i] == VRManager.renderWidth && RES_H[i] == VRManager.renderHeight) return i;
        }

        long want = (long)VRManager.renderWidth * VRManager.renderHeight;
        int best = 1;
        long bestDiff = long.MaxValue;
        for (int i = 2; i < RES_W.Length; i++)
        {
            long diff = Math.Abs((long)RES_W[i] * RES_H[i] - want);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        return best;
    }

    // Returns the possibly-changed value, and persists it on change. Taking and
    // returning by value keeps the call sites readable where the backing store is a
    // property or lives in another class.
    float Row(Rect r, string text, float v, float step, float min, float max, string key, string fmt)
    {
        GUI.Label(new Rect(r.x, r.y, r.width * 0.46f, r.height), text, label);

        float bw = r.width * 0.12f;
        float bx = r.x + r.width * 0.46f;

        if (GUI.Button(new Rect(bx, r.y + r.height * 0.1f, bw, r.height * 0.8f), "-", button))
        {
            v = Mathf.Clamp(v - step, min, max);
            VRConfigFile.Set(key, v.ToString(fmt, CultureInfo.InvariantCulture));
        }

        GUI.Label(new Rect(bx + bw, r.y, r.width * 0.30f, r.height), v.ToString(fmt, CultureInfo.InvariantCulture), value);

        if (GUI.Button(new Rect(bx + bw + r.width * 0.30f, r.y + r.height * 0.1f, bw, r.height * 0.8f), "+", button))
        {
            v = Mathf.Clamp(v + step, min, max);
            VRConfigFile.Set(key, v.ToString(fmt, CultureInfo.InvariantCulture));
        }

        return v;
    }
}
