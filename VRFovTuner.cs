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
        VRConfigFile.Set("screen_fov", next.ToString("0.##", CultureInfo.InvariantCulture));
    }

    static bool IsPaused()
    {
        try { if (GameManager.IsPaused) return true; } catch { }
        try { if (Time.timeScale == 0f) return true; } catch { }
        return false;
    }
}
