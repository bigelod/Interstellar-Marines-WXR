using System;
using UnityEngine;
using UnityEngine.PostProcessing;

// Forces anti-aliasing, ambient occlusion and depth of field permanently off.
//
// The game's video menu has toggles for these, but they do not persist - they come back
// on every session. Rather than fight the menu, this re-asserts the state on the
// PostProcessing profile a few times a minute, so it survives the menu, a level load,
// and the player toggling them by accident.
//
// ===== WHY THIS IS SURGICAL AND NOT "DISABLE POST-PROCESSING" =====
// Docs/MetaAI/BUG-FIXES.md records EnforceNoPostFX() disabling PostProcessingBehaviour,
// Bloom and Blur on every camera - which killed the HUD, because HUDCamera's bloom and
// blur ARE the bend pipeline that projects the visor HUD onto the glass. So this touches
// exactly three models by name and never the behaviour, never Bloom, never Blur, and
// never a camera's component set. Even if HUDCamera shares this profile, its bloom is
// left alone.
//
// Why these three specifically:
//  - TAA is actively dangerous here, not merely slow. It accumulates across frames and
//    jitters the projection matrix (PostProcessingBehaviour.jitteredMatrixFunc), which
//    blends and sub-pixel-shifts the red sync pixel in the top-left corner. The protocol
//    requires that pixel to arrive as an exact byte value with no filtering or blending
//    - XRAPI_V0_4_DESC.md section 5 - so TAA corrupts the frame id the runtime uses to
//    match image to head pose. That is motion sickness no frame rate can fix.
//  - Ambient occlusion and depth of field are pure cost on a standalone GPU, and depth
//    of field is actively wrong in VR: the headset cannot know where the eye is focused,
//    so any blur it applies fights the viewer's own accommodation.
public static class VRPostFX
{
    static float lastEnforce = -999f;
    static bool loggedOnce = false;

    public static void Enforce()
    {
        float now;
        try { now = Time.realtimeSinceStartup; } catch { return; }
        if (now - lastEnforce < 2f) return;
        lastEnforce = now;

        try
        {
            PostProcessingBehaviour[] all = UnityEngine.Object.FindObjectsOfType<PostProcessingBehaviour>();
            if (all == null) return;

            for (int i = 0; i < all.Length; i++)
            {
                PostProcessingBehaviour b = all[i];
                if (b == null) continue;
                PostProcessingProfile p = b.profile;
                if (p == null) continue;

                bool changed = false;

                // antialiasing covers both FXAA and TAA - the technique dropdown is moot
                // once the model itself is off.
                if (p.antialiasing != null && p.antialiasing.enabled) { p.antialiasing.enabled = false; changed = true; }
                if (p.ambientOcclusion != null && p.ambientOcclusion.enabled) { p.ambientOcclusion.enabled = false; changed = true; }
                if (p.depthOfField != null && p.depthOfField.enabled) { p.depthOfField.enabled = false; changed = true; }

                // Deliberately NOT touched: bloom, chromaticAberration, colorGrading,
                // vignette, fog, motionBlur. Bloom in particular is load-bearing for the
                // HUD bend; the rest are the game's look and have working menu toggles.

                if (changed && !loggedOnce)
                {
                    loggedOnce = true;
                    VRManagerLogger.LogInfo("[IMVR] PostFX forced off (AA/TAA, ambient occlusion, depth of field) on profile '" + p.name + "' via camera '" + b.gameObject.name + "'");
                }
            }
        }
        catch (Exception e)
        {
            try { VRManagerLogger.LogWarn("[IMVR] PostFX enforce failed: " + e.Message); } catch { }
        }
    }
}
