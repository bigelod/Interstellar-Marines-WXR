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
        if(t==null) return null;
        return t.GetMethod("InitFirstPersonCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    }
    static void Postfix(object __instance)
    {
        FieldInfo playerCamField = AccessTools.Field(__instance.GetType(), "playerCamera");
        if(playerCamField==null) return;
        object camObj = playerCamField.GetValue(__instance);
        Camera cam = camObj as Camera;
        if(cam!=null && cam.GetComponent<VRHeadTracker>()==null){
            XRAPI.ForceVRMode();
            cam.gameObject.AddComponent<VRHeadTracker>();
            Debug.Log("[IMVR] Added VRHeadTracker");
        }

        if (cam != null)
        {
            if (cam.GetComponent<SyncPixelQuad>() == null)
            {
                cam.gameObject.AddComponent<SyncPixelQuad>();
                Debug.Log("[IMVR] Added SyncPixelQuad");
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
        if(t==null) return null;
        return t.GetMethod("AttachToCamera");
    }
    static void Postfix(object __instance)
    {
        MonoBehaviour mb = __instance as MonoBehaviour;
        if(mb==null) return;
        GameObject go = mb.gameObject;
        if(go!=null && go.GetComponent<VRWeapon>()==null){
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
        if(t==null) return null;
        MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach(MethodInfo m in methods){
            if(m.Name=="IsInput" && m.GetParameters().Length==1) return m;
        }
        return null;
    }
    static bool Prefix(object keyBind, ref bool __result)
    {
        string name = "";
        if(keyBind!=null) name = keyBind.ToString();
        bool v;
        if(VRInput.overridesBool.TryGetValue(name, out v) && v){ __result=true; return false; }
        if(VRInput.overridesBool.ContainsKey(name) && (name.Contains("Move") || name.Contains("Strafe"))){
            bool val = VRInput.overridesBool[name];
            if(!val){ __result=false; return false; }
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
        if(t==null) return null;
        MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach(MethodInfo m in methods){
            if(m.Name=="IsInputDown" && m.GetParameters().Length==1) return m;
        }
        return null;
    }
    static bool Prefix(object keyBind, ref bool __result)
    {
        string name = "";
        if(keyBind!=null) name = keyBind.ToString();
        bool v;
        if(VRInput.overridesBool.TryGetValue(name, out v) && v){ __result=true; return false; }
        return true;
    }
}
