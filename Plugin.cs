using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("im.vr.winlatorxr", "IMVR - Interstellar Marines WinlatorXR", "0.3.2")]
public class VRManager : BaseUnityPlugin
{
    public static string configDominant = "right";
    public static float turnSpeed = 45f;
    public static float deadzone = 0.2f;
    public static bool flipHands = false;

    void Awake()
    {
        LoadConfig();
        XRAPI.XR_Init();

        Harmony harmony = new Harmony("im.vr.winlatorxr");
        harmony.PatchAll();

        GameObject runner = new GameObject("IMVR_InputRunner");
        DontDestroyOnLoad(runner);
        runner.AddComponent<VRInputRunner>();

        Logger.LogInfo("[IMVR] Interstellar Marines VR - UDP 7872 loaded, dominant=" + configDominant + " flipHands=" + flipHands.ToString());
    }

    void OnDestroy(){ XRAPI.Shutdown(); }

    void LoadConfig()
    {
        try{
            string basePath = Paths.PluginPath;
            string path = Path.Combine(basePath, "IMVR/config.txt");
            if(!File.Exists(path)) path = Path.Combine(basePath, "config.txt");
            if(!File.Exists(path)){
                string dir = Path.GetDirectoryName(Info.Location);
                path = Path.Combine(dir, "config.txt");
            }
            if(!File.Exists(path)) return;
            string[] lines = File.ReadAllLines(path);
            foreach(string line in lines){
                string t = line.Trim();
                if(t.StartsWith("#") || t.Length==0) continue;
                string[] parts = t.Split(new char[]{'='},2);
                if(parts.Length!=2) continue;
                string k = parts[0].Trim().ToLower();
                string v = parts[1].Trim().ToLower();
                if(k=="dominant_hand" || k=="dominant") configDominant = v;
                if(k=="turn_speed"){ float f; if(float.TryParse(v, out f)) turnSpeed=f; }
                if(k=="deadzone"){ float f; if(float.TryParse(v, out f)) deadzone=f; }
                if(k=="fliphand" || k=="fliphands" || k=="flip_hands" || k=="fliphands") flipHands = (v=="true" || v=="1" || v=="yes");
            }
            VRInput.SetConfig(deadzone);
        } catch(Exception e){
            Debug.LogError("[IMVR] Config error: "+e);
        }
    }
}
