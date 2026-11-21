using UnityEngine;
[DefaultExecutionOrder(-100)]
public class VRHeadTracker : MonoBehaviour
{
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    Camera cam;
    static float lastRollLog = -999f;
    static bool hasRollState = false;
    static float lastRollDeg = 0f;
    void Awake() { baseLocalPos = transform.localPosition; baseLocalRot = transform.localRotation; cam = GetComponent<Camera>(); }
    void Start()
    {
        // NearClip Postfix runs after Awake but before Start in most cases, recapture here
        // Helmet InitFirstPersonCamera sets localPosition 0, NearClip adds (0,0.08,0.05)
        // If we captured 0, HUD will be 0.08 low
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
    }
    // Applied as late as possible so the game's CameraFX/aim code cannot overwrite the
    // tuned vertical FOV before the frame is rendered.
    void OnPreRender()
    {
        if (cam != null && VRManager.cameraFov > 1f) cam.fieldOfView = VRManager.cameraFov;
    }

    void LateUpdate()
    {        if (VRManagerState.VRRoot == null) return;
        if (Marine.LocalMarine == null) return;
        VRManagerState.VRRoot.transform.position = Marine.LocalMarine.transform.position;
        VRManagerState.VRRoot.transform.rotation = Quaternion.Euler(0f, Marine.LocalMarine.transform.eulerAngles.y, 0f);
        Vector3 hPos = Vector3.zero; Quaternion hRot = Quaternion.identity;
        if (XRAPI.XR_GetHeadPose(out hPos, out hRot))
        {
            // Pose arrives already normalised and already in Unity's frame - XRAPI.ToUnity
            // does the RH->LH swap once at ingest. Do not swap it again here.
            Vector3 lean = Vector3.ClampMagnitude(hPos, 0.3f);
            // The eye offset is applied HERE, not in the near-clip patch, so config.txt can
            // be retuned live. baseLocalPos is the helmet's own pristine mount point.
            Vector3 eye = new Vector3(0f, VRManager.eyeHeight, VRManager.eyeForward);
            transform.localPosition = baseLocalPos + eye + lean;

            // Head roll is split out of the camera when the low-res world composite can
            // put it back. XOPS-WXR does it this way because its engine camera has no
            // roll: the world sprite is rotated at present time, while the 2D HUD (and
            // our visor glass, rendered after the composite) stays fixed. The weapon is
            // part of the world texture, so it rolls with the world instead of being
            // pinned to the view.
            //
            // With world_render_scale=1.0 there is no composite to rotate, so the full
            // rotation stays on the camera - the old behaviour remains the fallback.
            Quaternion raw = baseLocalRot * hRot;
            bool compositeRoll = VRManager.headRoll
                && VRWorldScaler.CompositeWanted
                && VRManagerState.inVR
                && Marine.LocalMarine != null;

            if (compositeRoll)
            {
                // Roll comes straight out of the XrAPI HMD quaternion. Do not derive it
                // from a world-space up vector: that is what made yaw and pitch leak into
                // the quad's roll. The quaternion is already the headset's orientation;
                // Unity's ZXY Euler decomposition gives us its local roll and the same
                // rotation with Z zeroed, which keeps the forward vector unchanged.
                float roll;
                Quaternion noRoll;
                SplitRoll(hRot, true, out noRoll, out roll);
                transform.localRotation = baseLocalRot * noRoll;
                VRManagerState.headRollRad = roll;

                // Temporary diagnostic: raw quaternion only, so a report can show whether
                // z is actually moving when only yaw/pitch changes.
                float now;
                try { now = Time.realtimeSinceStartup; } catch { now = 0f; }
                if (now - lastRollLog > 1f)
                {
                    lastRollLog = now;
                    Vector3 e = hRot.eulerAngles;
                    try {
                        VRManagerLogger.LogInfo("[IMVR] Raw head euler x=" + e.x.ToString("F1")
                            + " y=" + e.y.ToString("F1") + " z=" + e.z.ToString("F1")
                            + " q=" + hRot.x.ToString("F3") + "," + hRot.y.ToString("F3")
                            + "," + hRot.z.ToString("F3") + "," + hRot.w.ToString("F3")
                            + " -> quad roll=" + (roll * Mathf.Rad2Deg).ToString("F1"));
                    } catch { }
                }
            }
            else if (!VRManager.headRoll)
            {
                // Head roll explicitly off: keep the view upright everywhere.
                float roll;
                Quaternion noRoll;
                SplitRoll(hRot, false, out noRoll, out roll);
                transform.localRotation = baseLocalRot * noRoll;
                VRManagerState.headRollRad = 0f;
            }
            else
            {
                transform.localRotation = raw;
                VRManagerState.headRollRad = 0f;
            }
        }
        // No pose: leave the camera and the last roll alone. The runtime is still
        // presenting the last frame; zeroing the roll here would unroll the world
        // underneath a camera that has not moved.
        // Last, with the camera at its final position for the frame, so the
        // inside-geometry test is asked about where the eye actually ended up.
        VRClipGuard.Apply(cam);
        // Render the world at a lower internal resolution while the HUD/visor stays at
        // the window resolution. See VRWorldScaler.cs.
        VRWorldScaler.Apply(cam);
    }

    // Roll straight from the XrAPI HMD quaternion. Unity's Euler decomposition is ZXY:
    // z is the local roll and removing it with a local-Z quaternion multiply leaves the
    // forward vector untouched. No world-up projection, so yaw and pitch cannot leak
    // into the quad's roll.
    //
    // hold=true is the gameplay path: a 0.5 degree deadband kills the tiny yaw/pitch
    // coupling, and the last roll is held within 10 degrees of straight up/down, where
    // Euler Z is gimbal-locked. The camera still removes the same held angle, so the
    // view stays coherent while the quad stops jittering.
    static void SplitRoll(Quaternion raw, bool hold, out Quaternion noRoll, out float rollRad)
    {
        Vector3 e = raw.eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, e.x);
        float rollDeg = Mathf.DeltaAngle(0f, e.z);

        if (hold)
        {
            if (hasRollState && Mathf.Abs(Mathf.DeltaAngle(lastRollDeg, rollDeg)) < 0.5f)
                rollDeg = lastRollDeg;
            if (hasRollState && Mathf.Abs(pitch) > 80f)
                rollDeg = lastRollDeg;

            lastRollDeg = rollDeg;
            hasRollState = true;
        }
        else
        {
            // "Head roll off" has no state: remove the full raw roll every frame.
            hasRollState = false;
        }

        rollRad = rollDeg * Mathf.Deg2Rad;
        noRoll = raw * Quaternion.Inverse(Quaternion.AngleAxis(rollDeg, Vector3.forward));
    }
}
