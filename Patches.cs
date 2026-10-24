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
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }
        try { if (Cursor.visible && Cursor.lockState != CursorLockMode.Locked) return true; } catch { }
        string name = ""; if (keyBind != null) name = keyBind.ToString();
        bool v; if (VRInput.overridesBool.TryGetValue(name, out v) && v) { __result = true; return false; }
        if (name.Contains("Menu") || name.Contains("Confirm") || name.Contains("Cancel") || name.Contains("Pause") || name.Contains("Back") || name.Contains("Select")) return true;
        if (name.Contains("Move") || name.Contains("Strafe") || name.Contains("Turn") || name.Contains("Look") || name.Contains("Fire") || name.Contains("Aim") || name.Contains("Mouse"))
        { __result = false; return false; }
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
        string name = ""; if (keyBind != null) name = keyBind.ToString();
        bool v; if (VRInput.overridesBool.TryGetValue(name, out v) && v) { __result = true; return false; }
        if (name.Contains("Menu") || name.Contains("Confirm") || name.Contains("Cancel") || name.Contains("Pause") || name.Contains("Back") || name.Contains("Select")) return true;
        if (name.Contains("Move") || name.Contains("Strafe") || name.Contains("Turn") || name.Contains("Look") || name.Contains("Fire") || name.Contains("Aim") || name.Contains("Mouse"))
        { __result = false; return false; }
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
    static void Postfix(HUDCamera __instance)
    {
        if (!VRManagerState.inVR) return;
        __instance.hudScale = 1.2f;
        object scalerObj = Traverse.Create(__instance).Field("mainCanvasScaler").GetValue();
        if (scalerObj != null) Traverse.Create(scalerObj).Property("referenceResolution").SetValue(new Vector2(4200f, 2362f));
        __instance.distortIntensityX = 0.15f;
        __instance.distortIntensityY = 0.15f;
        __instance.glassIntensity = 0f;
        __instance.glassOffset = 0f;
        try { Traverse.Create(__instance).Field("glassTexture").SetValue(null); } catch { }
        object lastImageObj = Traverse.Create(__instance).Field("lastImage").GetValue();
        if (lastImageObj != null)
        {
            RectTransform rt = Traverse.Create(lastImageObj).Property("rectTransform").GetValue<RectTransform>();
            if (rt != null) { rt.localScale = new Vector3(0.68f, 0.68f, 1f); rt.anchoredPosition = Vector2.zero; }
        }
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
