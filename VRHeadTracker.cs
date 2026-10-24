using UnityEngine;
public class VRHeadTracker : MonoBehaviour
{
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    bool hasBase = false;

    void Awake()
    {
        // This is the flat game's default head - where 0,0,0 in XRAPI lives
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        hasBase = true;
    }

    void LateUpdate()
    {
        if (VRManagerState.VRRoot == null) return;
        if (Marine.LocalMarine == null) return;

        // VRRoot still follows Marine for weapons, not for head
        VRManagerState.VRRoot.transform.position = Marine.LocalMarine.transform.position;
        VRManagerState.VRRoot.transform.rotation = Quaternion.Euler(0f, Marine.LocalMarine.transform.eulerAngles.y, 0f);

        Vector3 hPos = Vector3.zero;
        Quaternion hRot = Quaternion.identity;
        if (XRAPI.XR_GetHeadPose(out hPos, out hRot))
        {
            // XRAPI V0.4: hPos is OFFSET from base, not absolute
            // Convert RH->LH for the offset only
            Vector3 lean = new Vector3(hPos.x, hPos.y, -hPos.z);
            lean = Vector3.ClampMagnitude(lean, 0.3f); // don't let it fly off

            // Head = base + lean, in Helmet space - this preserves HUD attachment
            transform.localPosition = baseLocalPos + lean;

            Vector3 e = hRot.eulerAngles;
            hRot = Quaternion.Euler(-e.x, -e.y, e.z);
            transform.localRotation = baseLocalRot * hRot;
        }
    }
}