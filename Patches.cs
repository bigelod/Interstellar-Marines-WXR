using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// ===== MERGED: Your old Patches.cs + New visual goals =====
// Fixes applied:
// 2 - No DontDestroyOnLoad, VRRoot recreated per level in InitFirstPersonCamera
// 4 - No culling of layers 9/10 (was hiding weapon/hands)
// 5 - Mouse/keyboard blocked in VR gameplay only, allowed in menus (LocalMarine == null)
// 1 - Left as you fixed it (VRRoot = GameObject, not Transform)

public static class VRVisualTweaks
{
    public static void ApplyGlobal()
    {
        try
        {
            QualitySettings.shadowCascades = 0;
            QualitySettings.shadowDistance = 15f;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.pixelLightCount = 1;
            QualitySettings.shadowProjection = ShadowProjection.CloseFit;
        }
        catch { }
        try
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance = 75f;
            RenderSettings.fogDensity = 0.015f;
            RenderSettings.fogColor = new Color(0.08f, 0.1f, 0.12f);
        }
        catch { }
    }
}

[HarmonyPatch]
class Helmet_InitFirstPersonCamera_Patch
{
    static MethodBase TargetMethod()
    {
        Type t = AccessTools.TypeByName("Helmet");
        if (t == null) return null;
        return t.GetMethod("InitFirstPersonCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    }
    static void Postfix(object __instance)
    {
        XRAPI.ForceVRMode();
        // FIX 2 - Dynamically hook per level, no DontDestroyOnLoad
        // Destroy old VRRoot from previous level if it exists
        if (VRManagerState.VRRoot != null)
        {
            GameObject.Destroy(VRManagerState.VRRoot);
            VRManagerState.VRRoot = null;
        }
        GameObject root = new GameObject("VRRoot");
        // NOT DontDestroyOnLoad - dies with level, recreated next InitFirstPersonCamera
        VRManagerState.VRRoot = root;
        VRManagerLogger.LogInfo("[IMVR] Created VRManagerState.VRRoot for level");
        FieldInfo camField = AccessTools.Field(__instance.GetType(), "playerCamera");
        Camera cam = null;
        if (camField != null)
        {
            object camObj = camField.GetValue(__instance);
            cam = camObj as Camera;
        }
        if (cam != null && cam.GetComponent<VRHeadTracker>() == null)
        {
            cam.gameObject.AddComponent<VRHeadTracker>();
        }
        if (cam != null && cam.GetComponent<SyncPixelQuad>() == null)
        {
            cam.gameObject.AddComponent<SyncPixelQuad>();
        }
        // GRAPHICAL ONLY - NOT TRACKING: Hide the opaque 1p helmet mesh that sits below the VR camera.
        // Dump: CH_Marine1pHelmet (layer 8) + CH_Marine1pHelmetGlass (layer 8) are children of Helmet -> Root -> Helmet, NOT of WorldCamera.
        // WorldCamera (VR camera) is at (0.1,0.2,-0.2) above helmet center, so in VR you see the helmet mesh as a huge black disc on the left / below you (second screenshot).
        // In flat mode you need it for the visor frame, in VR the headset IS the visor, so it must be hidden. Keep layer 14 glass HUD (CH_Marine1pHelmetGlass_HUD) - that IS the neon HUD, rendered by HelmetCamera.
        try {
            MonoBehaviour helmMB = __instance as MonoBehaviour;
            if (helmMB != null) {
                Transform helmRoot = helmMB.transform;
                // Find all renderers under Helmet and disable only the opaque helmet meshes (layer 8)
                Renderer[] allR = helmRoot.GetComponentsInChildren<Renderer>(true);
                for (int i=0;i<allR.Length;i++) {
                    Renderer r = allR[i];
                    if (r == null) continue;
                    string rn = r.gameObject.name;
                    if (r.gameObject.layer == 8 && (rn.Contains("CH_Marine1pHelmet") && !rn.Contains("Glass_HUD"))) {
                        r.enabled = false;
                    }
                }
                // Also hide HelmetOccluder (layer 14, Invisible_ShadowCaster) - it occludes world in VR and makes the black disc worse
                // Do NOT hide CH_Marine1pHelmetGlass_HUD (layer 14, mat CH_1pHelmet_HUD_NGUI) - that is the neon green HUD surface
            }
        } catch { }
        // FIXED7: Glued HUD mesh, not camera anchor. NGUI camera stays at UI root (flicker was from moving it).
        // Parent the glass HUD mesh to VR camera, like it was parented to helmet in flat mode.
        try {
            // FIXED10: Do NOT add VRHUDAnchor to HUDCamera - it fights HUDCamera.Update (HUD UI Root parent) and flickers the texture. Glass mesh is the HUD.
        } catch { }
        // FIXED8: Filter was layer==14, but dump shows CH_Marine1pHelmetGlass_HUD is layer 0 (its children meshes are 14). So it was skipped.
        // Fix: match name only, any layer. And attach a per-frame anchor - Glass_HUD has an Animator that can reset parent/transform, so one-time SetParent in Init is not enough.
        try {
            MonoBehaviour helmMB = __instance as MonoBehaviour;
            if (helmMB != null && cam != null) {
                Transform[] allT = helmMB.GetComponentsInChildren<Transform>(true);
                for (int i=0;i<allT.Length;i++) {
                    Transform t = allT[i];
                    if (t == null) continue;
                    if (t.name.Contains("Glass_HUD")) {
                        if (t.GetComponent<VRGlassAnchor>() == null) t.gameObject.AddComponent<VRGlassAnchor>();
                        // Initial parent now - preserve original local pose, don't zero it (Animator pose)
                        if (t.parent != cam.transform) {
                            Vector3 oPos = t.localPosition; Quaternion oRot = t.localRotation;
                            t.SetParent(cam.transform, false);
                            t.localPosition = oPos;
                            t.localRotation = oRot;
                        }
                    }
                }
            }
        } catch { }

        // SyncPixelQuad finds top camera itself - do NOT add to all cameras here
        // That created 2-3 instances all re-parenting to same top cam
    }
}

// --- YOUR OLD INPUT / RECOIL / WEAPON PATCHES - KEEP AS IS ---
[HarmonyPatch]
class RangedWeapon_AttachToCamera_Patch
{
    static MethodBase TargetMethod()
    {
        Type t = AccessTools.TypeByName("RangedWeapon");
        if (t == null) return null;
        return t.GetMethod("AttachToCamera");
    }
    static void Postfix(object __instance)
    {
        MonoBehaviour mb = __instance as MonoBehaviour;
        if (mb == null) return;
        GameObject go = mb.gameObject;
        if (go != null && go.GetComponent<VRWeapon>() == null)
        {
            VRWeapon v = go.AddComponent<VRWeapon>();
            v.rightDominant = VRManager.configDominant == "right";
        }
    }
}

[HarmonyPatch]
class InputManager_IsInput_Patch
{
    static MethodBase TargetMethod()
    {
        Type t = AccessTools.TypeByName("InputManager");
        if (t == null) return null;
        MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo m in methods) { if (m.Name == "IsInput" && m.GetParameters().Length == 1) return m; }
        return null;
    }
    static bool Prefix(object keyBind, ref bool __result)
    {
        // FIX 5 - Allow mouse in menus/paused, block only in VR gameplay
        // XRAPI.UpdateVRModeFromGameState already sets inVR=false when paused,
        // but check again here: LocalMarine null OR GameManager.IsPaused OR Cursor visible = menu
        // FIX: Do NOT check Cursor.visible - in Winlator the cursor is ALWAYS visible even in-game, so that check let mouse+keyboard through always.
        // Only allow when actually in a menu: LocalMarine null, paused, or a menu UI is open. In VR gameplay, block everything.
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        string name = ""; if (keyBind != null) name = keyBind.ToString();
        // VR overrides win - if VR stick/button says this input is true, allow it
        bool v; if (VRInput.overridesBool.TryGetValue(name, out v) && v) { __result = true; return false; }
        if (name.Contains("Menu") || name.Contains("Confirm") || name.Contains("Cancel") || name.Contains("Pause") || name.Contains("Back") || name.Contains("Select")) return true;
        // Block ALL gameplay inputs from mouse/keyboard - Move, Strafe, Turn, Look, Fire, Aim, Mouse, plus raw WASD names
        if (name.Contains("Move") || name.Contains("Strafe") || name.Contains("Turn") || name.Contains("Look") || name.Contains("Fire") || name.Contains("Aim") || name.Contains("Mouse") || name.Contains("Forward") || name.Contains("Backward") || name.Contains("Left") || name.Contains("Right") || name.Contains("Jump") || name.Contains("Crouch") || name.Contains("Sprint") || name.Contains("Reload") || name.Contains("Use"))
        { __result = false; return false; }
        // If VR says this input is false (stick centered, trigger released), also block - don't fall through to original keyboard
        if (VRInput.overridesBool.ContainsKey(name)) { __result = false; return false; }
        return true;
        if (VRInput.overridesBool.ContainsKey(name) && (name.Contains("Move") || name.Contains("Strafe")))
        { bool val = VRInput.overridesBool[name]; if (!val) { __result = false; return false; } }
        return true;
    }
}

[HarmonyPatch]
class InputManager_IsInputDown_Patch
{
    static MethodBase TargetMethod()
    {
        Type t = AccessTools.TypeByName("InputManager");
        if (t == null) return null;
        MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo m in methods) { if (m.Name == "IsInputDown" && m.GetParameters().Length == 1) return m; }
        return null;
    }
    static bool Prefix(object keyBind, ref bool __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        string name = ""; if (keyBind != null) name = keyBind.ToString();
        bool v; if (VRInput.overridesBool.TryGetValue(name, out v) && v) { __result = true; return false; }
        if (name.Contains("Menu") || name.Contains("Confirm") || name.Contains("Cancel") || name.Contains("Pause") || name.Contains("Back") || name.Contains("Select")) return true;
        if (name.Contains("Move") || name.Contains("Strafe") || name.Contains("Turn") || name.Contains("Look") || name.Contains("Fire") || name.Contains("Aim") || name.Contains("Mouse") || name.Contains("Forward") || name.Contains("Backward") || name.Contains("Left") || name.Contains("Right") || name.Contains("Jump") || name.Contains("Crouch") || name.Contains("Sprint") || name.Contains("Reload") || name.Contains("Use"))
        { __result = false; return false; }
        if (VRInput.overridesBool.ContainsKey(name)) { __result = false; return false; }
        return true;
    }
}

