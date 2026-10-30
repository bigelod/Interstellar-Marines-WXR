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

    // Resolved once in Awake, then re-read every 2s by PollConfig so values can be
    // tuned with the game running - editing files on a headset is painful enough
    // without a restart per attempt.
    public static string configPath = null;
    static float lastConfigPoll = -999f;

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

        ResolveConfigPath();
        ParseConfig();
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

    void ResolveConfigPath()
    {
        try{
            string basePath = Paths.PluginPath;
            string path = Path.Combine(basePath, "IMVR/config.txt");
            if(!File.Exists(path)) path = Path.Combine(basePath, "config.txt");
            if(!File.Exists(path)){
                string dir = Path.GetDirectoryName(Info.Location);
                path = Path.Combine(dir, "config.txt");
            }
            if(File.Exists(path)) configPath = path;
        } catch(Exception e){
            VRManagerLogger.LogError("[IMVR] Config path error: "+e);
        }
    }

    // Called every frame from VRInputRunner, rate-limited to 2s. Cheap, and it means
    // screen_fov and the rest can be dialled in live without a rebuild or a restart.
    public static void PollConfig()
    {
        if(Time.realtimeSinceStartup - lastConfigPoll < 2f) return;
        lastConfigPoll = Time.realtimeSinceStartup;
        ParseConfig();
    }

    public static void ParseConfig()
    {
        try{
            if(configPath == null || !File.Exists(configPath)) return;
            string[] lines = File.ReadAllLines(configPath);
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
                // How wide an angle the runtime should paint this frame across. This does
                // NOT have to match what the camera rendered: declaring less than the true
                // FOV shows the SAME world, shrunk toward the centre of the view, at full
                // resolution. That is the cheap way to fit everything inside the part of
                // the panel a headset actually shows - no viewport rect, no black borders,
                // no lost pixels, and the sync pixel does not move.
                if(k=="screen_fov" || k=="screenfov" || k=="vr_fov" || k=="fov"){
                    float f; if(float.TryParse(v, out f) && f > 1f && f <= 180f) XRAPI.SetDeclaredFov(f);
                }
            }
            VRInput.SetConfig(deadzone);
        } catch(Exception e){
            VRManagerLogger.LogError("[IMVR] Config error: "+e);
        }
    }

    void OnApplicationQuit() { XRAPI.Shutdown(); }
}
