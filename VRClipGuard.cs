using UnityEngine;

// Occlusion culling in this game does not just skip draw calls - it switches the lights off.
//
// ToggleOcclusion (decompiled, Release/dnSpyResults/ToggleOcclusion.cs) hangs off a
// renderer and does this:
//
//     private void OnBecameInvisible() {
//         foreach (Light light in this.unityLights)      light.enabled = false;
//         foreach (GameObject go in this.occludedGameObjects) go.SetActive(false);
//     }
//
// So when culling hides a room's marker renderer, every light in that room is disabled.
// The geometry is still drawn - it is simply unlit. THAT is the pitch-black screen, not
// the world being culled away, and it is why occlusion_culling=0 helped on device and why
// the darkness survived a respawn: nothing re-enables those lights until the marker
// renderer becomes visible again.
//
// The important consequence: OnBecameInvisible fires for FRUSTUM culling too. Collapsing
// the far clip plane when the camera is inside geometry - the obvious-looking fix, and
// what this file did in its first revision - would switch MORE lights off, not fewer.
// A short view distance is still the right way to buy performance back (see
// VRManager.farClip), but it has to be a steady global value the player moves through,
// not something that snaps shut while they are standing in a wall.
//
// So this guard only ever turns occlusion culling OFF, never the view distance down.
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

        // Steady, and the same wherever the player is standing - so ToggleOcclusion's
        // light switching stays something they walk into and out of predictably.
        cam.farClipPlane = VRManager.farClip;
        // Fog is derived from the same number, here, so a live far_clip change from the
        // tuning menu moves the fog wall with it. See VRVisualTweaks.ApplyFog.
        VRVisualTweaks.ApplyFog();

        // With occlusion_culling=0 there is nothing to guard against, so skip the query.
        if (!VRManager.occlusionCulling)
        {
            cam.useOcclusionCulling = false;
            return;
        }

        bool inside = false;
        try
        {
            Marine m = Marine.LocalMarine;
            if (m != null)
            {
                // From inside the body up to the eye. If a wall lies between the two, the
                // camera has passed through geometry. Starting at chest height keeps the
                // origin reliably inside the player and below the head, so a normal
                // standing pose can never register a hit.
                Vector3 chest = m.transform.position + Vector3.up * 1.0f;
                inside = Physics.Linecast(chest, cam.transform.position, ignoreMask);
            }
        }
        catch { }

        cam.useOcclusionCulling = !inside;

        if (inside != lastInside)
        {
            lastInside = inside;
            try { VRManagerLogger.LogInfo("[IMVR] Camera inside geometry = " + inside + " -> occlusion culling " + (inside ? "OFF" : "ON")); } catch { }
        }
    }
}
