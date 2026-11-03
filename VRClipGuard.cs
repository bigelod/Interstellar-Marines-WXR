using UnityEngine;

// When the VR camera ends up inside world geometry - a lean into a wall, a tight
// corridor, a respawn in a bad spot - Unity's baked occlusion culling concludes the whole
// level is hidden and culls everything, giving a pitch-black screen.
//
// The blunt fix is to leave occlusion culling off permanently, but in a corridor game
// that means submitting the geometry behind every wall, all the time, for a problem that
// only occurs occasionally.
//
// So: detect the condition and pay the cost only while it lasts, and bound it by
// collapsing the view distance at the same time. Inside geometry the player sees a few
// metres of their surroundings instead of the whole level through the walls - which is
// both cheaper and reads better than an X-ray view of the map.
public static class VRClipGuard
{
    static int ignoreMask = 0;
    static bool maskReady = false;
    static bool lastInside = false;

    // Layers that must not count as "solid world". Resolved by NAME rather than index,
    // because layer numbering is a per-project decision and guessing it from a culling
    // mask is how you get a detector that silently never fires.
    static readonly string[] NOT_WORLD = new string[] {
        "Ignore Raycast", "Player", "Ragdoll", "PawnTrigger", "AILOSOcclusion",
        "TransparentFX", "Water", "UI", "UIOverlay", "Debris", "Reflection", "Skybox"
    };

    static void BuildMask()
    {
        ignoreMask = ~0;
        for (int i = 0; i < NOT_WORLD.Length; i++)
        {
            int l = LayerMask.NameToLayer(NOT_WORLD[i]);
            if (l >= 0) ignoreMask &= ~(1 << l);
        }
        maskReady = true;
    }

    public static void Apply(Camera cam)
    {
        if (cam == null) return;
        if (!maskReady) BuildMask();

        bool inside = false;
        try
        {
            Marine m = Marine.LocalMarine;
            if (m != null)
            {
                // From inside the body up to the eye. If a wall lies between the two, the
                // camera has passed through geometry - which is exactly the case that
                // triggers the cull. Starting at chest height keeps the origin reliably
                // inside the player and below the head, so a normal standing pose can
                // never register a hit.
                Vector3 chest = m.transform.position + Vector3.up * 1.0f;
                inside = Physics.Linecast(chest, cam.transform.position, ignoreMask);
            }
        }
        catch { }

        if (inside)
        {
            cam.useOcclusionCulling = false;          // or the world vanishes entirely
            cam.farClipPlane = VRManager.insideFarClip; // and bound what that costs
        }
        else
        {
            cam.useOcclusionCulling = VRManager.occlusionCulling;
            cam.farClipPlane = VRManager.farClip;
        }

        if (inside != lastInside)
        {
            lastInside = inside;
            if (VRManagerState.verboseDebug)
            {
                try { VRManagerLogger.LogInfo("[IMVR] Camera inside geometry = " + inside + " (far clip " + cam.farClipPlane + ", occlusion " + cam.useOcclusionCulling + ")"); } catch { }
            }
        }
    }
}
