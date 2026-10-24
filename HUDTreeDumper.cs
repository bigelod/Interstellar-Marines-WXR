using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using HarmonyLib;
using System.IO;

// C# 3 compatible - no interpolated strings, no null-conditional, no pattern matching
public static class HUDTreeDumper
{
    public static string lastDumpPath = null;
    public static string GetDumpPath()
    {
        try {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(dllDir)) return Path.Combine(dllDir, "hud_tree.txt");
        } catch {}
        try { return Path.Combine(Directory.GetCurrentDirectory(), "hud_tree.txt"); } catch {}
        return "hud_tree.txt";
    }
    public static void DumpAll(object helmetInstance)
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[IMVR-TREE] ===== DUMP START =====");

            sb.AppendLine("[IMVR-TREE] --- All Cameras ---");
            Camera[] all = Camera.allCameras;
            if (all != null) for (int i=0;i<all.Length;i++) {
                Camera c = all[i];
                if (c == null) continue;
                string targetName = "null";
                if (c.targetTexture != null) targetName = c.targetTexture.name + " " + c.targetTexture.width + "x" + c.targetTexture.height;
                string parentName = "null";
                if (c.transform.parent != null) parentName = c.transform.parent.name;
                sb.AppendLine("[IMVR-TREE] CAM " + c.name + " depth=" + c.depth + " enabled=" + c.enabled + " cullingMask=" + c.cullingMask + " (" + LayerMaskToString(c.cullingMask) + ") clear=" + c.clearFlags + " targetTex=" + targetName + " pos=" + c.transform.position + " parent=" + parentName + " layer=" + c.gameObject.layer + " active=" + c.gameObject.activeInHierarchy);
                Component[] comps = c.gameObject.GetComponents<Component>();
                for (int k=0;k<comps.Length;k++) {
                    Component comp = comps[k];
                    sb.AppendLine("[IMVR-TREE]   comp: " + comp.GetType().Name);
                }
            }

            if (helmetInstance != null)
            {
                MonoBehaviour mb = helmetInstance as MonoBehaviour;
                if (mb != null)
                {
                    sb.AppendLine("[IMVR-TREE] --- Helmet Tree ---");
                    DumpTransform(mb.transform, sb, 0);
                    sb.AppendLine("[IMVR-TREE] --- Helmet Fields ---");
                    FieldInfo[] fields = helmetInstance.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    for (int fi=0; fi<fields.Length; fi++) {
                        FieldInfo f = fields[fi];
                        try {
                            object v = f.GetValue(helmetInstance);
                            if (v == null) sb.AppendLine("[IMVR-TREE] field " + f.Name + " = null");
                            else if (v is Camera) { Camera cam = (Camera)v; sb.AppendLine("[IMVR-TREE] field " + f.Name + " Camera " + cam.name + " depth=" + cam.depth + " mask=" + cam.cullingMask); }
                            else if (v is Transform) { Transform tr = (Transform)v; string pn = tr.parent != null ? tr.parent.name : "null"; sb.AppendLine("[IMVR-TREE] field " + f.Name + " Transform " + tr.name + " pos=" + tr.position + " parent=" + pn); }
                            else if (v is GameObject) { GameObject go = (GameObject)v; sb.AppendLine("[IMVR-TREE] field " + f.Name + " GO " + go.name + " layer=" + go.layer + " active=" + go.activeInHierarchy); }
                            else if (v is Component) { Component comp = (Component)v; sb.AppendLine("[IMVR-TREE] field " + f.Name + " Comp " + comp.GetType().Name + " on " + comp.gameObject.name); }
                            else sb.AppendLine("[IMVR-TREE] field " + f.Name + " = " + v.ToString());
                        } catch (Exception e) { sb.AppendLine("[IMVR-TREE] field " + f.Name + " err " + e.Message); }
                    }
                }
            }

            try {
                if (HUDCamera.Instance != null)
                {
                    sb.AppendLine("[IMVR-TREE] --- HUDCamera.Instance ---");
                    HUDCamera hc = HUDCamera.Instance;
                    DumpTransform(hc.transform, sb, 0);
                    sb.AppendLine("[IMVR-TREE] --- HUDCamera Fields ---");
                    FieldInfo[] hfields = typeof(HUDCamera).GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    for (int fi=0; fi<hfields.Length; fi++) {
                        FieldInfo f = hfields[fi];
                        try {
                            object v = f.GetValue(hc);
                            if (v == null) sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " = null");
                            else if (v is Camera) { Camera cam = (Camera)v; string tn = cam.targetTexture != null ? cam.targetTexture.name : "null"; sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " Camera " + cam.name + " depth=" + cam.depth + " mask=" + cam.cullingMask + " target=" + tn); }
                            else if (v is RenderTexture) { RenderTexture rt = (RenderTexture)v; sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " RT " + rt.name + " " + rt.width + "x" + rt.height); }
                            else if (v is Transform) { Transform tr = (Transform)v; sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " Transform " + tr.name); }
                            else if (v is GameObject) { GameObject go = (GameObject)v; sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " GO " + go.name + " layer=" + go.layer + " active=" + go.activeInHierarchy); }
                            else if (v is Component) { Component comp = (Component)v; sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " Comp " + comp.GetType().Name + " on " + comp.gameObject.name + " active=" + comp.gameObject.activeInHierarchy); }
                            else sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " = " + v.ToString());
                        } catch (Exception e) { sb.AppendLine("[IMVR-TREE] HUD field " + f.Name + " err " + e.Message); }
                    }
                    PropertyInfo[] props = typeof(HUDCamera).GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    for (int pi=0; pi<props.Length; pi++) {
                        PropertyInfo p = props[pi];
                        try { object v = p.GetValue(hc, null); if (v != null) sb.AppendLine("[IMVR-TREE] HUD prop " + p.Name + " = " + v.ToString()); } catch {}
                    }
                } else sb.AppendLine("[IMVR-TREE] HUDCamera.Instance = null");
            } catch (Exception e) { sb.AppendLine("[IMVR-TREE] HUDCamera dump err " + e.ToString()); }

            sb.AppendLine("[IMVR-TREE] --- Scene Roots ---");
            try {
                GameObject[] roots = UnityEngine.Object.FindObjectsOfType<GameObject>();
                for (int i=0;i<roots.Length;i++) {
                    GameObject go = roots[i];
                    if (go.transform.parent == null) sb.AppendLine("[IMVR-TREE] ROOT " + go.name + " layer=" + go.layer + " active=" + go.activeInHierarchy + " children=" + go.transform.childCount);
                }
            } catch {}

            sb.AppendLine("[IMVR-TREE] --- Search HUD/Canvas/LastImage ---");
            try {
                GameObject[] allGO = UnityEngine.Object.FindObjectsOfType<GameObject>();
                for (int i=0;i<allGO.Length;i++) {
                    GameObject go = allGO[i];
                    string n = go.name.ToLower();
                    if (n.Contains("hud") || n.Contains("lastimage") || n.Contains("canvas") || n.Contains("helmet") || n.Contains("visor") || n.Contains("neon") || n.Contains("glass")) {
                        string pn = go.transform.parent != null ? go.transform.parent.name : "null";
                        sb.AppendLine("[IMVR-TREE] FOUND " + go.name + " layer=" + go.layer + " active=" + go.activeInHierarchy + " parent=" + pn + " pos=" + go.transform.position + " localPos=" + go.transform.localPosition + " localRot=" + go.transform.localEulerAngles);
                        Component[] comps = go.GetComponents<Component>();
                        for (int k=0;k<comps.Length;k++) sb.AppendLine("[IMVR-TREE]   comp " + comps[k].GetType().Name);
                    }
                }
            } catch {}

            sb.AppendLine("[IMVR-TREE] ===== DUMP END =====");
            string outStr = sb.ToString();
            // PC test: dump to same directory as mod DLL, always
            try { lastDumpPath = GetDumpPath(); File.WriteAllText(lastDumpPath, outStr); VRManagerLogger.LogInfo("[IMVR-TREE] dump written to " + lastDumpPath); } catch (Exception fe) { VRManagerLogger.LogError("[IMVR-TREE] file write failed " + fe.Message); }
            int chunk = 4000;
            for (int i=0;i<outStr.Length;i+=chunk) {
                string part = outStr.Substring(i, Math.Min(chunk, outStr.Length-i));
                try { VRManagerLogger.LogInfo(part); } catch { VRManagerLogger.LogInfo(part); }
            }
        } catch (Exception e) { try { VRManagerLogger.LogError("[IMVR-TREE] dump failed " + e.ToString()); } catch { VRManagerLogger.LogError(e.Message); } }
    }

    static void DumpTransform(Transform t, StringBuilder sb, int depth)
    {
        if (t == null) return;
        string indent = new string(' ', depth*2);
        sb.AppendLine(indent + t.name + " layer=" + t.gameObject.layer + " active=" + t.gameObject.activeInHierarchy + " pos=" + t.localPosition + " rot=" + t.localEulerAngles + " scale=" + t.localScale);
        Component[] comps = t.gameObject.GetComponents<Component>();
        for (int i=0;i<comps.Length;i++) {
            Component comp = comps[i];
            if (comp == null) { sb.AppendLine(indent + "  comp null"); continue; }
            string extra = "";
            if (comp is Camera) { Camera c = (Camera)comp; string tn = c.targetTexture != null ? c.targetTexture.name : "null"; extra = " depth=" + c.depth + " mask=" + c.cullingMask + " target=" + tn; }
            if (comp is Renderer) { Renderer r = (Renderer)comp; string mn = r.material != null ? r.material.name : "null"; extra = " mat=" + mn + " enabled=" + r.enabled; }
            if (comp is Canvas) { Canvas cv = (Canvas)comp; string wn = cv.worldCamera != null ? cv.worldCamera.name : "null"; extra = " renderMode=" + cv.renderMode + " worldCam=" + wn; }
            sb.AppendLine(indent + "  comp " + comp.GetType().Name + extra);
        }
        for (int i=0;i<t.childCount;i++) DumpTransform(t.GetChild(i), sb, depth+1);
    }

    static string LayerMaskToString(int mask)
    {
        string[] names = new string[32];
        for (int i=0;i<32;i++) { string n = LayerMask.LayerToName(i); names[i] = string.IsNullOrEmpty(n) ? i.ToString() : n; }
        System.Collections.Generic.List<string> on = new System.Collections.Generic.List<string>();
        for (int i=0;i<32;i++) if ((mask & (1<<i)) != 0) on.Add(names[i]);
        return string.Join(",", on.ToArray());
    }
}


