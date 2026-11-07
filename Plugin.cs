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
    // ON is the performance-safe default and it is where this landed: culling is what
    // skips the draw calls, so 0 costs frames rather than saving them.
    //
    // It was defaulted OFF on 3 Nov 2026 because this game's culling switches lights off
    // rather than merely skipping draws (ToggleOcclusion.OnBecameInvisible), and 0 cured
    // the blackouts on device. The blackouts turned out to have a second cause that is now
    // fixed, and the owner confirmed on 3 Nov that 1 behaves - so it is back ON, with
    // VRClipGuard still forcing it off per frame for as long as the camera is detected
    // inside geometry, which is the only time culling can strand the player in the dark.
    public static bool occlusionCulling = true;
    // View distance. 80 was hardcoded in the near-clip patch before it became tunable, then
    // 60; 45 is the owner's call on 3 Nov - this is a horror game and almost everything
    // that matters is within arm's reach of a corridor.
    // Note this is not purely a draw-cost knob: ToggleOcclusion disables the lights in
    // anything that frustum-culls out, so lowering it also unlights distant rooms until
    // the player gets closer. That is why the fog is tied to it - see VRVisualTweaks.
    // ApplyFog, which walls the view off in fog just short of the far plane so geometry
    // fades out instead of popping in at the clip.
    public static float farClip = 45f;
    // Scale applied to HelmetOccluder. 1.0 = untouched. See the note in Patches.cs: it is
    // an invisible shadow-caster on layer 14, which only HelmetCamera draws, so this
    // cannot affect the world view - it is here because the owner asked to try 2x.
    public static float occluderScale = 1f;
    // The peripheral damage bands that replace the game's full-screen flash in VR.
    // painAlpha is their peak opacity, painBarWidth their peak width as a fraction of the
    // VISIBLE width, and painBarInset how much of the panel WinlatorXR actually shows -
    // keep it in step with hudScale, since that is the same boundary. See VRPainOverlay.cs.
    public static float painAlpha = 0.55f;
    public static float painBarWidth = 0.15f;
    public static float painBarInset = 1.00f;

    // Supersampling factor for the visor HUD's own render target, written straight into
    // HUDCamera.hudScale. That field is NOT a screen-size knob despite the name: the game
    // sizes cleanToBendTarget / bendToDoubleGlassTarget at Screen size * hudScale AND sets
    // mainCanvasScaler.referenceResolution to 3840x2160 / hudScale, so raising it renders
    // the same HUD, at the same apparent size, into proportionally more pixels. That is
    // the one lever that makes the HUD text crisper without disturbing its fit.
    // 1.1 is the game's own default; the mod used to force 1.0, which was slightly WORSE
    // than stock. 2.0 doubles the linear resolution of the glyphs. The cost is two
    // render targets at 2x and two full-screen blits over 4x the pixels, so this is the
    // first thing to wind back if the frame rate is short.
    public static float hudSupersample = 2.0f;
    // Edge length of the red sync patch, in framebuffer pixels. XRAPI_V0_4_DESC calls for
    // 10x10 at the top-left; it sat at 150 for a long while on the theory that a letterboxed
    // panel might move the corner, and on device that turned out to be an enormous red
    // square in the player's view for no gain. Config-exposed only so it can be raised again
    // without a rebuild if the runtime ever stops reading it.
    public static int syncPixelSize = 10;
    // Render resolution, applied once at startup. 0 in either field leaves the window
    // alone, which is the escape hatch: Docs/MetaAI records an earlier SetResolution
    // attempt breaking the HUD (FIXED23-25), though that attempt also resized render
    // targets by hand, which this does not - HUDCamera sizes its own off Screen.
    // 1280x960 is the owner's balance point between legibility and frame rate.
    public static int renderWidth = 1280;
    public static int renderHeight = 960;
    // Shadows. Only the player's helmet lamp casts any (VRLightTamer), and it casts them
    // at this distance and resolution. 0=Low, 1=Medium, 2=High, 3=VeryHigh.
    public static float shadowDistance = 10f;
    public static int shadowResolution = 0;

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

        // First line in the log, every session: which binary is actually loaded.
        // Docs/general_wxr/CLAUDE.md asks for a build stamp on line 1 for exactly the
        // reason it was needed on 3 Nov - a stale Debug DLL went to the headset and the
        // only way to tell was that the build "did not contain" a change that was in it.
        LogBuildStamp();

        ResolveConfigPath();
        ParseConfig();
        ApplyRenderResolution();
        XRAPI.XR_Init();

        Harmony harmony = new Harmony("im.vr.winlatorxr");
        harmony.PatchAll();

        SyncPixelQuad.Ensure();

        GameObject runner = new GameObject("IMVR_InputRunner");
        DontDestroyOnLoad(runner);
        runner.AddComponent<VRInputRunner>();
        runner.AddComponent<VRTuneMenu>();
        
        Logger.LogInfo("[IMVR] Interstellar Marines VR - UDP 7872 loaded, dominant=" + configDominant + " flipHands=" + flipHands.ToString());
    }

    // Config is read from the assembly rather than from a #if, so the line cannot
    // disagree with the binary it is printed from.
    void LogBuildStamp()
    {
        string cfg = "unknown";
        string built = "unknown";
        try
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            object[] attrs = asm.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false);
            bool jitOptimizerDisabled = false;
            if (attrs != null && attrs.Length > 0)
            {
                System.Diagnostics.DebuggableAttribute d = attrs[0] as System.Diagnostics.DebuggableAttribute;
                if (d != null) jitOptimizerDisabled = d.IsJITOptimizerDisabled;
            }
            cfg = jitOptimizerDisabled ? "DEBUG (unoptimised - do not ship this to the headset)" : "RELEASE";
            built = File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch { }
        Logger.LogInfo("[IMVR] build " + cfg + ", compiled " + built);
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

    // Applied at startup and again whenever the tuning menu changes it - deliberately NOT
    // from PollConfig, because SetResolution reallocates the backbuffer and the HUD's two
    // render targets, which is not something to do every two seconds.
    //
    // Nothing else in the mod touches the window. Lower is a real frame-rate win here: the
    // frame is captured, encoded, shipped to the runtime and repainted across the panel, so
    // every pixel is paid for more than once. The sync pixel is placed in framebuffer
    // coordinates (SyncPixelQuad) and the pain bands as a fraction of the frame, so both
    // follow the new size without any further work.
    public static void ApplyRenderResolution()
    {
        try
        {
            if (renderWidth <= 0 || renderHeight <= 0) return;
            if (Screen.width == renderWidth && Screen.height == renderHeight) return;
            VRManagerLogger.LogInfo("[IMVR] Render resolution -> " + renderWidth + "x" + renderHeight
                + " (was " + Screen.width + "x" + Screen.height + ")");
            Screen.SetResolution(renderWidth, renderHeight, Screen.fullScreen);
        }
        catch (Exception e)
        {
            VRManagerLogger.LogWarn("[IMVR] SetResolution failed: " + e.Message);
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
                if(k=="pain_alpha" || k=="painalpha"){ float f; if(float.TryParse(v, out f) && f >= 0f && f <= 1f) painAlpha = f; }
                if(k=="pain_bar_width" || k=="painbarwidth"){ float f; if(float.TryParse(v, out f) && f >= 0f && f <= 0.5f) painBarWidth = f; }
                if(k=="pain_bar_inset" || k=="painbarinset"){ float f; if(float.TryParse(v, out f) && f >= 0.2f && f <= 1f) painBarInset = f; }
                if(k=="occluder_scale" || k=="occluderscale"){ float f; if(float.TryParse(v, out f) && f >= 0.1f && f <= 10f) occluderScale = f; }
                if(k=="hud_supersample" || k=="hudsupersample"){ float f; if(float.TryParse(v, out f) && f >= 0.5f && f <= 4f) hudSupersample = f; }
                if(k=="sync_pixel_size" || k=="syncpixelsize"){ int n; if(int.TryParse(v, out n) && n >= 2 && n <= 300) syncPixelSize = n; }
                if(k=="render_width" || k=="renderwidth"){ int n; if(int.TryParse(v, out n) && (n == 0 || (n >= 320 && n <= 4096))) renderWidth = n; }
                if(k=="render_height" || k=="renderheight"){ int n; if(int.TryParse(v, out n) && (n == 0 || (n >= 240 && n <= 4096))) renderHeight = n; }
                if(k=="shadow_distance" || k=="shadowdistance"){ float f; if(float.TryParse(v, out f) && f >= 0f && f <= 200f) shadowDistance = f; }
                if(k=="shadow_resolution" || k=="shadowresolution"){ int n; if(int.TryParse(v, out n) && n >= 0 && n <= 3) shadowResolution = n; }
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
