using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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

        var helm = __instance as MonoBehaviour;

        // NEW: Attach to Helmet base so Root/Helmet visor AND WorldCamera both move with HMD
        if (helm != null && helm.GetComponent<VRHeadTracker>() == null)
        {
            helm.gameObject.AddComponent<VRHeadTracker>();
            VRManagerLogger.LogInfo("[IMVR] Added VRHeadTracker to Helmet base");
        }
        else
        {
            // Your original camera fallback - keep for safety
            var playerCamField = AccessTools.Field(__instance.GetType(), "playerCamera");
            var cam = null as Camera;
            if (playerCamField != null) cam = playerCamField.GetValue(__instance) as Camera;
            // Only add to camera if Helmet base didn't get it (old behavior)
            if (cam != null && cam.GetComponent<VRHeadTracker>() == null && helm.GetComponent<VRHeadTracker>() == null)
            {
                cam.gameObject.AddComponent<VRHeadTracker>();
                VRManagerLogger.LogInfo("[IMVR] Added VRHeadTracker to WorldCamera fallback");
            }

            if (cam != null)
            {
                if (cam.GetComponent<SyncPixelQuad>() == null)
                {
                    cam.gameObject.AddComponent<SyncPixelQuad>();
                    VRManagerLogger.LogInfo("[IMVR] Added SyncPixelQuad");
                }
            }
        }
    }
}

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
        foreach (MethodInfo m in methods)
        {
            if (m.Name == "IsInput" && m.GetParameters().Length == 1) return m;
        }
        return null;
    }
    static bool Prefix(object keyBind, ref bool __result)
    {
        string name = "";
        if (keyBind != null) name = keyBind.ToString();
        bool v;
        if (VRInput.overridesBool.TryGetValue(name, out v) && v) { __result = true; return false; }
        if (VRInput.overridesBool.ContainsKey(name) && (name.Contains("Move") || name.Contains("Strafe")))
        {
            bool val = VRInput.overridesBool[name];
            if (!val) { __result = false; return false; }
        }
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
        foreach (MethodInfo m in methods)
        {
            if (m.Name == "IsInputDown" && m.GetParameters().Length == 1) return m;
        }
        return null;
    }
    static bool Prefix(object keyBind, ref bool __result)
    {
        string name = "";
        if (keyBind != null) name = keyBind.ToString();
        bool v;
        if (VRInput.overridesBool.TryGetValue(name, out v) && v) { __result = true; return false; }
        return true;
    }
}

[HarmonyPatch]
class NoHeadRecoil_VR_Patch
{
    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForRecoil",
            new Type[] { typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(Vector2).MakeByRefType() });
    }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state)
    {
        __state = new Vector2(__0, __1); // save mouse-only yaw/pitch
    }
    static void Postfix(ref float __0, ref float __1, Vector2 __state)
    {
        if (VRManagerState.inVR)
        {
            __0 = __state.x;
            __1 = __state.y;
        }
    }
}

// --- 2. Recoil without retainedRecoil ---
[HarmonyPatch]
class NoHeadRecoil_Simple_VR_Patch
{
    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForRecoil",
            new Type[] { typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });
    }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state)
    {
        __state = new Vector2(__0, __1);
    }
    static void Postfix(ref float __0, ref float __1, Vector2 __state)
    {
        if (VRManagerState.inVR)
        {
            __0 = __state.x;
            __1 = __state.y;
        }
    }
}

// --- 3. Lurch (return to center) ---
[HarmonyPatch]
class NoHeadLurch_VR_Patch
{
    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForLurch");
    }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state)
    {
        __state = new Vector2(__0, __1);
    }
    static void Postfix(ref float __0, ref float __1, Vector2 __state)
    {
        if (VRManagerState.inVR)
        {
            __0 = __state.x;
            __1 = __state.y;
        }
    }
}

// --- 4. Exhaustion sway ---
[HarmonyPatch]
class NoHeadExhaustion_VR_Patch
{
    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(RangedWeapon), "OwnerProcessViewRotationForExhaustion");
    }
    static void Prefix(ref float __0, ref float __1, out Vector2 __state)
    {
        __state = new Vector2(__0, __1);
    }
    static void Postfix(ref float __0, ref float __1, Vector2 __state)
    {
        if (VRManagerState.inVR)
        {
            __0 = __state.x;
            __1 = __state.y;
        }
    }
}

