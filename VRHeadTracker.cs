using UnityEngine;
public class VRHeadTracker : MonoBehaviour
{
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    bool hasBase = false;
    void Awake() { baseLocalPos = transform.localPosition; baseLocalRot = transform.localRotation; hasBase = true; }
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
            Vector3 lean = new Vector3(hPos.x, hPos.y, -hPos.z);
            lean = Vector3.ClampMagnitude(lean, 0.3f);
            transform.localPosition = baseLocalPos + lean;
            Vector3 e = hRot.eulerAngles;
            hRot = Quaternion.Euler(-e.x, -e.y, e.z);
            transform.localRotation = baseLocalRot * hRot;
        }
    }
}
