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
        MonoBehaviour helm = __instance as MonoBehaviour;
        if (helm == null) return;

        // Attach tracker to Helmet base so both camera and visor move
        if (helm.GetComponent<VRHeadTracker>() == null)
        {
            helm.gameObject.AddComponent<VRHeadTracker>();
            Debug.Log("[IMVR] Added VRHeadTracker to Helmet base");
        }

        // Also ensure camera has tracker fallback if Helmet patch fails
        FieldInfo playerCamField = AccessTools.Field(__instance.GetType(), "playerCamera");
        if (playerCamField != null)
        {
            object camObj = playerCamField.GetValue(__instance);
            Camera cam = camObj as Camera;
            if (cam != null && cam.GetComponent<VRHeadTracker>() == null && helm.GetComponent<VRHeadTracker>() == null)
            {
                cam.gameObject.AddComponent<VRHeadTracker>();
                Debug.Log("[IMVR] Added VRHeadTracker to WorldCamera fallback");
            }
        }
    }
}

[HarmonyPatch]
class RangedWeapon_AttachToCamera_Patch
{
    static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("RangedWeapon");
        if (t != null)
        {
            return t.GetMethod("AttachToCamera");
        }

        return null;
    }
    static void Postfix(object __instance)
    {
        var go = null as GameObject;

        if (__instance != null && (__instance as MonoBehaviour) != null)
        {
            go = (__instance as MonoBehaviour).gameObject;
        }

        if (go != null && go.GetComponent<VRWeapon>() == null)
        {
            var v = go.AddComponent<VRWeapon>();
            v.rightDominant = VRManager.configDominant == "right";
            v.flipHands = VRManager.configFlipHands == 1;
        }
    }
}

[HarmonyPatch]
class InputManager_IsInput_Patch
{
    static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("InputManager");
        if (t == null) return null;
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
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
            if (!VRInput.overridesBool[name]) { __result = false; return false; }
        }
        return true;
    }
}

[HarmonyPatch]
class InputManager_IsInputDown_Patch
{
    static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("InputManager");
        if (t == null) return null;
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
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
