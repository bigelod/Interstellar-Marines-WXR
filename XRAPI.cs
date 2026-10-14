using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using UnityEngine;
using System.Globalization;

// Exact UDP format from WinlatorXR/SixDOFinator_SampleProject README
// Port 7872 (fallback 7873 for XrAPI V0.4)
// Example: client0 0.213 0.287 -0.933 0.035 0.0 0.0 -0.008 -0.229 -0.173 0.095 -0.296 0.947 -0.077 0.0 0.0 0.154 -0.240 -0.140 0.146 -0.072 0.048 0.985 0.037 0.006 -0.017 0.0678 99.00 103.40 224 TFFFFFFFFFTTTFFFFFT META

public static class XRAPI
{
    public const int LEFT = 0, RIGHT = 1;
    public const uint BTN_A = 1u << 0, BTN_B = 1u << 1, BTN_X = 1u << 2, BTN_Y = 1u << 3, BTN_THUMB = 1u << 4, BTN_MENU = 1u << 5, BTN_GRIP = 1u << 6;

    static UdpClient client;
    static Thread recvThread;
    static volatile bool running = false;
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
    static int[] PORTS = new[] { 7872, 7873, 7874, 7875 };

    public static bool XR_Init()
    {
        if (running) return true;
        foreach (var port in PORTS)
        {
            try
            {
                client = new UdpClient(port);
                client.Client.ReceiveTimeout = 500;
                running = true;
                recvThread = new Thread(() => RecvLoop(port));
                recvThread.IsBackground = true;
                recvThread.Start();
                Debug.Log("[IMVR] SixDOFinator UDP listening on " + port.ToString());
                latest.headRot = Quaternion.identity;
                latest.leftRot = Quaternion.identity;
                latest.rightRot = Quaternion.identity;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IMVR] Port " + port.ToString() + " busy: " + e.Message);

                try
                {
                    if (client != null) client.Close();
                }
                catch
                {

                }
            }
        }
        Debug.LogWarning("[IMVR] No UDP port, checking D:/xrtemp fallback");
        return false;
    }

    static void RecvLoop(int port)
    {
        IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref any);
                if (data == null || data.Length == 0) continue;
                ParseString(Encoding.UTF8.GetString(data).Trim());
            }
            catch (SocketException)
            {
                TryFileFallback();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IMVR] UDP error: " + e.Message);
            }
        }
    }

    static void TryFileFallback()
    {
        try
        {
            foreach (var dir in new[] { "D:/xrtemp", "C:/xrtemp", "./xrtemp" })
            {
                if (!System.IO.Directory.Exists(dir)) continue;
                var files = System.IO.Directory.GetFiles(dir);
                if (files.Length == 0) continue;
                ParseString(System.IO.Path.GetFileName(files[0]));
                return;
            }
        }
        catch { }
    }

    static void ParseString(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 30) return;
        int idx = parts[0].StartsWith("client") ? 1 : 0;
        try
        {
            Func<int, float> PF = (i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
            Quaternion lQuat = new Quaternion(PF(idx), PF(idx + 1), PF(idx + 2), PF(idx + 3)); idx += 4;
            Vector2 lStick = new Vector2(PF(idx), PF(idx + 1)); idx += 2;
            Vector3 lPos = new Vector3(PF(idx), PF(idx + 1), PF(idx + 2)); idx += 3;
            Quaternion rQuat = new Quaternion(PF(idx), PF(idx + 1), PF(idx + 2), PF(idx + 3)); idx += 4;
            Vector2 rStick = new Vector2(PF(idx), PF(idx + 1)); idx += 2;
            Vector3 rPos = new Vector3(PF(idx), PF(idx + 1), PF(idx + 2)); idx += 3;
            Quaternion hQuat = new Quaternion(PF(idx), PF(idx + 1), PF(idx + 2), PF(idx + 3)); idx += 4;
            Vector3 hPos = new Vector3(PF(idx), PF(idx + 1), PF(idx + 2)); idx += 3;
            float ipd = PF(idx); idx++;
            float fovH = PF(idx); idx++;
            float fovV = PF(idx); idx++;
            int frameId = 0; int.TryParse(parts[idx], out frameId); idx++;
            string boolStr = idx < parts.Length ? parts[idx] : ""; idx++;
            string hmdType = idx < parts.Length ? parts[idx] : "META";

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
            Debug.LogWarning("[IMVR] Parse fail: " + e.Message);
        }
    }

    public static bool XR_GetHeadPose(out Vector3 pos, out Quaternion rot)
    {
        lock (lockObj) { if (hasData) { pos = latest.headPos; rot = latest.headRot; return true; } }
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
        var cam = Camera.main;
        if (cam) { pos = cam.transform.position + cam.transform.forward * 0.45f + cam.transform.right * (hand == RIGHT ? 0.2f : -0.2f); rot = cam.transform.rotation; }
        return false;
    }
    public static bool XR_GetControllerState(int hand, out uint buttons, out Vector2 stick, out float trigger, out float grip)
    {
        buttons = 0; stick = Vector2.zero; trigger = 0; grip = 0;
        lock (lockObj)
        {
            if (hasData)
            {
                if (hand == LEFT) { stick = latest.leftStick; trigger = latest.lTrigger ? 1f : 0f; grip = latest.lGrip ? 1f : 0f; if (latest.lX) buttons |= BTN_X; if (latest.lY) buttons |= BTN_Y; if (latest.lThumbPress) buttons |= BTN_THUMB; if (latest.lMenu) buttons |= BTN_MENU; if (latest.lGrip) buttons |= BTN_GRIP; return true; }
                else { stick = latest.rightStick; trigger = latest.rTrigger ? 1f : 0f; grip = latest.rGrip ? 1f : 0f; if (latest.rA) buttons |= BTN_A; if (latest.rB) buttons |= BTN_B; if (latest.rThumbPress) buttons |= BTN_THUMB; if (latest.rGrip) buttons |= BTN_GRIP; return true; }
            }
        }
        stick = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        trigger = Input.GetMouseButton(0) ? 1f : 0f; grip = Input.GetMouseButton(1) ? 1f : 0f;
        return false;
    }
    public static bool GetBoolState(out State s) { lock (lockObj) { s = latest; return hasData; } }
    public static void Shutdown()
    {
        running = false;
        try
        {
            if (client != null) client.Close();
        }
        catch
        {

        }
        try
        {
            if (recvThread != null) recvThread.Abort();
        }
        catch
        {

        }
    }
}
