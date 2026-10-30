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
    static volatile bool shouldExit = false;

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
    static int modeVR = 1; // 1 to enable VR, 0 to disable VR
    static int mode3D = 0; // 0 mono for IM, 1 SBS, 2 AER
    static float targetFovX = 104.5f;
    static float targetFovY = 104.5f;


    public static void XR_Init()
    {
        if (shouldExit) return;

        if (VRManagerState.rxTxFailCount > VRManagerState.rxTxFailMaxRetry)
        {
            Debug.Log("[IMVR] XrAPI Restart: Max retries hit, giving up");
            return;
        }

        if (client == null || sendClient == null) VRManagerState.isSetup = false;

        if (VRManagerState.isSetup) return;

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

        if (client == null)
        {
            foreach (int port in PORTS)
            {
                try { if (recvThread != null) recvThread.Abort(); } catch (Exception e) { }

                try
                {
                    client = new UdpClient(port);
                    client.Client.ReceiveTimeout = 500;
                    recvThread = new Thread(RecvLoopParam);
                    recvThread.IsBackground = true;
                    recvThread.Start(port);
                    VRManagerLogger.LogInfo("[IMVR] UDP Rx listening on " + port);
                    latest.headRot = Quaternion.identity;
                    latest.leftRot = Quaternion.identity;
                    latest.rightRot = Quaternion.identity;
                    break;
                }
                catch (Exception e)
                {
                    VRManagerLogger.LogWarn("[IMVR] UDP Rx Port " + port + " busy: " + e.Message);
                    try { if (client != null) client.Close(); } catch { }
                }
            }
        }

        // Still start sender even if receiver failed
        try
        {
            if (sendClient == null)
            {
                sendClient = new UdpClient();
            }
            SendFirstFrame();
        }
        catch { }

        VRManagerState.isSetup = true;
    }

    static void RecvLoopParam(object obj)
    {
        int port = (int)obj;
        RecvLoop(port);
    }

    static void RecvLoop(int port)
    {
        IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (client != null && any != null && VRManagerState.rxTxFailCount <= VRManagerState.rxTxFailMaxRetry)
        {
            try
            {
                byte[] data = client.Receive(ref any);
                if (data == null || data.Length == 0) continue;
                VRManagerState.rxTxFailCount = 0;
                string s = Encoding.ASCII.GetString(data).Trim();
                ParseString(s);
            }
            catch (SocketException)
            {
                if (shouldExit) break;
                VRManagerLogger.LogWarn("[IMVR] UDP Rx port failed, will try again");
                continue;
            }
            catch (Exception e)
            {
                if (shouldExit) break;
                VRManagerLogger.LogWarn("[IMVR] UDP Rx fatal error: " + e.Message + " . Restarting XrAPI...");
                VRManagerState.rxTxFailCount += 1;

                try { if (client != null) client.Close(); } catch { client = null; }
                try { if (sendClient != null) sendClient.Close(); } catch { sendClient = null; }
                VRManagerState.isSetup = false;

                Thread.Sleep(500);
                XR_Init();
                break;
            }
        }
    }

    public static void SendFirstFrame()
    {
        try
        {
            VRManagerState.inVR = false;
            modeVR = 0;
            mode3D = 0; // mono for Interstellar Marines
            leftHapticFrames = 0;
            rightHapticFrames = 0;
            string msg = BuildOutPacket();
            byte[] bytes = Encoding.ASCII.GetBytes(msg);
            if (sendClient != null)
            {
                sendClient.Send(bytes, bytes.Length, sendEndpoint);
                VRManagerLogger.LogInfo("[IMVR] UDP Sent first frame to awaken WinlatorXR: " + msg);
            }
        }
        catch (Exception e)
        {
            VRManagerLogger.LogWarn("[IMVR] UDP SendFirstFrame failed: " + e.Message + " . Restarting XrAPI...");
            VRManagerState.rxTxFailCount += 1;

            try { if (client != null) client.Close(); } catch { client = null; }
            try { if (sendClient != null) sendClient.Close(); } catch { sendClient = null; }
            VRManagerState.isSetup = false;

            XR_Init();
        }
    }

    // Haptics are a DURATION IN FRAMES, per hand, not an amplitude - we are telling
    // the runtime how long to buzz. Sending 0 does not stop a running buzz: the
    // runtime only stores values greater than zero. So the number has to be topped
    // up while the condition holds and decayed once per frame by TickHaptics.
    public static void TickHaptics()
    {
        if (leftHapticFrames > 0) leftHapticFrames--;
        if (rightHapticFrames > 0) rightHapticFrames--;
    }

    // The angle the runtime paints the captured frame across, in degrees. It does NOT
    // have to equal the camera's real FOV - see config.txt screen_fov. Lower = the same
    // scene shown smaller and more central, at full render resolution.
    public static float GetDeclaredFov() { return targetFovX; }

    public static void SetDeclaredFov(float fovDegrees)
    {
        if (Mathf.Abs(targetFovX - fovDegrees) > 0.01f)
        {
            VRManagerLogger.LogInfo("[IMVR] Declared FOV -> " + fovDegrees.ToString(CultureInfo.InvariantCulture) + " (was " + targetFovX.ToString(CultureInfo.InvariantCulture) + ")");
        }
        targetFovX = fovDegrees;
        targetFovY = fovDegrees;
    }

    // Always build the 7278 packet through this. string.Format with InvariantCulture is
    // not optional: targetFovX.ToString() under a comma-decimal locale emits "104,5",
    // which turns one field into two and corrupts every field after it.
    static string BuildOutPacket()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5}",
            leftHapticFrames, rightHapticFrames, modeVR, mode3D, targetFovX, targetFovY);
    }

    public static void SendHapticUpdate()
    {
        try
        {
            string msg = BuildOutPacket();
            byte[] bytes = Encoding.ASCII.GetBytes(msg);
            if (sendClient != null)
            {
                sendClient.Send(bytes, bytes.Length, sendEndpoint);
            }
        }
        catch (Exception e)
        {
            VRManagerLogger.LogWarn("[IMVR] UDP Send failed: " + e.Message + " . Restarting XrAPI...");
            VRManagerState.rxTxFailCount += 1;

            try { if (client != null) client.Close(); } catch { client = null; }
            try { if (sendClient != null) sendClient.Close(); } catch { sendClient = null; }
            VRManagerState.isSetup = false;

            XR_Init();
        }
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
                // Normalised and converted to Unity's frame here and nowhere else - see ToUnity above
                latest.leftRot = ToUnity(lQuat); latest.leftPos = ToUnity(lPos); latest.leftStick = lStick;
                latest.rightRot = ToUnity(rQuat); latest.rightPos = ToUnity(rPos); latest.rightStick = rStick;
                latest.headRot = ToUnity(hQuat); latest.headPos = ToUnity(hPos);
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
            VRManagerLogger.LogWarn("[IMVR] UDP Rx Parse fail: " + e.Message);
        }
    }

    static float ParseFloat(string[] parts, int i)
    {
        return float.Parse(parts[i], CultureInfo.InvariantCulture);
    }

    // ===== HANDEDNESS + NORMALISATION - DONE ONCE, HERE, AT INGEST =====
    // Every pose that leaves XR_GetHeadPose / XR_GetControllerPose is already in
    // Unity's left-handed frame and already unit length. Do NOT convert again
    // downstream: a second swap applies the mirror twice and cancels it out.
    //
    // Previously VRHeadTracker and VRWeapon each did their own
    // "pos.z = -z; rot = Euler(-e.x, -e.y, e.z)" from the raw packet. That is a
    // decompose-negate-recompose, not a mirror: it agrees with the real thing
    // for a rotation about one axis only, and diverges once pitch and roll are
    // combined (head tilted sideways while looking up). The component form below
    // is the correct right-handed -> left-handed flip for a negated Z axis.
    //
    // Normalisation is unconditional because the XrAPI UDP Tester's packets are
    // routinely a few percent long, and a non-unit quaternion gives a stretched,
    // asymmetric basis - and we take .eulerAngles of these, which is meaningless
    // for a non-unit value.
    static Quaternion ToUnity(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (m < 0.000001f) return Quaternion.identity;
        float inv = 1f / m;
        return new Quaternion(-q.x * inv, -q.y * inv, q.z * inv, q.w * inv);
    }

    static Vector3 ToUnity(Vector3 p)
    {
        return new Vector3(p.x, p.y, -p.z);
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
        VRManagerState.inVR = true;

        //We know we're in VR for sure now
        modeVR = 1;
        mode3D = 0;
        VRManagerLogger.LogInfo("[IMVR] Switching to VR mode (MODE_VR=1) - in-game");

        SendHapticUpdate();
    }

    public static void UpdateVRModeFromGameState()
    {
        try
        {
            bool isInGame = VRManagerState.inVR;
            bool isPaused = false;

            // Check if local character exists (in-game)
            if (Character.AllCharacters != null)
            {
                foreach (Character c in Character.AllCharacters)
                {
                    if (c != null && c.IsLocallyControlled)
                    {
                        isInGame = true;
                        break;
                    }
                }
            }

            try
            {
                bool cursorVisible = Cursor.visible;
                bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;

                if (GameManager.Instance != null)
                {
                    // RunningGame = actually in level, not main menu
                    isInGame = GameManager.IsRunningGame && Marine.LocalMarine != null && Marine.LocalMarine.IsAlive;

                    // This is the same pause GameManager uses for Escape menu
                    isPaused = GameManager.IsPaused;
                }

                if (cursorVisible && !cursorLocked)
                {
                    isPaused = true;
                }

                // Fallback if GameManager not ready yet (main menu)
                if (!isInGame)
                {
                    isPaused = true;
                }
            }
            catch { }

            // Also check if timeScale is 0 (paused)
            try
            {
                if (UnityEngine.Time.timeScale == 0f) isPaused = true;
            }
            catch { }

            // If not in-game (title screen, main menu) or paused -> 2D screen mode
            if (!isInGame || isPaused)
            {
                VRManagerState.inVR = false;
                if (modeVR != 0)
                {
                    VRManagerLogger.LogInfo("[IMVR] Switching to 2D UI mode (MODE_VR=0) - not in-game or paused. isInGame=" + isInGame + " isPaused=" + isPaused);
                }
                modeVR = 0; // Non-VR / 2D screen mode per WinlatorXR spec
                mode3D = 0; // flat
            }
            else
            {
                VRManagerState.inVR = true;
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
            VRManagerState.inVR = true;
            // Fallback: stay in VR mode
            VRManagerLogger.LogWarn("[IMVR] UpdateVRMode failed: " + e.Message);
            modeVR = 1;
            mode3D = 0;
        }

        SendHapticUpdate();
    }


    public static void Shutdown()
    {
        VRManagerLogger.LogWarn("[IMVR] Shutdown received, closing");
        shouldExit = true;
        try { if (client != null) client.Close(); } catch { }
        try { if (sendClient != null) sendClient.Close(); } catch { }
        try { if (recvThread != null) recvThread.Abort(); } catch { }
    }
}
