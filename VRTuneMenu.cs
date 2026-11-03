using System;
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
// Every change is written straight back to config.txt via VRConfigFile. This is
// required, not a nicety: VRManager.PollConfig re-reads the file every 2 seconds and
// would otherwise revert an in-memory change within two seconds of making it.
public class VRTuneMenu : MonoBehaviour
{
    static bool expanded = false;

    GUIStyle label, button, box, title, value;
    bool stylesReady = false;

    // The panel must land inside the part of the frame the headset actually shows. The
    // owner confirmed on 1 Nov that the green HUD sits exactly on the top and bottom
    // edges of the visible area, so the HUD's own scale is a measured proxy for it.
    const float SAFE = 0.62f;

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
        int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 34f));

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
        title.fontSize = Mathf.RoundToInt(fs * 1.15f);
        title.alignment = TextAnchor.MiddleCenter;

        stylesReady = true;
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
        if (!stylesReady) BuildStyles();

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

        float rowH = Screen.height * 0.055f;
        float panelW = safe.width * 0.62f;
        float panelH = rowH * 17f;
        Rect panel = new Rect(safe.x, safe.y, panelW, Mathf.Min(panelH, safe.height));

        GUI.Box(panel, GUIContent.none, box);

        float x = panel.x + panelW * 0.04f;
        float w = panelW * 0.92f;
        float y = panel.y + rowH * 0.25f;

        GUI.Label(new Rect(x, y, w, rowH), "IMVR tuning - changes save to config.txt", title);
        y += rowH * 1.1f;

        VRManager.eyeHeight = Row(new Rect(x, y, w, rowH), "Eye height", VRManager.eyeHeight, 0.02f, -0.20f, 0.80f, "eye_height", "0.00"); y += rowH;
        VRManager.eyeForward = Row(new Rect(x, y, w, rowH), "Eye forward", VRManager.eyeForward, 0.02f, -0.30f, 0.50f, "eye_forward", "0.00"); y += rowH;
        VRManager.handHeight = Row(new Rect(x, y, w, rowH), "Hand height", VRManager.handHeight, 0.02f, -0.30f, 0.50f, "hand_height", "0.00"); y += rowH;
        VRManager.nearClip = Row(new Rect(x, y, w, rowH), "Near clip", VRManager.nearClip, 0.01f, 0.01f, 0.40f, "near_clip", "0.00"); y += rowH;
        VRManager.farClip = Row(new Rect(x, y, w, rowH), "Far clip", VRManager.farClip, 10f, 10f, 400f, "far_clip", "0"); y += rowH;
        // View distance used only while the camera is inside geometry. Lower = cheaper,
        // and less of the level visible through the wall you are standing in.
        VRManager.insideFarClip = Row(new Rect(x, y, w, rowH), "Inside far clip", VRManager.insideFarClip, 1f, 1f, 60f, "inside_far_clip", "0"); y += rowH;

        // 1 = Unity's occlusion culling on (fast). 0 = off, which cures the pitch-black
        // screen when the camera is inside geometry, at a real cost in a corridor game.
        float oc = VRManager.occlusionCulling ? 1f : 0f;
        float oc2 = Row(new Rect(x, y, w, rowH), "Occl. cull (0=fix black)", oc, 1f, 0f, 1f, "occlusion_culling", "0"); y += rowH;
        if (oc2 != oc) VRManager.occlusionCulling = (oc2 >= 0.5f);
        // Takes effect on the next spawn - the occluder is only found during helmet setup.
        VRManager.occluderScale = Row(new Rect(x, y, w, rowH), "Occluder scale (respawn)", VRManager.occluderScale, 0.25f, 0.25f, 4.00f, "occluder_scale", "0.00"); y += rowH;
        VRManager.lightHeight = Row(new Rect(x, y, w, rowH), "Lamp height", VRManager.lightHeight, 0.02f, -0.30f, 0.60f, "light_height", "0.00"); y += rowH;
        VRManager.lightForward = Row(new Rect(x, y, w, rowH), "Lamp forward", VRManager.lightForward, 0.02f, -0.30f, 0.60f, "light_forward", "0.00"); y += rowH;

        float hs = VRGlassAnchor.GetHudScale();
        float hs2 = Row(new Rect(x, y, w, rowH), "HUD scale", hs, 0.02f, 0.20f, 1.00f, "hudScale", "0.00"); y += rowH;
        if (Mathf.Abs(hs2 - hs) > 0.0001f) VRGlassAnchor.SetHudScale(hs2);

        float fov = XRAPI.GetDeclaredFov();
        float fov2 = Row(new Rect(x, y, w, rowH), "Screen FOV", fov, 5f, 20f, 160f, "screen_fov", "0.#"); y += rowH;
        if (Mathf.Abs(fov2 - fov) > 0.001f) XRAPI.SetDeclaredFov(fov2);

        VRManager.turnSpeed = Row(new Rect(x, y, w, rowH), "Turn speed", VRManager.turnSpeed, 5f, 10f, 240f, "turn_speed", "0"); y += rowH;

        float dz = VRManager.deadzone;
        float dz2 = Row(new Rect(x, y, w, rowH), "Deadzone", dz, 0.02f, 0.00f, 0.60f, "deadzone", "0.00"); y += rowH;
        if (Mathf.Abs(dz2 - dz) > 0.0001f) { VRManager.deadzone = dz2; VRInput.SetConfig(dz2); }

        if (GUI.Button(new Rect(x, y + rowH * 0.15f, w * 0.3f, rowH * 0.85f), "Close", button))
        {
            expanded = false;
        }
    }

    // Returns the possibly-changed value, and persists it on change. Taking and
    // returning by value keeps the call sites readable where the backing store is a
    // property or lives in another class.
    float Row(Rect r, string text, float v, float step, float min, float max, string key, string fmt)
    {
        GUI.Label(new Rect(r.x, r.y, r.width * 0.42f, r.height), text, label);

        float bw = r.width * 0.13f;
        float bx = r.x + r.width * 0.42f;

        if (GUI.Button(new Rect(bx, r.y + r.height * 0.1f, bw, r.height * 0.8f), "-", button))
        {
            v = Mathf.Clamp(v - step, min, max);
            VRConfigFile.Set(key, v.ToString(fmt, CultureInfo.InvariantCulture));
        }

        GUI.Label(new Rect(bx + bw, r.y, r.width * 0.19f, r.height), v.ToString(fmt, CultureInfo.InvariantCulture), value);

        if (GUI.Button(new Rect(bx + bw + r.width * 0.19f, r.y + r.height * 0.1f, bw, r.height * 0.8f), "+", button))
        {
            v = Mathf.Clamp(v + step, min, max);
            VRConfigFile.Set(key, v.ToString(fmt, CultureInfo.InvariantCulture));
        }

        return v;
    }
}
