using UnityEngine;

// Only the player's helmet lamp casts shadows. Everything else in the level is lit
// without them.
//
// Real-time shadows are the single most expensive thing a light can do, and this game
// scatters lights generously - corridor strips, emergency lamps, screens, muzzle flashes.
// A standalone headset running an x86 translation layer cannot pay for them, and the one
// that actually carries information is the player's own torch: it is the thing that
// tells you whether something is standing between you and the wall.
//
// ===== WHY A SWEEP AND NOT A PATCH =====
// Lights are spawned by the level, by prefabs, by weapon fire and by ToggleOcclusion
// turning rooms back on, from a dozen places with no common entry point. A periodic
// sweep catches all of them for one FindObjectsOfType every two seconds - the same
// pattern, and the same interval, as VRPostFX.
//
// It only ever writes Light.shadows. It never touches Light.enabled, which is
// ToggleOcclusion's business: that component switches whole rooms of lights off when
// they cull out, and a second writer fighting it is how the pitch-black screen gets
// reinvented. See VRClipGuard.cs for that story.
public static class VRLightTamer
{
    static float lastSweep = -999f;
    static bool loggedOnce = false;

    public static void Enforce()
    {
        if (!VRManagerState.inVR) return;

        float now;
        try { now = Time.realtimeSinceStartup; } catch { return; }
        if (now - lastSweep < 2f) return;
        lastSweep = now;

        try
        {
            Light[] all = Object.FindObjectsOfType<Light>();
            if (all == null) return;

            GameObject lamp = VRHeadLamp.LampRoot;
            int silenced = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Light l = all[i];
                if (l == null) continue;

                if (lamp != null && IsUnder(l.transform, lamp))
                {
                    // The helmet lamp is the one light that keeps its shadows. Hard
                    // rather than soft: soft shadows sample the map several times, and
                    // the extra cost buys a softness nobody notices from inside a torch
                    // beam. shadowResolution follows QualitySettings (VRVisualTweaks).
                    if (l.shadows == LightShadows.None) l.shadows = LightShadows.Hard;
                    continue;
                }

                if (l.shadows != LightShadows.None) { l.shadows = LightShadows.None; silenced++; }
            }

            if (silenced > 0 && !loggedOnce)
            {
                loggedOnce = true;
                try { VRManagerLogger.LogInfo("[IMVR] Shadow casting left to the helmet lamp only - " + silenced + " other lights silenced on first sweep"); } catch { }
            }
        }
        catch { }
    }

    static bool IsUnder(Transform t, GameObject root)
    {
        while (t != null)
        {
            if (t.gameObject == root) return true;
            t = t.parent;
        }
        return false;
    }
}
