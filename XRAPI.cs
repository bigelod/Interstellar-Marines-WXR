using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using UnityEngine;
using System.Globalization;
using System.IO;

public static class XRAPI
{
    public const int LEFT = 0, RIGHT = 1;
    public const uint BTN_A = 1u << 0, BTN_B = 1u << 1, BTN_X = 1u << 2, BTN_Y = 1u << 3, BTN_THUMB = 1u << 4, BTN_MENU = 1u << 5, BTN_GRIP = 1u << 6;

    static UdpClient client;
    static UdpClient sendClient;

    static Thread recvThread;
    
    static object lockObj = new object();

    public struct State
    {
        public Vector3 headPos; public Quaternion headRot;
        public Vector3 leftPos; public Quaternion leftRot; public Vector2 leftStick;
        public Vector3 rightPos; public Quaternion rightRot; public Vector2 rightStick;
        public float ipd, fovH, fovV; public int frameId;
        public bool lGrip, lMenu, lThumbPress, lThumbLeft, lThumbRight, lThumbUp, lThumbDown, lTrigger, lX, lY;
        public bool rA, rB, rGrip, rThumbPress, rThumbLeft, rThumbRight, rThumbUp, rThumbDown, rTrigger;
        public string hmdType;
        public double lastUpdate;
    }
    static State latest;
    static bool hasData = false;
    static int[] PORTS = new int[] { 7872, 7873 };

