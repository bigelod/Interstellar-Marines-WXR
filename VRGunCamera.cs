using UnityEngine;

// Full-resolution first-person weapon on top of the low-res world.
//
// VRWorldScaler renders WorldCamera into a small RT so the world/body are cheap and
// upscaled. The gun does not need to pay that cost: it is close to the eye, its edges
// are the most visible thing in the frame, and it is already a separate world object.
// So the weapon renderers are moved to their own layer, WorldCamera stops drawing that
// layer, and this camera - a child of WorldCamera, same pose and projection - renders
// only the gun at the real window resolution after the world composite.
//
// It clears DEPTH only: the upscaled world underneath must survive. That also means the
// gun is not occluded by world geometry; that is the usual viewmodel trade. The HUD
// cameras are at depth 1, so the visor still draws over it.
public static class VRGunCamera
{
    // Layer 30 is one of the unnamed spare layers; the world camera includes it in the
    // dump, so it is removed while this camera is active. Layer 31 is the composite quad.
    public const int GunLayer = 30;

    static Camera gunCam;
    static Camera worldCam;
    static GameObject go;

    static GameObject cachedModel;
    static Renderer[] cachedRenderers;

    public static bool Active
    {
        get { return gunCam != null && gunCam.enabled; }
    }

    public static void Tick()
    {
        if (!VRManager.gunFullRes || !VRWorldScaler.CompositeActive)
        {
            SetInactive();
            return;
        }

        Camera wc = VRHands.FindHeadCamera();
        if (wc == null)
        {
            SetInactive();
            return;
        }

        if (gunCam == null || worldCam != wc) Create(wc);

        gunCam.enabled = true;
        // The world quad is overscaled/cropped by quad_overscale, which narrows the
        // visible FOV. Render the gun with the same effective FOV so its size and
        // position still match the world image behind it.
        float over = Mathf.Clamp(VRManager.quadOverscale, 1f, 1.5f);
        float halfFov = wc.fieldOfView * 0.5f * Mathf.Deg2Rad;
        gunCam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(halfFov) / over) * Mathf.Rad2Deg;
        gunCam.nearClipPlane = wc.nearClipPlane;
        gunCam.farClipPlane = wc.farClipPlane;
        gunCam.transform.localPosition = Vector3.zero;
        // hands_roll=1 keeps the gun fixed on screen; hands_roll=0 rolls it with the
        // world image. The model itself is left in its raw controller pose below, so
        // this camera rotation is the only roll applied to the full-res gun.
        gunCam.transform.localRotation = VRManager.handsRoll
            ? Quaternion.identity
            : Quaternion.Euler(0f, 0f, VRWorldScaler.CompositeRollDeg);

        // Keep the gun out of the low-res world pass every frame; the game can rewrite
        // the camera mask on a level or mode change.
        wc.cullingMask &= ~(1 << GunLayer);
    }

    public static void PrepareWeapon(GameObject model)
    {
        if (model == null) return;
        int want = Active ? GunLayer : PlayerLayer();

        // Renderer list is cached per weapon model: this runs every frame and must not
        // allocate. A weapon swap or level change replaces the model and rescans then.
        if (model != cachedModel || cachedRenderers == null)
        {
            cachedModel = model;
            cachedRenderers = model.GetComponentsInChildren<Renderer>(true);
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null && cachedRenderers[i].gameObject.layer != want)
                cachedRenderers[i].gameObject.layer = want;
        }
    }

    static void Create(Camera wc)
    {
        Destroy();
        worldCam = wc;

        go = new GameObject("IMVR_GunCamera");
        go.transform.SetParent(wc.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        gunCam = go.AddComponent<Camera>();
        gunCam.depth = 0.75f;                    // after the world composite (0.5), before HUD (1)
        gunCam.clearFlags = CameraClearFlags.Depth;
        gunCam.cullingMask = (1 << GunLayer);
        gunCam.orthographic = false;
        gunCam.fieldOfView = wc.fieldOfView;
        gunCam.nearClipPlane = wc.nearClipPlane;
        gunCam.farClipPlane = wc.farClipPlane;
        gunCam.useOcclusionCulling = false;
        gunCam.allowHDR = false;
        gunCam.allowMSAA = false;
        gunCam.targetTexture = null;
        try { gunCam.renderingPath = RenderingPath.Forward; } catch { }
        gunCam.enabled = true;

        try { VRManagerLogger.LogInfo("[IMVR] Full-res gun camera created (layer " + GunLayer + ")"); } catch { }
    }

    static void SetInactive()
    {
        if (gunCam != null) gunCam.enabled = false;
        if (worldCam != null) worldCam.cullingMask |= (1 << GunLayer);
    }

    static void Destroy()
    {
        if (worldCam != null) worldCam.cullingMask |= (1 << GunLayer);
        if (go != null) Object.Destroy(go);
        go = null;
        gunCam = null;
        worldCam = null;
    }

    static int PlayerLayer()
    {
        int l = LayerMask.NameToLayer("Player");
        return l >= 0 ? l : 0;
    }
}
