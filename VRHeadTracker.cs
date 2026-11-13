using UnityEngine;
public class VRHeadTracker : MonoBehaviour
{
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    Camera cam;
    void Awake() { baseLocalPos = transform.localPosition; baseLocalRot = transform.localRotation; cam = GetComponent<Camera>(); }
    void Start()
    {
        // NearClip Postfix runs after Awake but before Start in most cases, recapture here
        // Helmet InitFirstPersonCamera sets localPosition 0, NearClip adds (0,0.08,0.05)
        // If we captured 0, HUD will be 0.08 low
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
    }
    void LateUpdate()
    {
        if (VRManagerState.VRRoot == null) return;
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
                && VRManager.worldRenderScale < 0.999f
                && VRManagerState.inVR
                && Marine.LocalMarine != null;

            if (compositeRoll)
            {
                float roll;
                Quaternion noRoll;
                SplitRoll(raw, baseLocalRot * Vector3.up, out noRoll, out roll);
                transform.localRotation = noRoll;
                VRManagerState.headRollRad = roll;
            }
            else if (!VRManager.headRoll)
            {
                // Head roll explicitly off: keep the view upright everywhere.
                float roll;
                Quaternion noRoll;
                SplitRoll(raw, baseLocalRot * Vector3.up, out noRoll, out roll);
                transform.localRotation = noRoll;
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

    // Split a head rotation into "yaw/pitch only" and the roll left over about its own
    // forward axis. upRef is the parent/base up vector, so a fixed helmet-mount tilt is
    // preserved and only the tracked roll is removed.
    static void SplitRoll(Quaternion raw, Vector3 upRef, out Quaternion noRoll, out float rollRad)
    {
        Vector3 f = raw * Vector3.forward;
        Vector3 actualUp = raw * Vector3.up;

        // World/base up projected onto the plane perpendicular to the view forward.
        Vector3 up0 = upRef - f * Vector3.Dot(upRef, f);
        if (up0.sqrMagnitude < 0.000001f)
        {
            // Looking straight up or down: the reference is degenerate, so use another
            // stable axis. Roll is undefined there anyway; keep the frame deterministic.
            Vector3 fallback = (Mathf.Abs(Vector3.Dot(f, Vector3.forward)) > 0.999f)
                ? Vector3.right : Vector3.forward;
            up0 = fallback - f * Vector3.Dot(fallback, f);
        }

        if (up0.sqrMagnitude < 0.000001f)
        {
            // Degenerate even after the fallback (looking exactly along it). There is no
            // meaningful roll here; leave the rotation alone.
            noRoll = raw;
            rollRad = 0f;
            return;
        }

        up0.Normalize();
        float angle = Vector3.Angle(up0, actualUp);
        float sign = Mathf.Sign(Vector3.Dot(Vector3.Cross(up0, actualUp), f));
        rollRad = angle * sign * Mathf.Deg2Rad;
        noRoll = Quaternion.LookRotation(f, up0);
    }
}