[HarmonyPatch(typeof(Helmet), "UpdateVisorBob")]
class Helmet_VR_NoVisorBob
{
    static bool Prefix()
    {
        if (VRManagerState.inVR) return false;
        return true;
    }
}

[HarmonyPatch(typeof(Helmet), "UpdateDynamicVisorBob")]
class Helmet_VR_NoDynamicBob
{
    static bool Prefix()
    {
        if (VRManagerState.inVR) return false;
        return true;
    }
}

[HarmonyPatch(typeof(Helmet), "UpdateBob")]
class Helmet_VR_NoBob
{
    static bool Prefix()
    {
        if (VRManagerState.inVR) return false;
        return true;
    }
}

// FLASHLIGHT - patch LateUpdate, not Update
[HarmonyPatch(typeof(WeaponLight), "LateUpdate")]
class WeaponLight_VR_Head
{
    static Transform originalParent;
    static Vector3 originalOffset;
    static bool saved = false;

    static void Postfix(WeaponLight __instance, GameObject ___flashLightGO, Vector3 ___flashlightPrefabOffset)
    {
        if (___flashLightGO == null) return;
        if (!VRManagerState.inVR)
        {
            // restore to gun when back in 2D
            if (saved && originalParent != null && ___flashLightGO.transform.parent != originalParent)
            {
                ___flashLightGO.transform.parent = originalParent;
                ___flashLightGO.transform.localPosition = originalOffset;
                ___flashLightGO.transform.localRotation = Quaternion.identity;
            }
            return;
        }

        if (!__instance.IsFlashlightOn) return;

        Marine localMarine = Marine.LocalMarine;
        if (localMarine == null) return;
        if (localMarine.Helmet == null) return;

        Camera headCam = Traverse.Create(localMarine.Helmet).Field("playerCamera").GetValue<Camera>();
        if (headCam == null) return;

        if (!saved)
        {
            originalParent = ___flashLightGO.transform.parent;
            originalOffset = ___flashlightPrefabOffset;
            saved = true;
        }

        // attach to head - fixes wall blocking
        if (___flashLightGO.transform.parent != headCam.transform)
        {
            ___flashLightGO.transform.parent = headCam.transform;
            ___flashLightGO.transform.localPosition = new Vector3(0f, -0.06f, 0.25f);
            ___flashLightGO.transform.localRotation = Quaternion.identity;
        }
    }
}

// HUD - no CanvasScaler type reference
[HarmonyPatch(typeof(HUDCamera), "Update")]
class HUDCamera_VR_Shrink_Patch
{
    static void Postfix(HUDCamera __instance)
    {
        if (!VRManagerState.inVR) return;

        // bigger = smaller HUD (inverse of 0.5)
        float targetScale = 1.8f;
        if (Mathf.Abs(__instance.hudScale - targetScale) > 0.01f)
        {
            __instance.hudScale = targetScale;
        }

        // use Traverse so you don't need UnityEngine.UI.dll referenced
        object scalerObj = Traverse.Create(__instance).Field("mainCanvasScaler").GetValue();
        if (scalerObj != null)
        {
            Traverse.Create(scalerObj).Property("referenceResolution").SetValue(new Vector2(5500f, 3093f));
        }

        __instance.distortIntensityX = 0.25f;
        __instance.distortIntensityY = 0.25f;
        __instance.glassIntensity = 0.3f;
    }
}

[HarmonyPatch(typeof(Helmet), "InitFirstPersonCamera")]
class Helmet_VR_NearClip
{
    static void Postfix(Camera ___playerCamera, Transform ___visorTransform, Vector3 ___helmetInitialOffset)
    {
        if (!VRManagerState.inVR) return;

        if (___playerCamera != null)
        {
            ___playerCamera.nearClipPlane = 0.18f;
        }
        if (___visorTransform != null)
        {
            ___visorTransform.localPosition = ___helmetInitialOffset + new Vector3(0f, 0f, 0.06f);
            ___visorTransform.localScale = Vector3.one;
        }
    }
}