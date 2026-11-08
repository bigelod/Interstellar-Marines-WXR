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
    // Signature of the last logged game-state, so State: lines appear once per
    // transition instead of once per frame. Letters, in order:
    //   V=VR mode  G=inGame  P=paused | c=localChar r=runningGame m=marine a=alive
    //   g=GameManager.IsPaused  t=timeScale==0  f=notInGame fallback  u=UI wants pointer
    static string lastStateSig = "";

    // The recentre origin, subtracted from head AND hand positions alike.
    // general_wxr/CLAUDE.md: "Convert each pose independently of the head. Share only the
    // recentre origin." Sharing it is what keeps the hands in the right place relative to
    // the head after a recentre - offsetting only the head would move the player's hands
    // away from their body.
    static Vector3 recenterOrigin = Vector3.zero;

    static int rxTimeouts = 0;
    static int lastRxTimeoutTick = -100000;
    // The receive thread parses into these buffers; no per-packet allocation, so a
    // 72-90 Hz stream cannot trigger a GC pause on the main thread.
    static readonly byte[] recvBuffer = new byte[2048];
    static readonly float[] packetFloats = new float[28];
    static bool parseOk;

    // Handshake files. The runtime wipes Z:/tmp/xr at its own startup, so `version`
    // must be written unconditionally and re-checked while the session runs. The old
    // "only if absent" guard could leave the mod with no handshake at all after a
    // runtime restart, which presents as a silent pose stream with no error.
    static string handshakeDir = "Z:/tmp/xr";
    static float lastHandshakeCheck = -999f;
    static bool hasDetection = false;
    static bool detectedUpsideDown = false;
    static string detectedMake = "";
    static string detectedProduct = "";

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

        // Write the handshake unconditionally, then read `system` if the runtime has
        // already written it. If it has not, TickHandshake retries every 2s.
        handshakeDir = ResolveHandshakeDir();
        EnsureHandshakeFiles(true);
        ReadSystemInfo();

        if (client == null)
        {
            foreach (int port in PORTS)
            {
                try { if (recvThread != null) recvThread.Abort(); } catch { }

                try
                {
                    client = new UdpClient(port);
                    client.Client.ReceiveTimeout = 500;
                    // Absorb a short burst instead of dropping it. On a standalone
                    // device the receiver can be descheduled while the GPU stalls.
                    try { client.Client.ReceiveBufferSize = 65536; } catch { }
                    recvThread = new Thread(RecvLoopParam);
                    recvThread.IsBackground = true;
                    // Above Normal so a frame-time spike does not leave a queue of
                    // stale packets behind; the thread only parses and copies a pose.
                    try { recvThread.Priority = System.Threading.ThreadPriority.AboveNormal; } catch { }
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

    // On device this is Z:/tmp/xr. If Z: does not exist (desktop tester) use an
    // xrtemp folder next to the DLL, so the same binary runs in both places.
    static string ResolveHandshakeDir()
    {
        try
        {
            if (Directory.Exists("Z:/tmp/xr")) return "Z:/tmp/xr";
            if (Directory.Exists("Z:/tmp"))
            {
                Directory.CreateDirectory("Z:/tmp/xr");
                return "Z:/tmp/xr";
            }
        }
        catch { }

        try
        {
            string dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(dllDir))
            {
                string local = Path.Combine(dllDir, "xrtemp");
                if (!Directory.Exists(local)) Directory.CreateDirectory(local);
                return local;
            }
        }
        catch { }

        return "Z:/tmp/xr";
    }

    static bool WriteIfDifferent(string path, string text)
    {
        try
        {
            if (File.Exists(path))
            {
                string current = File.ReadAllText(path).Trim();
                if (current == text.Trim()) return false;
            }
            // ASCII, not the default UTF-8: the runtime matches `version` with
            // startsWith, and a stray BOM turns "0.4" into an unrecognised value.
            File.WriteAllText(path, text, Encoding.ASCII);
            return true;
        }
        catch (Exception e)
        {
            try { VRManagerLogger.LogWarn("[IMVR] Handshake write failed: " + path + " : " + e.Message); } catch { }
            return false;
        }
    }

    static void EnsureHandshakeFiles(bool logAlways)
    {
        try
        {
            if (!Directory.Exists(handshakeDir)) Directory.CreateDirectory(handshakeDir);
        }
        catch (Exception e)
        {
            try { VRManagerLogger.LogWarn("[IMVR] Handshake dir failed: " + handshakeDir + " : " + e.Message); } catch { }
            return;
        }

        bool wroteVersion = WriteIfDifferent(Path.Combine(handshakeDir, "version"), "0.4" + Environment.NewLine);
        bool wroteVr = WriteIfDifferent(Path.Combine(handshakeDir, "vr"), "VR" + Environment.NewLine);
        if (logAlways || wroteVersion || wroteVr)
        {
            string suffix = (wroteVersion || wroteVr) ? " (rewritten)" : "";
            try { VRManagerLogger.LogInfo("[IMVR] Handshake " + handshakeDir + " version=0.4 vr=VR" + suffix); } catch { }
        }
    }

    // Called every 2s from VRInputRunner.Update. Repairs the directory if the runtime
    // restarted and wiped it, and keeps retrying `system` until it appears.
    public static void TickHandshake()
    {
        float now = 0f;
        try { now = Time.realtimeSinceStartup; } catch { return; }
        if (now - lastHandshakeCheck < 2f) return;
        lastHandshakeCheck = now;
        EnsureHandshakeFiles(false);
        if (!hasDetection) ReadSystemInfo();
        ApplyDetectedHandedness();
    }

    // `system` line 1 is the manufacturer, line 2 the product codename. The codename
    // mapping is from Docs/general_wxr/XRAPI_V0_4_DESC.md; default to Quest 3 rather
    // than failing when the file is absent.
    static void ReadSystemInfo()
    {
        try
        {
            string systemFile = Path.Combine(handshakeDir, "system");
            if (!File.Exists(systemFile)) return;

            string[] lines = File.ReadAllLines(systemFile);
            string make = "META";
            string product = "";
            if (lines.Length > 0 && lines[0] != null && lines[0].Trim().Length > 0) make = lines[0].Trim().ToUpperInvariant();
            if (lines.Length > 1 && lines[1] != null) product = lines[1].Trim().ToUpperInvariant();
            if (make == "OCULUS") make = "META";
            if (make.Length == 0) make = "META";

            // Do not latch a partial read. The runtime writes this file at startup;
            // if we catch it half-written, retry on the next TickHandshake instead of
            // assuming Quest 3 forever.
            if (lines.Length < 2 || product.Length == 0) return;

            bool upsideDown = false;
            string label = "unknown";
            if (product == "EUREKA" || product == "PANTHER") { upsideDown = false; label = "Quest 3"; }
            else if (product == "HOLLYWOOD") { upsideDown = true; label = "Quest 2"; }
            else if (product == "SEACLIFF") { upsideDown = true; label = "Quest Pro"; }
            else if (product == "MONTEREY") { upsideDown = false; label = "Quest 1"; }
            else if (make == "META") { upsideDown = false; label = "Quest 3 (default)"; }
            else { label = make + "/" + product; }

            detectedMake = make;
            detectedProduct = product;
            detectedUpsideDown = upsideDown;
            hasDetection = true;
            try
            {
                VRManagerLogger.LogInfo("[IMVR] Headset: make=" + make
                    + " product=" + (product.Length > 0 ? product : "?")
                    + " -> " + label + ", hands "
                    + (upsideDown ? "upside-down (flipHands=true)" : "upright (flipHands=false)"));
            }
            catch { }
            ApplyDetectedHandedness();
        }
        catch (Exception e)
        {
            try { VRManagerLogger.LogWarn("[IMVR] system read failed: " + e.Message); } catch { }
        }
    }

    // Auto-detection is a default, not an override. An explicit flipHands= in
    // config.txt always wins; flipHands=auto (or no key) lets this apply.
    static void ApplyDetectedHandedness()
    {
        if (!hasDetection) return;
        if (VRManager.flipHandsExplicit) return;
        if (VRManager.flipHands == detectedUpsideDown) return;
        VRManager.flipHands = detectedUpsideDown;
        try
        {
            VRManagerLogger.LogInfo("[IMVR] flipHands auto -> " + VRManager.flipHands.ToString()
                + " (" + detectedMake + "/" + detectedProduct + ")");
        }
        catch { }
    }

    static void RecvLoopParam(object obj)
    {
        int port = (int)obj;
        RecvLoop(port);
    }

    static void RecvLoop(int port)
    {
        while (client != null && VRManagerState.rxTxFailCount <= VRManagerState.rxTxFailMaxRetry)
        {
            try
            {
                // Receive into a reusable buffer. UdpClient.Receive allocates a new
                // byte[] for every datagram; at 72-90 Hz that is a steady GC load.
                int n = client.Client.Receive(recvBuffer);
                if (n <= 0) continue;

                // Drain everything already queued and keep only the newest packet.
                // Parsing one-per-frame builds a queue and acts on staler poses.
                try
                {
                    int guard = 0;
                    while (client != null && client.Available > 0 && guard++ < 32)
                    {
                        int n2 = client.Client.Receive(recvBuffer);
                        if (n2 > 0) n = n2;
                    }
                }
                catch { }

                VRManagerState.rxTxFailCount = 0;
                rxTimeouts = 0;

                // A malformed packet must not tear down the socket. Parsing is
                // allocation-free and should not throw; if it does, skip this packet.
                try { ParsePacket(recvBuffer, n); }
                catch (Exception pe)
                {
                    if (VRManagerState.verboseDebug)
                    {
                        try { VRManagerLogger.LogWarn("[IMVR] UDP parse skipped: " + pe.Message); } catch { }
                    }
                }
            }
            catch (SocketException)
            {
                if (shouldExit) break;
                // A 500ms ReceiveTimeout with no data is NORMAL whenever MODE_VR is 0 -
                // the runtime is not sending, by design, while in menus. Logging every
                // one buried the real signal under 267 identical lines on 1 Nov. Keep the
                // signal, drop the spam: report at most every 5s, with the count.
                // Environment.TickCount, not Time.* - this is a background thread.
                rxTimeouts++;
                int now = Environment.TickCount;
                if (unchecked(now - lastRxTimeoutTick) > 5000)
                {
                    lastRxTimeoutTick = now;
                    VRManagerLogger.LogWarn("[IMVR] UDP Rx: no packets for a while (" + rxTimeouts + " timeouts). Normal if MODE_VR=0; a problem if it persists in gameplay.");
                }
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

    // Allocation-free packet parser. The old version split the line into ~30 strings
    // and called float.Parse on each, which is a steady GC source at 72-90 Hz. This
    // scans the ASCII bytes in place. It also means the receive thread never touches
    // Time.* (Unity APIs are not thread-safe), so it cannot stall the main thread.
    static void ParsePacket(byte[] b, int len)
    {
        if (b == null || len < 32) return;

        int i = 0;
        SkipSpaces(b, ref i, len);
        int tokStart = i;
        while (i < len && b[i] != ' ') i++;
        if (!IsClientToken(b, tokStart, i - tokStart)) i = tokStart;

        parseOk = true;
        float[] f = packetFloats;
        for (int k = 0; k < 28; k++) f[k] = ReadFloat(b, ref i, len);
        int frameId = ReadInt(b, ref i, len);
        if (!parseOk) return;

        SkipSpaces(b, ref i, len);
        int boolStart = i;
        while (i < len && b[i] != ' ') i++;
        int boolLen = i - boolStart;

        bool lGrip = BoolAt(b, boolStart, boolLen, 0);
        bool lMenu = BoolAt(b, boolStart, boolLen, 1);
        bool lThumbPress = BoolAt(b, boolStart, boolLen, 2);
        bool lThumbLeft = BoolAt(b, boolStart, boolLen, 3);
        bool lThumbRight = BoolAt(b, boolStart, boolLen, 4);
        bool lThumbUp = BoolAt(b, boolStart, boolLen, 5);
        bool lThumbDown = BoolAt(b, boolStart, boolLen, 6);
        bool lTrigger = BoolAt(b, boolStart, boolLen, 7);
        bool lX = BoolAt(b, boolStart, boolLen, 8);
        bool lY = BoolAt(b, boolStart, boolLen, 9);
        bool rA = BoolAt(b, boolStart, boolLen, 10);
        bool rB = BoolAt(b, boolStart, boolLen, 11);
        bool rGrip = BoolAt(b, boolStart, boolLen, 12);
        bool rThumbPress = BoolAt(b, boolStart, boolLen, 13);
        bool rThumbLeft = BoolAt(b, boolStart, boolLen, 14);
        bool rThumbRight = BoolAt(b, boolStart, boolLen, 15);
        bool rThumbUp = BoolAt(b, boolStart, boolLen, 16);
        bool rThumbDown = BoolAt(b, boolStart, boolLen, 17);
        bool rTrigger = BoolAt(b, boolStart, boolLen, 18);

        lock (lockObj)
        {
            // Normalised and converted to Unity's frame here and nowhere else - see ToUnity above
            latest.leftRot = ToUnity(new Quaternion(f[0], f[1], f[2], f[3]));
            latest.leftStick = new Vector2(f[4], f[5]);
            latest.leftPos = ToUnity(new Vector3(f[6], f[7], f[8]));
            latest.rightRot = ToUnity(new Quaternion(f[9], f[10], f[11], f[12]));
            latest.rightStick = new Vector2(f[13], f[14]);
            latest.rightPos = ToUnity(new Vector3(f[15], f[16], f[17]));
            latest.headRot = ToUnity(new Quaternion(f[18], f[19], f[20], f[21]));
            latest.headPos = ToUnity(new Vector3(f[22], f[23], f[24]));
            latest.ipd = f[25]; latest.fovH = f[26]; latest.fovV = f[27]; latest.frameId = frameId;
            latest.lGrip = lGrip; latest.lMenu = lMenu; latest.lThumbPress = lThumbPress; latest.lThumbLeft = lThumbLeft; latest.lThumbRight = lThumbRight;
            latest.lThumbUp = lThumbUp; latest.lThumbDown = lThumbDown; latest.lTrigger = lTrigger; latest.lX = lX; latest.lY = lY;
            latest.rA = rA; latest.rB = rB; latest.rGrip = rGrip; latest.rThumbPress = rThumbPress; latest.rThumbLeft = rThumbLeft;
            latest.rThumbRight = rThumbRight; latest.rThumbUp = rThumbUp; latest.rThumbDown = rThumbDown; latest.rTrigger = rTrigger;
            // hmdType is unused; `system` detection owns the headset identity now.
            latest.hmdType = "";
            latest.lastUpdate = Environment.TickCount;
            hasData = true;
        }
    }

    static void SkipSpaces(byte[] b, ref int i, int len)
    {
        while (i < len && (b[i] == ' ' || b[i] == '\t' || b[i] == '\r' || b[i] == '\n')) i++;
    }

    static bool IsClientToken(byte[] b, int start, int len)
    {
        if (len < 6) return false;
        return (b[start] | 32) == 'c'
            && (b[start + 1] | 32) == 'l'
            && (b[start + 2] | 32) == 'i'
            && (b[start + 3] | 32) == 'e'
            && (b[start + 4] | 32) == 'n'
            && (b[start + 5] | 32) == 't';
    }

    static bool BoolAt(byte[] b, int start, int len, int idx)
    {
        if (idx < 0 || idx >= len) return false;
        return b[start + idx] == 'T';
    }

    static float ReadFloat(byte[] b, ref int i, int len)
    {
        SkipSpaces(b, ref i, len);
        if (i >= len) { parseOk = false; return 0f; }

        int sign = 1;
        if (b[i] == '-') { sign = -1; i++; }
        else if (b[i] == '+') { i++; }

        double val = 0.0;
        bool digits = false;
        while (i < len && b[i] >= '0' && b[i] <= '9')
        {
            val = val * 10.0 + (b[i] - '0');
            i++;
            digits = true;
        }
        if (i < len && b[i] == '.')
        {
            i++;
            double frac = 0.1;
            while (i < len && b[i] >= '0' && b[i] <= '9')
            {
                val += (b[i] - '0') * frac;
                frac *= 0.1;
                i++;
                digits = true;
            }
        }
        if (!digits) { parseOk = false; return 0f; }

        if (i < len && (b[i] == 'e' || b[i] == 'E'))
        {
            i++;
            int esign = 1;
            if (i < len && (b[i] == '+' || b[i] == '-'))
            {
                if (b[i] == '-') esign = -1;
                i++;
            }
            int exp = 0;
            bool expDigits = false;
            while (i < len && b[i] >= '0' && b[i] <= '9')
            {
                exp = exp * 10 + (b[i] - '0');
                i++;
                expDigits = true;
            }
            if (expDigits) val *= Math.Pow(10.0, esign * exp);
        }
        return (float)(sign * val);
    }

    static int ReadInt(byte[] b, ref int i, int len)
    {
        SkipSpaces(b, ref i, len);
        if (i >= len) { parseOk = false; return 0; }

        int sign = 1;
        if (b[i] == '-') { sign = -1; i++; }
        else if (b[i] == '+') { i++; }

        int val = 0;
        bool digits = false;
        while (i < len && b[i] >= '0' && b[i] <= '9')
        {
            val = val * 10 + (b[i] - '0');
            i++;
            digits = true;
        }
        if (!digits) { parseOk = false; return 0; }
        return sign * val;
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

    // Makes wherever the player is standing right now the new origin. Bound to right
    // thumbstick click - play-space position drifts over a long session.
    public static void Recenter()
    {
        lock (lockObj)
        {
            if (!hasData) return;
            recenterOrigin = latest.headPos;
        }
        VRManagerLogger.LogInfo("[IMVR] Recentred - origin now " + recenterOrigin.ToString("F2"));
    }

    public static bool XR_GetHeadPose(out Vector3 pos, out Quaternion rot)
    {
        lock (lockObj)
        {
            if (hasData) { pos = latest.headPos - recenterOrigin; rot = latest.headRot; return true; }
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
                if (hand == LEFT) { pos = latest.leftPos - recenterOrigin; rot = latest.leftRot; return true; }
                else { pos = latest.rightPos - recenterOrigin; rot = latest.rightRot; return true; }
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

            // Every individual signal is captured so the log can say WHICH one moved.
            // Without this, a mode flip only ever reported its own result, which is how
            // the Cursor.visible bug survived as long as it did.
            bool sigLocalChar = false, sigRunning = false, sigMarine = false, sigAlive = false;
            bool sigGmPaused = false, sigTimeScaleZero = false, sigNotInGameFallback = false;
            bool sigUiBlocking = false;
            float sigTimeScale = 1f;

            // Check if local character exists (in-game)
            if (Character.AllCharacters != null)
            {
                foreach (Character c in Character.AllCharacters)
                {
                    if (c != null && c.IsLocallyControlled)
                    {
                        isInGame = true;
                        sigLocalChar = true;
                        break;
                    }
                }
            }

            try
            {
                if (GameManager.Instance != null)
                {
                    sigRunning = GameManager.IsRunningGame;
                    sigMarine = Marine.LocalMarine != null;
                    sigAlive = sigMarine && Marine.LocalMarine.IsAlive;
                    sigGmPaused = GameManager.IsPaused;
                    // RunningGame = actually in level, not main menu
                    isInGame = GameManager.IsRunningGame && Marine.LocalMarine != null && Marine.LocalMarine.IsAlive;

                    // This is the same pause GameManager uses for Escape menu
                    isPaused = GameManager.IsPaused;
                }

                // DO NOT reinstate a Cursor.visible test here. In Winlator the cursor is
                // ALWAYS visible, so "cursorVisible && !cursorLocked" is permanently true
                // and forces isPaused every frame. The 1 Nov 2026 logs show exactly that:
                // "isInGame=True isPaused=True" in gameplay, MODE_VR pinned at 0, and 267
                // receive timeouts because MODE_VR=0 stops the runtime sending any UDP at
                // all. The identical test was removed from the input patches long ago for
                // the same reason; this copy was missed.

                // Fallback if GameManager not ready yet (main menu)
                if (!isInGame)
                {
                    isPaused = true;
                    sigNotInGameFallback = true;
                }
            }
            catch { }

            // A menu that wants the pointer counts as paused: MODE_VR=0 hands screen
            // control back to the runtime's own mouse emulation, which is what makes the
            // menu usable at all. Same signal the game uses for cursor lock.
            try
            {
                GameUI ui = GameUI.Instance;
                if (ui != null && (ui.IsShowingMouseBlockingGUI || ui.IsShowingModalGUI))
                {
                    isPaused = true;
                    sigUiBlocking = true;
                }
            }
            catch { }

            // Also check if timeScale is 0 (paused)
            try
            {
                sigTimeScale = UnityEngine.Time.timeScale;
                if (sigTimeScale == 0f) { isPaused = true; sigTimeScaleZero = true; }
            }
            catch { }

            // If not in-game (title screen, main menu) or paused -> 2D screen mode
            if (!isInGame || isPaused)
            {
                VRManagerState.inVR = false;
                modeVR = 0; // Non-VR / 2D screen mode per WinlatorXR spec
                mode3D = 0; // flat
            }
            else
            {
                VRManagerState.inVR = true;
                modeVR = 1; // VR mode
                mode3D = 0; // mono for Interstellar Marines
            }

            // Log on any change to ANY contributing signal, not just when modeVR flips.
            // Opening or closing the pause menu moves gmPaused and usually timeScale, so
            // this fires there as well as on every mode change - and names the cause.
            // Change-gated, so it is one line per transition rather than per frame.
            string sig = (modeVR == 1 ? "V" : "-")
                + (isInGame ? "G" : "-") + (isPaused ? "P" : "-")
                + (sigLocalChar ? "c" : "-") + (sigRunning ? "r" : "-")
                + (sigMarine ? "m" : "-") + (sigAlive ? "a" : "-")
                + (sigGmPaused ? "g" : "-") + (sigTimeScaleZero ? "t" : "-")
                + (sigNotInGameFallback ? "f" : "-") + (sigUiBlocking ? "u" : "-");
            if (sig != lastStateSig)
            {
                string was = lastStateSig;
                lastStateSig = sig;
                VRManagerLogger.LogInfo("[IMVR] State: MODE_VR=" + modeVR
                    + " isInGame=" + isInGame + " isPaused=" + isPaused
                    + " | localChar=" + sigLocalChar
                    + " runningGame=" + sigRunning
                    + " marine=" + sigMarine
                    + " alive=" + sigAlive
                    + " gmPaused=" + sigGmPaused
                    + " timeScale=" + sigTimeScale.ToString("0.00")
                    + " notInGameFallback=" + sigNotInGameFallback
                    + " uiWantsPointer=" + sigUiBlocking
                    + "  [" + was + " -> " + sig + "]");
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

        // `vr` declares that this process wants VR; leaving it behind after a clean
        // shutdown can convince the runtime that a dead process still wants it.
        // `version` stays: it is the protocol this binary speaks, not a live claim.
        try
        {
            if (!string.IsNullOrEmpty(handshakeDir))
            {
                string vrFile = Path.Combine(handshakeDir, "vr");
                if (File.Exists(vrFile))
                {
                    File.Delete(vrFile);
                    VRManagerLogger.LogWarn("[IMVR] Handshake vr file removed");
                }
            }
        }
        catch { }
    }
}
