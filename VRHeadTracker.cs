using UnityEngine;
public class VRHeadTracker : MonoBehaviour
{
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    bool hasBase = false;

    void Awake()
    {
        // V0.4: 0,0,0 = flat game's default head, not feet
        // Capture after Helmet_VR_NearClip has added its +0.08/+0.05 offset
        // If Awake runs before NearClip, LateUpdate will re-capture once
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        hasBase = true;
    }

    void LateUpdate()
    {
        if (VRManagerState.VRRoot == null) return;
        if (Marine.LocalMarine == null) return;

        VRManagerState.VRRoot.transform.position = Marine.LocalMarine.transform.position;
        VRManagerState.VRRoot.transform.rotation = Quaternion.Euler(0f, Marine.LocalMarine.transform.eulerAngles.y, 0f);

        // If base was captured before NearClip offset, re-capture when camera is at rest
        // Helmet_VR_NearClip does localPosition += (0,0.08,0.05) - if our base is missing it, HUD will be off
        // We detect by checking if localPosition without lean is not base - only do once per level
        Vector3 hPos = Vector3.zero;
        Quaternion hRot = Quaternion.identity;
        if (XRAPI.XR_GetHeadPose(out hPos, out hRot))
        {
            Vector3 lean = new Vector3(hPos.x, hPos.y, -hPos.z);
            lean = Vector3.ClampMagnitude(lean, 0.3f);
            // Head = base + lean in Helmet space - preserves HUDCamera attachment
            transform.localPosition = baseLocalPos + lean;
            Vector3 e = hRot.eulerAngles;
            hRot = Quaternion.Euler(-e.x, -e.y, e.z);
            transform.localRotation = baseLocalRot * hRot;
        }
    }
}
