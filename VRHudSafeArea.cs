using System.Collections.Generic;
using UnityEngine;

// Shrinks the screen-anchored helmet HUD toward the centre of the view so it is
// readable in a headset, WITHOUT touching the bend pipeline.
//
// ===== WHY EVERY PREVIOUS ATTEMPT FAILED (Docs/MetaAI/BUG-FIXES.md) =====
//   HUD UI Root scale          -> moves HUDCamera, breaks the bend, HUD only shows paused
//   HUDCamera.hudScale         -> render-target resolution, not screen real estate
//   doubleGlassMaterial UV     -> the shader resets mainTextureScale
//   HUDCamera frustum          -> wrong camera; it bends an already-rendered texture
//   HUDCamera.rect             -> cyan border, because clear colour fills the rest of the RT
//   Glass mesh scale           -> doubleGlass projects in screen space, mesh scale is irrelevant
//   CanvasScaler.scaleFactor   -> shrank the compass but edge-anchored widgets stayed pinned
//
// That last one is the clue, and it is the whole reason this file exists.
//
// The HUD is uGUI, not NGUI - Assembly-CSharp references UnityEngine.UI, and HUDCamera
// has a mainCanvasScaler. In uGUI a widget anchored to the bottom-left stays at the
// bottom-left however you scale the canvas, because its position is defined relative to
// its PARENT RECT, not in absolute screen units. Scaling the canvas scales the glyphs
// and leaves the anchors where they were - exactly the reported symptom.
//
// So: give the content a smaller parent rect. One stretched RectTransform inserted
// between the Canvas and its children, scaled about its own centre, pulls every anchored
// child inward AND shrinks it by the same factor. This is the standard uGUI "safe area"
// pattern used for notched phone displays, and it touches no camera, no material, no
// culling mask and no render target - so the bend pipeline cannot notice it.
//
// World-space markers (the diamond waypoints) must NOT move, so canvases and children
// whose names look like world markers are skipped - see SKIP_NAMES.
public static class VRHudSafeArea
{
    public const string CONTAINER_NAME = "IMVR_HudSafeArea";

    // Canvases and top-level children matching any of these are left alone, because they
    // are projected from world positions rather than anchored to the screen. Shrinking
    // them would pull markers away from the things they point at.
    static readonly string[] SKIP_NAMES = new string[] {
        "waypoint", "marker", "objective", "tracker", "world", "nametag", "name tag"
    };

    static List<RectTransform> containers = new List<RectTransform>();
    static float lastScanTime = -999f;
    static bool loggedOnce = false;

    public static void Apply(float scale, bool active)
    {
        float target = active ? scale : 1f;

        // hudScale=1.0 is a true no-op escape hatch: if this mechanism ever misbehaves on
        // device, setting 1.0 in config.txt stops it installing anything at all.
        if (target >= 0.999f && containers.Count == 0) return;

        // Canvases are rebuilt per level and HUD widgets spawn at runtime, so rescan
        // periodically rather than once. Cheap: a couple of GetComponentsInChildren every 2s.
        if (Time.realtimeSinceStartup - lastScanTime > 2f)
        {
            lastScanTime = Time.realtimeSinceStartup;
            Scan();
        }

        for (int i = 0; i < containers.Count; i++)
        {
            RectTransform c = containers[i];
            if (c == null) continue;
            if (Mathf.Abs(c.localScale.x - target) > 0.0001f)
            {
                c.localScale = new Vector3(target, target, 1f);
            }
        }
    }

    static bool ShouldSkip(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string low = name.ToLower();
        for (int i = 0; i < SKIP_NAMES.Length; i++)
        {
            if (low.Contains(SKIP_NAMES[i])) return true;
        }
        return false;
    }

    static void Scan()
    {
        containers.Clear();
        try
        {
            // Scope the search to the HUD UI Root subtree - the parent of HUDCamera. Menu
            // and lobby canvases live elsewhere and must stay full-screen.
            if (HUDCamera.Instance == null) return;
            Transform hudRoot = HUDCamera.Instance.transform.parent;
            if (hudRoot == null) return;

            Canvas[] canvases = hudRoot.GetComponentsInChildren<Canvas>(true);
            if (canvases == null) return;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null) continue;
                if (ShouldSkip(canvas.name))
                {
                    LogOnce("[IMVR] SafeArea skip canvas (world-space): " + canvas.name);
                    continue;
                }

                RectTransform canvasRT = canvas.GetComponent<RectTransform>();
                if (canvasRT == null) continue;

                RectTransform container = GetOrCreateContainer(canvasRT);
                if (container == null) continue;

                AdoptChildren(canvasRT, container);
                containers.Add(container);
            }
            loggedOnce = true;
        }
        catch { }
    }

    static RectTransform GetOrCreateContainer(RectTransform canvasRT)
    {
        // Reuse an existing container - this runs every 2s and must not stack containers
        for (int i = 0; i < canvasRT.childCount; i++)
        {
            Transform c = canvasRT.GetChild(i);
            if (c != null && c.name == CONTAINER_NAME) return c as RectTransform;
        }

        GameObject go = new GameObject(CONTAINER_NAME, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvasRT, false);
        // Stretch to fill the canvas exactly, pivot at centre. The rect then matches the
        // canvas, so children keep their anchored positions when adopted, and localScale
        // scales the whole lot about the centre of the view.
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one; // adopt at 1, scale afterwards - see AdoptChildren
        LogOnce("[IMVR] SafeArea created container under canvas: " + canvasRT.name);
        return rt;
    }

    static void AdoptChildren(RectTransform canvasRT, RectTransform container)
    {
        // Adopt at scale 1 so anchored positions transfer unchanged, then let Apply scale it.
        Vector3 held = container.localScale;
        container.localScale = Vector3.one;

        // Iterate backwards: SetParent mutates the child list we are walking.
        for (int i = canvasRT.childCount - 1; i >= 0; i--)
        {
            Transform child = canvasRT.GetChild(i);
            if (child == null) continue;
            if (child == container.transform) continue;
            if (ShouldSkip(child.name))
            {
                LogOnce("[IMVR] SafeArea skip child (world-space): " + child.name);
                continue;
            }
            // Only RectTransforms belong under a stretched UI container
            if ((child as RectTransform) == null) continue;

            LogOnce("[IMVR] SafeArea adopt: " + canvasRT.name + "/" + child.name);
            child.SetParent(container, false);
        }

        container.localScale = held;
    }

    static void LogOnce(string msg)
    {
        // First scan only, so a 2s rescan does not spam the log. Verbose-gated after that.
        if (loggedOnce && !VRManagerState.verboseDebug) return;
        try { VRManagerLogger.LogInfo(msg); } catch { }
    }
}
