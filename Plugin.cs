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

    // View comfort, all live-tunable. Defaults raised from the original 0.08 because the
    // 1 Nov test showed the Marine's own body blocking the camera.
    public static float eyeHeight = 0.16f;   // metres above the helmet's own camera mount
    public static float eyeForward = 0.05f;  // metres forward, clear of the chest
    public static float handHeight = 0.05f;  // raises the weapon with the eye
    // Low enough that a holographic sight can be brought to the eye without being cut
    // off. The rest of the weapon clipping is acceptable; the sight is not.
    public static float nearClip = 0.05f;
    // Helmet lamp placement, relative to the VR camera. Above the middle of the view
    // reads as helmet-mounted rather than hand-held.
    public static float lightHeight = 0.10f;
    public static float lightForward = 0.10f;
    // Unity's occlusion culling on the player camera, while the camera is in open space.
    // Leaving it ON is the performance-safe default. VRClipGuard overrides it to off for
    // as long as the camera is inside geometry, which is the only time it causes the
    // pitch-black screen - so there is no longer any reason to disable it globally.
    public static bool occlusionCulling = true;
    // View distance. farClip is the normal one (80 was hardcoded in the near-clip patch
    // before it became tunable). insideFarClip is what VRClipGuard collapses it to while
    // the camera is inside geometry: occlusion culling has to be off there or the world
    // disappears, and a short view distance is what keeps that affordable - frustum
    // culling still runs, so nothing beyond it is submitted at all.
    public static float farClip = 80f;
    public static float insideFarClip = 8f;
    // Scale applied to HelmetOccluder. 1.0 = untouched. See the note in Patches.cs: it is
    // an invisible shadow-caster on layer 14, which only HelmetCamera draws, so this
    // cannot affect the world view - it is here because the owner asked to try 2x.
    public static float occluderScale = 1f;

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
        runner.AddComponent<VRTuneMenu>();
        
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
                if(k=="eye_height" || k=="eyeheight"){ float f; if(float.TryParse(v, out f) && f >= -0.5f && f <= 1.0f) eyeHeight = f; }
                if(k=="eye_forward" || k=="eyeforward"){ float f; if(float.TryParse(v, out f) && f >= -0.5f && f <= 1.0f) eyeForward = f; }
                if(k=="hand_height" || k=="handheight"){ float f; if(float.TryParse(v, out f) && f >= -0.5f && f <= 1.0f) handHeight = f; }
                if(k=="light_height" || k=="lightheight"){ float f; if(float.TryParse(v, out f) && f >= -0.5f && f <= 1.0f) lightHeight = f; }
                if(k=="light_forward" || k=="lightforward"){ float f; if(float.TryParse(v, out f) && f >= -0.5f && f <= 1.0f) lightForward = f; }
                if(k=="occlusion_culling" || k=="occlusionculling"){ occlusionCulling = !(v=="false" || v=="0" || v=="no"); }
                if(k=="near_clip" || k=="nearclip"){ float f; if(float.TryParse(v, out f) && f > 0.001f && f <= 1.0f) nearClip = f; }
                if(k=="far_clip" || k=="farclip"){ float f; if(float.TryParse(v, out f) && f >= 5f && f <= 2000f) farClip = f; }
                if(k=="inside_far_clip" || k=="insidefarclip"){ float f; if(float.TryParse(v, out f) && f >= 1f && f <= 500f) insideFarClip = f; }
                if(k=="occluder_scale" || k=="occluderscale"){ float f; if(float.TryParse(v, out f) && f >= 0.1f && f <= 10f) occluderScale = f; }
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