[HarmonyPatch]
class NoHeadRecoil_VR_Patch
{
    static MethodBase TargetMethod() { return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForRecoil", new Type[] { typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(Vector2).MakeByRefType() }); }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state) { __state = new Vector2(__0, __1); }
    static void Postfix(ref float __0, ref float __1, Vector2 __state) { if (VRManagerState.inVR) { __0 = __state.x; __1 = __state.y; } }
}
[HarmonyPatch]
class NoHeadRecoil_Simple_VR_Patch
{
    static MethodBase TargetMethod() { return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForRecoil", new Type[] { typeof(float).MakeByRefType(), typeof(float).MakeByRefType() }); }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state) { __state = new Vector2(__0, __1); }
    static void Postfix(ref float __0, ref float __1, Vector2 __state) { if (VRManagerState.inVR) { __0 = __state.x; __1 = __state.y; } }
}
[HarmonyPatch]
class NoHeadLurch_VR_Patch
{
    static MethodBase TargetMethod() { return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForLurch"); }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state) { __state = new Vector2(__0, __1); }
    static void Postfix(ref float __0, ref float __1, Vector2 __state) { if (VRManagerState.inVR) { __0 = __state.x; __1 = __state.y; } }
}
[HarmonyPatch]
class NoHeadExhaustion_VR_Patch
{
    static MethodBase TargetMethod() { return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForExhaustion"); }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state) { __state = new Vector2(__0, __1); }
    static void Postfix(ref float __0, ref float __1, Vector2 __state) { if (VRManagerState.inVR) { __0 = __state.x; __1 = __state.y; } }
}

[HarmonyPatch(typeof(Helmet), "UpdateVisorBob")] class Helmet_VR_NoVisorBob { static bool Prefix() { return !VRManagerState.inVR; } }
[HarmonyPatch(typeof(Helmet), "UpdateDynamicVisorBob")] class Helmet_VR_NoDynamicBob { static bool Prefix() { return !VRManagerState.inVR; } }
[HarmonyPatch(typeof(Helmet), "UpdateBob")] class Helmet_VR_NoBob { static bool Prefix() { return !VRManagerState.inVR; } }

[HarmonyPatch(typeof(Helmet), "InitFirstPersonCamera")]
class Helmet_VR_NearClip
{
    static void Postfix(Camera ___playerCamera, Transform ___visorTransform, Vector3 ___helmetInitialOffset)
    {
        if (!VRManagerState.inVR) return;
        VRVisualTweaks.ApplyGlobal();

        if (___playerCamera != null)
        {
            // Only apply once - InitFirstPersonCamera can run multiple times per level, += would drift HUD
            if (Mathf.Abs(___playerCamera.transform.localPosition.y - 0.08f) > 0.01f) ___playerCamera.transform.localPosition += new Vector3(0f, 0.08f, 0.05f);
            ___playerCamera.nearClipPlane = 0.18f;
            ___playerCamera.farClipPlane = 80f;
            ___playerCamera.fieldOfView = 98f;
            // FIX 4 - Removed culling of layers 9/10 - was hiding weapon/hands
            // Old: ___playerCamera.cullingMask &= ~(1 << 9); etc.
        }
        if (___visorTransform != null)
        {
            ___visorTransform.localPosition = ___helmetInitialOffset + new Vector3(0f, 0f, 0.06f);
            ___visorTransform.localScale = Vector3.one;
            try
            {
                Renderer r = ___visorTransform.GetComponent<Renderer>();
                if (r != null && r.material != null) { r.material.mainTexture = null; r.material.color = new Color(1, 1, 1, 0.12f); }
            }
            catch { }
        }
    }
}

[HarmonyPatch(typeof(WeaponLight), "LateUpdate")]
class WeaponLight_VR_Head
{
    static void Postfix(WeaponLight __instance, GameObject ___flashLightGO, GameObject ___lightHitPosition)
    {
        if (___flashLightGO == null) return;
        if (!VRManagerState.inVR) return;
        if (!__instance.IsFlashlightOn) return;
        Marine localMarine = Marine.LocalMarine;
        if (localMarine == null || localMarine.Helmet == null) return;
        Camera headCam = Traverse.Create(localMarine.Helmet).Field("playerCamera").GetValue<Camera>();
        if (headCam == null) return;
        if (___flashLightGO.transform.parent != headCam.transform)
        {
            ___flashLightGO.transform.SetParent(headCam.transform, false);
            ___flashLightGO.transform.localPosition = new Vector3(0f, -0.06f, 0.25f);
            ___flashLightGO.transform.localRotation = Quaternion.identity;
            // FIX 4 - Removed light culling of 9/10 here too
        }
        if (___lightHitPosition != null && ___lightHitPosition.transform.parent != headCam.transform)
        {
            ___lightHitPosition.transform.SetParent(headCam.transform, false);
            ___lightHitPosition.transform.localPosition = Vector3.zero;
        }
    }
}

[HarmonyPatch(typeof(HUDCamera), "Update")]
class HUDCamera_VR_Shrink_Patch
{
    // FIXED from tree dump: HUDCamera is NOT the neon visor. Neon visor = layer 14 mesh rendered by HelmetCamera (culling 16384, child of WorldCamera).
    // HUDCamera (culling 1568 = UI,Debris,Precipitation, target CleanToBlendTarget) blends NGUI UI via doubleGlassMaterial onto the visor.
    // Setting glassIntensity=0, glassTexture=null, and clear=transparent (old code) killed that blend -> HUD completely missing (dump: glassIntensity=0, lastImage active but empty).
    // So: ONLY set hudScale and distort (safe for VR). Never touch glass, cullingMask, or clearFlags.
    static System.Collections.Generic.HashSet<int> fixedInstances = new System.Collections.Generic.HashSet<int>();
    static void Postfix(HUDCamera __instance)
    {
        if (!VRManagerState.inVR) return;
        int id = __instance.GetInstanceID();
        if (!fixedInstances.Contains(id))
        {
            __instance.hudScale = 1.0f;
            __instance.distortIntensityX = 0f;
            __instance.distortIntensityY = 0f;
            try { Traverse.Create(__instance).Field("lastHudScale").SetValue(1.0f); } catch { }
            // DO NOT set glassIntensity / glassOffset / glassTexture - doubleGlass needs them to show HUD
            // DO NOT set myCamera.cullingMask or clearFlags - original 1568 and Color clear are correct for the blend pipeline
            fixedInstances.Add(id);
        }
    }
}

// VRGlassAnchor - FIXED9: Only glue when helmet is ON/worn (visor Closed). When visor is Open / helmet up, HUD must NOT show - restore to original Helmet parent like flat mode.
// Dump: Helmet.currentVisorState = Closed when worn. Field is on Helmet component (Helmet(Clone) -> Root -> Helmet). We find it by walking up parents.
public class VRGlassAnchor : MonoBehaviour
{
    Transform originalParent;
    Vector3 originalLocalPos;
    Quaternion originalLocalRot;
    Component helmetComp;
    System.Reflection.FieldInfo visorStateField;
    void Awake()
    {
        originalParent = transform.parent;
        originalLocalPos = transform.localPosition;
        originalLocalRot = transform.localRotation;
        // Walk up to find Helmet component
        Transform p = transform.parent;
        while (p != null) {
            Component h = p.gameObject.GetComponent("Helmet");
            if (h != null) { helmetComp = h; visorStateField = h.GetType().GetField("currentVisorState", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance); break; }
            p = p.parent;
        }
    }
    bool IsHelmetOn()
    {
        // If we can't read state, default to on (glued) - flat mode default is Closed
        if (helmetComp == null || visorStateField == null) return true;
        try {
            object v = visorStateField.GetValue(helmetComp);
            if (v == null) return true;
            string s = v.ToString();
            // Closed = worn / on, Open = up / off - only glue when Closed
            return s == "Closed";
        } catch { return true; }
    }
    void LateUpdate()
    {
        if (!VRManagerState.inVR) return;
        if (Marine.LocalMarine == null) return;
        Camera headCam = null;
        Camera[] all = Camera.allCameras;
        if (all != null) for (int i=0;i<all.Length;i++) if (all[i]!=null && all[i].GetComponent<VRHeadTracker>()!=null) { headCam = all[i]; break; }
        if (headCam == null) return;
        Transform t = transform;
        if (IsHelmetOn()) {
            // Worn: glued to VR head like flat mode helmet attachment - preserve Animator pose, don't force identity
            // Dump: original localRot (1,359.7,0) is helmetClose pose. Forcing (0,0,0) every LateUpdate fights Animator in Update = flicker.
            // Only re-parent when needed, let Animator drive local pos/rot after that.
            if (t.parent != headCam.transform) {
                t.SetParent(headCam.transform, false);
                t.localPosition = originalLocalPos;
                t.localRotation = originalLocalRot;
            }
        } else {
            // Helmet up / visor open: restore to original Helmet parent - HUD disappears like flat mode (it's attached to the lifted helmet, not your face)
            if (originalParent != null && t.parent != originalParent) {
                t.SetParent(originalParent, false);
                t.localPosition = originalLocalPos;
                t.localRotation = originalLocalRot;
            }
        }
    }
}

// VRHUDAnchor - FIXED7: Only for HUDCamera (visor distortion). NGUI camera is NOT anchored - moving it caused flicker (it fights Fake 3D UI Root parent).
// The glued HUD is the GLASS MESH re-parented to WorldCamera in Helmet patch, not a camera move.
public class VRHUDAnchor : MonoBehaviour
{
    Camera hudCam;
    void Awake() { hudCam = GetComponent<Camera>(); if (hudCam == null) hudCam = GetComponentInChildren<Camera>(); }
    void OnPreRender()
    {
        if (!VRManagerState.inVR) return;
        if (Marine.LocalMarine == null) return;
        if (hudCam == null) return;
        Camera headCam = null;
        Camera[] all = Camera.allCameras;
        if (all != null) for (int i=0;i<all.Length;i++) if (all[i]!=null && all[i].GetComponent<VRHeadTracker>()!=null) { headCam = all[i]; break; }
        if (headCam == null) return;
        hudCam.transform.position = headCam.transform.position;
        hudCam.transform.rotation = headCam.transform.rotation;
    }
}

[HarmonyPatch(typeof(GUICompass), "UpdateCompass")]
class GUICompass_VR_HeadYaw_Patch
{
    static bool Prefix(GUICompass __instance)
    {
        if (!VRManagerState.inVR) return true;
        // Original uses Marine body yaw, in VR head yaw != body yaw, compass must follow head
        // Re-implement UpdateCompass with head yaw and 1.0x factor (not 1.8x flat exaggeration)
        try {
            Camera head = null; Camera[] all = Camera.allCameras; if (all!=null) for(int i=0;i<all.Length;i++) if(all[i]!=null && all[i].GetComponent<VRHeadTracker>()!=null) { head=all[i]; break; }
            float headYaw = head != null ? head.transform.rotation.eulerAngles.y : Marine.LocalMarine.transform.rotation.eulerAngles.y;
            // Call UpdateTicks via reflection with headYaw
            MethodInfo mi = AccessTools.Method(typeof(GUICompass), "UpdateTicks");
            FieldInfo dirF = AccessTools.Field(typeof(GUICompass), "directionLabels");
            FieldInfo numF = AccessTools.Field(typeof(GUICompass), "directionNumberLabels");
            FieldInfo tickF = AccessTools.Field(typeof(GUICompass), "tickLabels");
            FieldInfo dirOffF = AccessTools.Field(typeof(GUICompass), "directionHeightOffset");
            FieldInfo numOffF = AccessTools.Field(typeof(GUICompass), "directionNumberHeightOffset");
            // Use original UpdateTicks but with headYaw - it still has 1.8x inside, but head yaw is correct source
            // To avoid rewriting whole method, just let original run but temporarily swap Marine yaw? Simpler: call original logic by setting a temp?
            // For now, let original UpdateTicks run with headYaw via invoking it
            mi.Invoke(__instance, new object[]{ dirF.GetValue(__instance), headYaw, dirOffF.GetValue(__instance), true });
            mi.Invoke(__instance, new object[]{ numF.GetValue(__instance), headYaw, numOffF.GetValue(__instance), true });
            mi.Invoke(__instance, new object[]{ tickF.GetValue(__instance), headYaw, 0f, true });
        } catch { }
        return false; // skip original which uses body yaw
    }
}

[HarmonyPatch(typeof(Helmet), "SetRenderMode")]
class Helmet_VR_NearClip_2 { static void Postfix(Camera ___playerCamera) { if (VRManagerState.inVR && ___playerCamera != null) ___playerCamera.nearClipPlane = 0.18f; } }

[HarmonyPatch]
class InputManager_IsInput_Debug
{
    static MethodBase TargetMethod()
    {
        Type t = AccessTools.TypeByName("InputManager");
        return t.GetMethod("IsInput", BindingFlags.Public | BindingFlags.Static);
    }
    static void Prefix(object keyBind)
    {
        if (VRManagerState.verboseDebug) VRManagerLogger.LogInfo("[IMVR DEBUG] IsInput called: " + keyBind.ToString() + " overridesHas=" + VRInput.overridesBool.ContainsKey(keyBind.ToString()));
    }
}

// === RAW UNITY INPUT BLOCK - game reads Input.GetAxis / GetKey directly, bypassing InputManager.IsInput ===
// If we only block InputManager, mouse look (Mouse X/Y) and WASD via Unity Input still work. Block those too in VR gameplay.
[HarmonyPatch(typeof(UnityEngine.Input), "GetAxis")]
class UnityInput_GetAxis_Block
{
    static bool Prefix(string axisName, ref float __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        if (axisName == null) return true;
        if (axisName == "Mouse X" || axisName == "Mouse Y" || axisName == "Horizontal" || axisName == "Vertical" || axisName.Contains("Mouse") || axisName.Contains("Look"))
        { __result = 0f; return false; }
        return true;
    }
}
[HarmonyPatch(typeof(UnityEngine.Input), "GetAxisRaw")]
class UnityInput_GetAxisRaw_Block
{
    static bool Prefix(string axisName, ref float __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        if (axisName == null) return true;
        if (axisName == "Mouse X" || axisName == "Mouse Y" || axisName == "Horizontal" || axisName == "Vertical" || axisName.Contains("Mouse") || axisName.Contains("Look"))
        { __result = 0f; return false; }
        return true;
    }
}
[HarmonyPatch(typeof(UnityEngine.Input), "GetKey", new System.Type[]{typeof(UnityEngine.KeyCode)})]
class UnityInput_GetKey_Block
{
    static bool Prefix(UnityEngine.KeyCode key, ref bool __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        // Block WASD, arrows, space, shift, mouse buttons in VR gameplay - VR overrides handle these via InputManager
        if (key == UnityEngine.KeyCode.W || key == UnityEngine.KeyCode.A || key == UnityEngine.KeyCode.S || key == UnityEngine.KeyCode.D ||
            key == UnityEngine.KeyCode.UpArrow || key == UnityEngine.KeyCode.DownArrow || key == UnityEngine.KeyCode.LeftArrow || key == UnityEngine.KeyCode.RightArrow ||
            key == UnityEngine.KeyCode.Space || key == UnityEngine.KeyCode.LeftShift || key == UnityEngine.KeyCode.RightShift ||
            key == UnityEngine.KeyCode.Mouse0 || key == UnityEngine.KeyCode.Mouse1 || key == UnityEngine.KeyCode.Mouse2)
        { __result = false; return false; }
        return true;
    }
}
[HarmonyPatch(typeof(UnityEngine.Input), "GetKeyDown", new System.Type[]{typeof(UnityEngine.KeyCode)})]
class UnityInput_GetKeyDown_Block
{
    static bool Prefix(UnityEngine.KeyCode key, ref bool __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        if (key == UnityEngine.KeyCode.W || key == UnityEngine.KeyCode.A || key == UnityEngine.KeyCode.S || key == UnityEngine.KeyCode.D ||
            key == UnityEngine.KeyCode.UpArrow || key == UnityEngine.KeyCode.DownArrow || key == UnityEngine.KeyCode.LeftArrow || key == UnityEngine.KeyCode.RightArrow ||
            key == UnityEngine.KeyCode.Space || key == UnityEngine.KeyCode.LeftShift || key == UnityEngine.KeyCode.RightShift ||
            key == UnityEngine.KeyCode.Mouse0 || key == UnityEngine.KeyCode.Mouse1 || key == UnityEngine.KeyCode.Mouse2)
        { __result = false; return false; }
        return true;
    }
}
[HarmonyPatch(typeof(UnityEngine.Input), "GetMouseButton")]
class UnityInput_GetMouseButton_Block
{
    static bool Prefix(int button, ref bool __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        __result = false; return false;
    }
}
[HarmonyPatch(typeof(UnityEngine.Input), "GetMouseButtonDown")]
class UnityInput_GetMouseButtonDown_Block
{
    static bool Prefix(int button, ref bool __result)
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        __result = false; return false;
    }
}
public class VRInputRunner : MonoBehaviour
{
    void Update()
    {
        try { VRInput.UpdateFrame(); }
        catch (Exception e) { VRManagerLogger.LogError("[IMVR] VRInput Update failed: " + e); }
        if (VRManagerState.verboseDebug && Time.frameCount % 60 == 0)
        {
            XRAPI.State s;
            XRAPI.GetBoolState(out s);
            uint lb, rb; Vector2 ls, rs; float lt, rt, lg, rg;
            XRAPI.XR_GetControllerState(0, out lb, out ls, out lt, out lg);
            XRAPI.XR_GetControllerState(1, out rb, out rs, out rt, out rg);
            VRManagerLogger.LogInfo("[IMVR DEBUG] ls=" + ls.ToString() + " rs=" + rs.ToString() + " rt=" + rt.ToString() + " state.rTrigger=" + s.rTrigger.ToString() + " lStick=" + s.leftStick.ToString());
        }
    }
}
