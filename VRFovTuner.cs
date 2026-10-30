using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// In-headset tuning for screen_fov - the angle WinlatorXR paints the captured frame
// across. The right value can only be found by looking through the lenses, and editing
// a text file on a standalone headset between attempts is painful. So: pause, tap
// up/down, watch the view resize, unpause. The chosen value is written straight back
// into config.txt, so it survives the session and there is nothing to copy down.
//
// ===== WHY ARROW KEYS AND NOT THE VR THUMBSTICK =====
// While the game is paused the mod sends MODE_VR = 0, and at 0 the runtime sends NO UDP
// packets at all - that is the documented meaning of 0, and it is deliberate here
// (see Docs/general_wxr/XRAPI_V0_4_DESC.md section 4). So there is no live thumbstick
// data to read in exactly the state we want to tune in: XRAPI is frozen on its last
// packet, and reading latest.lThumbUp would give a stale value forever.
//
// What DOES exist at MODE_VR = 0 is the runtime's own screen-mode input emulation,
// which is one of the reasons 0 is the right choice for menus: it maps the left
// thumbstick to the arrow keys. So reading the keyboard is not a workaround, it is the
// only channel that carries live stick input while paused.
//
// The raw-input block in Patches.cs already lets arrow keys through whenever the game
// is paused, so these reads are not suppressed by the mod's own patches.
public static class VRFovTuner
{
    const float STEP = 10f;
    const float MIN_FOV = 20f;
    const float MAX_FOV = 160f;

    public static void Update()
    {
        if (!IsPaused()) return;

        bool up = false, down = false;
        try
        {
            up = Input.GetKeyDown(KeyCode.UpArrow);
            down = Input.GetKeyDown(KeyCode.DownArrow);
        }
        catch { }
        if (up == down) return; // neither, or both in the same frame

        float cur = XRAPI.GetDeclaredFov();

        // Snap onto the 10-degree grid in the direction of travel, so the first tap from
        // an odd starting value like 104.5 lands on 110 or 100 rather than 114.5 / 94.5.
        float next;
        if (up) next = Mathf.Floor(cur / STEP) * STEP + STEP;
        else next = Mathf.Ceil(cur / STEP) * STEP - STEP;
        next = Mathf.Clamp(next, MIN_FOV, MAX_FOV);

        if (Mathf.Abs(next - cur) < 0.01f) return; // already at the clamp
        XRAPI.SetDeclaredFov(next);
        Persist(next);
    }

    static bool IsPaused()
    {
        try { if (GameManager.IsPaused) return true; } catch { }
        try { if (Time.timeScale == 0f) return true; } catch { }
        return false;
    }

    static void Persist(float value)
    {
        string text = value.ToString("0.##", CultureInfo.InvariantCulture);
        try { VRManagerLogger.LogInfo("[IMVR] screen_fov -> " + text + " (up/down while paused)"); } catch { }

        string path = VRManager.configPath;
        if (string.IsNullOrEmpty(path)) return; // in-memory change still applies this session

        try
        {
            string[] lines = File.ReadAllLines(path);
            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string k = t.Substring(0, eq).Trim().ToLower();
                if (k == "screen_fov" || k == "screenfov" || k == "vr_fov" || k == "fov")
                {
                    // Rewrite EVERY match, not just the first. ParseConfig applies each
                    // line in turn, so the last one wins - leaving a stale duplicate
                    // behind would let the 2s poll undo what was just set.
                    lines[i] = "screen_fov=" + text;
                    found = true;
                }
            }

            if (found)
            {
                File.WriteAllLines(path, lines);
            }
            else
            {
                // Key absent entirely - append rather than rewriting the file, so comments
                // and every other setting are left exactly as the user had them.
                File.AppendAllText(path, Environment.NewLine + "screen_fov=" + text + Environment.NewLine);
            }
        }
        catch (Exception e)
        {
            try { VRManagerLogger.LogWarn("[IMVR] Could not persist screen_fov: " + e.Message); } catch { }
        }
    }
}
