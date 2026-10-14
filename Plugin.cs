using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("im.vr.winlatorxr", "IMVR - Interstellar Marines WinlatorXR Monocular", "0.3.0")]
public class VRManager : BaseUnityPlugin
{
    public static string configDominant = "right";
    public static int configFlipHands = 0;
    public static float turnSpeed = 45f;
    public static float deadzone = 0.2f;

    void Awake()
    {
        LoadConfig();
        XRAPI.XR_Init();

        var harmony = new Harmony("im.vr.winlatorxr");
        harmony.PatchAll();

        var runner = new GameObject("IMVR_InputRunner");
        DontDestroyOnLoad(runner);
        runner.AddComponent<VRInputRunner>();

        Logger.LogInfo("[IMVR] Interstellar Marines VR - SixDOFinator UDP 7872 loaded, dominant=" + configDominant + ", flipHands=" + configFlipHands.ToString());
    }

    void OnDestroy() { XRAPI.Shutdown(); }

    void LoadConfig()
    {
        try
        {
            string path = Path.Combine(Paths.PluginPath, "IMVR/config.txt");
            if (!File.Exists(path)) path = Path.Combine(Paths.PluginPath, "config.txt");
            if (!File.Exists(path)) path = Path.Combine(Path.GetDirectoryName(Info.Location), "config.txt");
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                var parts = t.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;
                var k = parts[0].Trim().ToLower();
                var v = parts[1].Trim().ToLower();
                if (k == "dominant_hand" || k == "dominant") configDominant = v;
                if (k == "turn_speed") float.TryParse(v, out turnSpeed);
                if (k == "deadzone") float.TryParse(v, out deadzone);
                if (k == "flip_hands") int.TryParse(v, out configFlipHands);
            }
            VRInput.SetConfig(deadzone);
        }
        catch (Exception e)
        {
            Debug.LogError("[IMVR] Config error: " + e);
        }
    }
}