// F9 runner - always dumps, bypasses verboseDebug. DontDestroyOnLoad so it survives level loads.
public class HUDTreeDumpRunner : MonoBehaviour
{
    public static object lastHelmet;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            HUDTreeDumper.DumpAll(lastHelmet);
            VRManagerLogger.LogInfo("[IMVR-TREE] F9 dump written to " + HUDTreeDumper.lastDumpPath);
        }
    }
}

[HarmonyPatch]
class Helmet_TreeDump_Patch
{
    static MethodBase TargetMethod()
    {
        Type t = AccessTools.TypeByName("Helmet");
        if (t == null) return null;
        return t.GetMethod("InitFirstPersonCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    }
    static void Postfix(object __instance)
    {
        // F9 setup - always, not gated
        HUDTreeDumpRunner.lastHelmet = __instance;
        try {
            GameObject runnerGO = GameObject.Find("HUDTreeDumpRunner");
            if (runnerGO == null) {
                runnerGO = new GameObject("HUDTreeDumpRunner");
                runnerGO.AddComponent<HUDTreeDumpRunner>();
                GameObject.DontDestroyOnLoad(runnerGO);
            }
        } catch {}
        // Non-F9 auto dump - keep verboseDebug gate
        if (VRManagerState.verboseDebug)
        {
            try
            {
                MonoBehaviour mb = __instance as MonoBehaviour;
                if (mb != null)
                {
                    mb.StartCoroutine(DumpAfterFrame(__instance));
                }
                else
                {
                    HUDTreeDumper.DumpAll(__instance);
                }
            }
            catch { HUDTreeDumper.DumpAll(__instance); }
        }
    }
    static System.Collections.IEnumerator DumpAfterFrame(object inst)
    {
        yield return null;
        yield return null;
        if (VRManagerState.verboseDebug)
        {
            HUDTreeDumper.DumpAll(inst);
        }
    }
}
