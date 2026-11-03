using UnityEngine;
public class VRHeadTracker : MonoBehaviour
{
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    bool hasBase = false;
    Camera cam;
    void Awake() { baseLocalPos = transform.localPosition; baseLocalRot = transform.localRotation; hasBase = true; cam = GetComponent<Camera>(); }
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
            transform.localRotation = baseLocalRot * hRot;
        }
        // Last, with the camera at its final position for the frame, so the
        // inside-geometry test is asked about where the eye actually ended up.
        VRClipGuard.Apply(cam);
    }
}
