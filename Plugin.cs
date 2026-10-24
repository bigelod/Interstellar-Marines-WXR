using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

public static class VRManagerState
{
    public static bool inVR = false;
    public static bool isSetup = false;
    public static int rxTxFailCount = 0;
    public static int rxTxFailMaxRetry = 5;
    public static bool verboseDebug = false;

    public static GameObject VRRoot;
}

public static class VRManagerLogger
{
    public static Action<string> LogDebug;
    public static Action<string> LogInfo;
    public static Action<string> LogWarn;
    public static Action<string> LogError;
}

[BepInPlugin("im.vr.winlatorxr", "IMVR - Interstellar Marines WinlatorXR", "0.3.2")]
public class VRManager : BaseUnityPlugin
{
    public static string configDominant = "right";
    public static float turnSpeed = 45f;
    public static float deadzone = 0.2f;
    public static bool flipHands = false;

    void Awake()
    {
        // Unity 4 compat - BepInEx thinks isBatchMode exists
        try
        {
            var prop = typeof(UnityEngine.Application).GetProperty("isBatchMode");
            if (prop == null)
            {
                Debug.Log("[IMVR] Patching missing isBatchMode check - ignoring");
                // Tell BepInEx chainloader to skip batchmode check by setting env var
                System.Environment.SetEnvironmentVariable("BEPINEX_BATCH_CHECK", "0");
            }
        }
        catch { }

        VRManagerLogger.LogInfo = LogDebug;
        VRManagerLogger.LogWarn = LogWarn;
        VRManagerLogger.LogError = LogError;
        VRManagerLogger.LogInfo = LogInfo;

        LoadConfig();
        XRAPI.XR_Init();

        Harmony harmony = new Harmony("im.vr.winlatorxr");
        harmony.PatchAll();

        GameObject runner = new GameObject("IMVR_InputRunner");
        DontDestroyOnLoad(runner);
        runner.AddComponent<VRInputRunner>();
        
        Logger.LogInfo("[IMVR] Interstellar Marines VR - UDP 7872 loaded, dominant=" + configDominant + " flipHands=" + flipHands.ToString());
    }

    void LogWarn(string msg)
    {
        Logger.LogWarning(msg);
    }

    void LogError(string msg)
    {
        Logger.LogError(msg);
    }

    void LogInfo(string msg)
    {
        Logger.LogInfo(msg);
    }

    void LogDebug(string msg)
    {
        Logger.LogDebug(msg);
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
            VRManagerLogger.LogError("[IMVR] Config error: "+e);
        }
    }

    void OnApplicationQuit() { XRAPI.Shutdown(); }
}