    // Sending to WinlatorXR: 127.0.0.1:7278 format: L_HAPTICS R_HAPTICS MODE_VR MODE_3D FOVX FOVY
    static IPEndPoint sendEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7278);
    static int leftHapticFrames = 0;
    static int rightHapticFrames = 0;
    static int modeVR = 1; // 1 to enable VR, 2 for 2d non-vr
    static int mode3D = 0; // 0 mono for IM, 1 SBS, 2 AER
    static float targetFovX = 104.5f;
    static float targetFovY = 104.5f;


    public static void XR_Init()
    {
        if (VRManagerLogger.isSetupAlready) ;

        string dataDirectory = "Z:/tmp/xr";

        if (!Directory.Exists(dataDirectory))
        {
            if (Directory.Exists("Z:/tmp"))
            {
                Directory.CreateDirectory(dataDirectory);
            }
        }

        if (Directory.Exists(dataDirectory))
        {
            string versionFile = dataDirectory + "/version";

            if (!File.Exists(versionFile))
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(versionFile))
                    {
                        sw.WriteLine("0.4");
                    }
                }
                catch
                {

                }
            }
        }

        foreach (int port in PORTS)
        {
            try
            {
                client = new UdpClient(port);
                client.Client.ReceiveTimeout = 500;
                recvThread = new Thread(RecvLoopParam);
                recvThread.IsBackground = true;
                recvThread.Start(port);
                VRManagerLogger.LogInfo("[IMVR] UDP listening on " + port);
                latest.headRot = Quaternion.identity;
                latest.leftRot = Quaternion.identity;
                latest.rightRot = Quaternion.identity;
                break;
            }
            catch (Exception e)
            {
                VRManagerLogger.LogWarn("[IMVR] Port " + port + " busy: " + e.Message);
                try { if (client != null) client.Close(); } catch { }
            }
        }

        // Still start sender even if receiver failed
        try
        {
            sendClient = new UdpClient();
            SendFirstFrame();
        }
        catch { }

        VRManagerLogger.isSetupAlready = true;
    }

    static void RecvLoopParam(object obj)
    {
        int port = (int)obj;
        RecvLoop(port);
    }

    static void RecvLoop(int port)
    {
        IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                byte[] data = client.Receive(ref any);
                if (data == null || data.Length == 0) continue;
                string s = Encoding.ASCII.GetString(data).Trim();
                ParseString(s);
            }
            catch (SocketException)
            {
                VRManagerLogger.LogWarn("[IMVR] No UDP port, checking D:/xrtemp fallback");
                TryFileFallback();
            }
            catch (Exception e)
            {
                VRManagerLogger.LogWarn("[IMVR] UDP error: " + e.Message);
            }
        }
    }

    public static void SendFirstFrame()
    {
        //try
        //{
        //    modeVR = 1;
        //    mode3D = 0; // mono for Interstellar Marines
        //    leftHapticFrames = 0;
        //    rightHapticFrames = 0;
        //    string msg = string.Format(CultureInfo.InvariantCulture, "0 0 1 0 104.5 104.5");
        //    byte[] bytes = Encoding.ASCII.GetBytes(msg);
        //    if (sendClient != null)
        //    {
        //        sendClient.Send(bytes, bytes.Length, sendEndpoint);
        //        VRManagerLogger.LogInfo("[IMVR] Sent first frame to awaken WinlatorXR: " + msg);
        //    }
        //}
        //catch (Exception e)
        //{
        //    VRManagerLogger.LogWarn("[IMVR] SendFirstFrame failed: " + e.Message);
        //}
    }

    public static void SendHapticVibration(int hand, int frames)
    {
        SendHapticVibration(hand, frames, 1f);
    }

    public static void SendHapticVibration(int hand, int frames, float amplitude)
    {
        int f = Mathf.Clamp(frames, 0, 100);
        if (hand == LEFT) leftHapticFrames = Mathf.Max(leftHapticFrames, f);
        else rightHapticFrames = Mathf.Max(rightHapticFrames, f);

        SendHapticUpdate();
    }

    public static void SendHapticBoth(int leftFrames, int rightFrames)
    {
        leftHapticFrames = Mathf.Max(leftHapticFrames, leftFrames);
        rightHapticFrames = Mathf.Max(rightHapticFrames, rightFrames);

        SendHapticUpdate();
    }

    public static void SendHapticUpdate()
    {
        string msg = string.Format(CultureInfo.InvariantCulture, "0 0 " + modeVR.ToString() + " " + mode3D.ToString() + " 104.5 104.5");
        byte[] bytes = Encoding.ASCII.GetBytes(msg);
        if (sendClient != null)
        {
            sendClient.Send(bytes, bytes.Length, sendEndpoint);
        }
    }


    static void TryFileFallback()
    {
        try
        {
            string[] dirs = new string[] { "D:/xrtemp", "C:/xrtemp", "./xrtemp" };
            foreach (string dir in dirs)
            {
                if (!System.IO.Directory.Exists(dir)) continue;
                string[] files = System.IO.Directory.GetFiles(dir);
                if (files.Length == 0) continue;
                string name = System.IO.Path.GetFileName(files[0]);
                ParseString(name);
                return;
            }
        }
        catch { }
    }

    static void ParseString(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        string[] parts = s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 30) return;
        int idx = 0;
        if (parts[0].StartsWith("client")) idx = 1;
        try
        {
            Quaternion lQuat = new Quaternion(ParseFloat(parts, idx), ParseFloat(parts, idx + 1), ParseFloat(parts, idx + 2), ParseFloat(parts, idx + 3)); idx += 4;
            Vector2 lStick = new Vector2(ParseFloat(parts, idx), ParseFloat(parts, idx + 1)); idx += 2;
            Vector3 lPos = new Vector3(ParseFloat(parts, idx), ParseFloat(parts, idx + 1), ParseFloat(parts, idx + 2)); idx += 3;
            Quaternion rQuat = new Quaternion(ParseFloat(parts, idx), ParseFloat(parts, idx + 1), ParseFloat(parts, idx + 2), ParseFloat(parts, idx + 3)); idx += 4;
            Vector2 rStick = new Vector2(ParseFloat(parts, idx), ParseFloat(parts, idx + 1)); idx += 2;
            Vector3 rPos = new Vector3(ParseFloat(parts, idx), ParseFloat(parts, idx + 1), ParseFloat(parts, idx + 2)); idx += 3;
            Quaternion hQuat = new Quaternion(ParseFloat(parts, idx), ParseFloat(parts, idx + 1), ParseFloat(parts, idx + 2), ParseFloat(parts, idx + 3)); idx += 4;
            Vector3 hPos = new Vector3(ParseFloat(parts, idx), ParseFloat(parts, idx + 1), ParseFloat(parts, idx + 2)); idx += 3;
            float ipd = ParseFloat(parts, idx); idx++;
            float fovH = ParseFloat(parts, idx); idx++;
            float fovV = ParseFloat(parts, idx); idx++;
            int frameId = 0; int.TryParse(parts[idx], out frameId); idx++;
            string boolStr = "";
            string hmdType = "META";
            if (idx < parts.Length) { boolStr = parts[idx]; idx++; if (idx < parts.Length) hmdType = parts[idx]; }

            bool lGrip = false, lMenu = false, lThumbPress = false, lThumbLeft = false, lThumbRight = false, lThumbUp = false, lThumbDown = false, lTrigger = false, lX = false, lY = false;
            bool rA = false, rB = false, rGrip = false, rThumbPress = false, rThumbLeft = false, rThumbRight = false, rThumbUp = false, rThumbDown = false, rTrigger = false;
            if (boolStr.Length >= 19)
            {
                lGrip = boolStr[0] == 'T'; lMenu = boolStr[1] == 'T'; lThumbPress = boolStr[2] == 'T'; lThumbLeft = boolStr[3] == 'T'; lThumbRight = boolStr[4] == 'T';
                lThumbUp = boolStr[5] == 'T'; lThumbDown = boolStr[6] == 'T'; lTrigger = boolStr[7] == 'T'; lX = boolStr[8] == 'T'; lY = boolStr[9] == 'T';
                rA = boolStr[10] == 'T'; rB = boolStr[11] == 'T'; rGrip = boolStr[12] == 'T'; rThumbPress = boolStr[13] == 'T'; rThumbLeft = boolStr[14] == 'T';
                rThumbRight = boolStr[15] == 'T'; rThumbUp = boolStr[16] == 'T'; rThumbDown = boolStr[17] == 'T'; rTrigger = boolStr[18] == 'T';
            }
            lock (lockObj)
            {
                latest.leftRot = lQuat; latest.leftPos = lPos; latest.leftStick = lStick;
                latest.rightRot = rQuat; latest.rightPos = rPos; latest.rightStick = rStick;
                latest.headRot = hQuat; latest.headPos = hPos;
                latest.ipd = ipd; latest.fovH = fovH; latest.fovV = fovV; latest.frameId = frameId;
                latest.lGrip = lGrip; latest.lMenu = lMenu; latest.lThumbPress = lThumbPress; latest.lThumbLeft = lThumbLeft; latest.lThumbRight = lThumbRight;
                latest.lThumbUp = lThumbUp; latest.lThumbDown = lThumbDown; latest.lTrigger = lTrigger; latest.lX = lX; latest.lY = lY;
                latest.rA = rA; latest.rB = rB; latest.rGrip = rGrip; latest.rThumbPress = rThumbPress; latest.rThumbLeft = rThumbLeft;
                latest.rThumbRight = rThumbRight; latest.rThumbUp = rThumbUp; latest.rThumbDown = rThumbDown; latest.rTrigger = rTrigger;
                latest.hmdType = hmdType; latest.lastUpdate = Time.realtimeSinceStartup; hasData = true;
            }
        }
        catch (Exception e)
        {
            VRManagerLogger.LogWarn("[IMVR] Parse fail: " + e.Message);
        }
    }

    static float ParseFloat(string[] parts, int i)
    {
        return float.Parse(parts[i], CultureInfo.InvariantCulture);
    }

    public static bool XR_GetHeadPose(out Vector3 pos, out Quaternion rot)
    {
        lock (lockObj)
        {
            if (hasData) { pos = latest.headPos; rot = latest.headRot; return true; }
        }
        pos = Vector3.zero; rot = Quaternion.identity;
        rot = Quaternion.Euler(0, Input.GetAxis("Mouse X") * 2f, 0);
        return false;
    }
    public static bool XR_GetControllerPose(int hand, out Vector3 pos, out Quaternion rot)
    {
        lock (lockObj)
        {
            if (hasData)
            {
                if (hand == LEFT) { pos = latest.leftPos; rot = latest.leftRot; return true; }
                else { pos = latest.rightPos; rot = latest.rightRot; return true; }
            }
        }
        pos = Vector3.zero; rot = Quaternion.identity;
        Camera cam = Camera.main;
        if (cam != null)
        {
            pos = cam.transform.position + cam.transform.forward * 0.45f + cam.transform.right * (hand == RIGHT ? 0.2f : -0.2f);
            rot = cam.transform.rotation;
        }
        return false;
    }
    public static bool XR_GetControllerState(int hand, out uint buttons, out Vector2 stick, out float trigger, out float grip)
    {
        buttons = 0; stick = Vector2.zero; trigger = 0; grip = 0;
        lock (lockObj)
        {
            if (hasData)
            {
                if (hand == LEFT)
                {
                    stick = latest.leftStick; trigger = latest.lTrigger ? 1f : 0f; grip = latest.lGrip ? 1f : 0f;
                    if (latest.lX) buttons |= BTN_X; if (latest.lY) buttons |= BTN_Y;
                    if (latest.lThumbPress) buttons |= BTN_THUMB; if (latest.lMenu) buttons |= BTN_MENU; if (latest.lGrip) buttons |= BTN_GRIP;
                    return true;
                }
                else
                {
                    stick = latest.rightStick; trigger = latest.rTrigger ? 1f : 0f; grip = latest.rGrip ? 1f : 0f;
                    if (latest.rA) buttons |= BTN_A; if (latest.rB) buttons |= BTN_B;
                    if (latest.rThumbPress) buttons |= BTN_THUMB; if (latest.rGrip) buttons |= BTN_GRIP;
                    return true;
                }
            }
        }
        stick = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        trigger = Input.GetMouseButton(0) ? 1f : 0f; grip = Input.GetMouseButton(1) ? 1f : 0f;
        return false;
    }
    public static bool GetBoolState(out State s) { lock (lockObj) { s = latest; return hasData; } }

    public static void ForceVRMode()
    {
        VRManagerLogger.isVRTime = true;

        //We know we're in VR for sure now
        modeVR = 1;
        mode3D = 0;
        VRManagerLogger.LogInfo("[IMVR] Switching to VR mode (MODE_VR=1) - in-game");

        SendHapticUpdate();
    }

    public static void UpdateVRModeFromGameState()
    {
        if (VRManagerLogger.isVRTime)
        {
            SendHapticUpdate();

            return;
        }

        try
        {
            bool isInGame = false;
            bool isPaused = false;

            // Check if local character exists (in-game)
            //if(Character.AllCharacters!=null){
            //    foreach(Character c in Character.AllCharacters){
            //        if(c!=null && c.IsLocallyControlled){
            //            isInGame = true;
            //            break;
            //        }
            //    }
            //}

            //// Try to detect pause via reflection (PauseManager, GameManager, etc)
            //try{
            //    var pauseType = HarmonyLib.AccessTools.TypeByName("PauseManager");
            //    if(pauseType!=null){
            //        var prop = pauseType.GetProperty("IsPaused", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            //        if(prop!=null){
            //            object v = prop.GetValue(null, null);
            //            if(v is bool) isPaused = (bool)v;
            //        }
            //        var field = HarmonyLib.AccessTools.Field(pauseType, "isPaused");
            //        if(field!=null){
            //            object v = field.GetValue(null);
            //            if(v is bool) isPaused = isPaused || (bool)v;
            //        }
            //    }
            //} catch{}

            //try{
            //    var gmType = HarmonyLib.AccessTools.TypeByName("GameManager");
            //    if(gmType!=null){
            //        var prop = gmType.GetProperty("IsPaused", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            //        if(prop!=null){
            //            object v = prop.GetValue(null, null);
            //            if(v is bool) isPaused = isPaused || (bool)v;
            //        }
            //    }
            //} catch{}

            // Also check if timeScale is 0 (paused) or cursor visible (menu)
            try
            {
                if (UnityEngine.Time.timeScale == 0f) isPaused = true;
            }
            catch { }

            // If not in-game (title screen, main menu) or paused -> 2D screen mode
            if (!isInGame || isPaused)
            {
                if (modeVR != 2)
                {
                    VRManagerLogger.LogInfo("[IMVR] Switching to 2D UI mode (MODE_VR=2) - not in-game or paused. isInGame=" + isInGame + " isPaused=" + isPaused);
                }
                modeVR = 2; // Non-VR / 2D screen mode per WinlatorXR spec
                mode3D = 0; // flat
            }
            else
            {
                if (modeVR != 1)
                {
                    VRManagerLogger.LogInfo("[IMVR] Switching to VR mode (MODE_VR=1) - in-game");
                }
                modeVR = 1; // VR mode
                mode3D = 0; // mono for Interstellar Marines
            }
        }
        catch (Exception e)
        {
            // Fallback: stay in VR mode
            VRManagerLogger.LogWarn("[IMVR] UpdateVRMode failed: " + e.Message);
            modeVR = 1;
            mode3D = 0;
        }
    }


    public static void Shutdown() { try { if (client != null) client.Close(); } catch { } try { if (recvThread != null) recvThread.Abort(); } catch { } }
}
