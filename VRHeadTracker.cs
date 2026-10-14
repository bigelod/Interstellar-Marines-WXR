using UnityEngine;
using UnityEngine.PostProcessing;

public class VRHeadTracker : MonoBehaviour
{
    Transform vrRoot;
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;

    void Awake()
    {
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        vrRoot = new GameObject("VRRoot_IM").transform;
        vrRoot.parent = transform.parent;
        vrRoot.localPosition = baseLocalPos;
        vrRoot.localRotation = baseLocalRot;
        transform.parent = vrRoot;

        PlayerCamera pc = GetComponent<PlayerCamera>();
        if(pc!=null){
            PostProcessingBehaviour ppb = pc.GetComponent<PostProcessingBehaviour>();
            if(ppb!=null && ppb.profile!=null){
                if(ppb.profile.motionBlur!=null) ppb.profile.motionBlur.enabled = false;
                if(ppb.profile.chromaticAberration!=null) ppb.profile.chromaticAberration.enabled = false;
                if (ppb.profile.depthOfField != null) ppb.profile.depthOfField.enabled = false;
            }
        }
    }

    void LateUpdate()
    {
        Vector3 hPos;
        Quaternion hRot;
        if(!XRAPI.XR_GetHeadPose(out hPos, out hRot)) return;
        Vector3 lean = Vector3.ClampMagnitude(hPos, 0.15f);
        vrRoot.localPosition = baseLocalPos + lean;
        vrRoot.localRotation = baseLocalRot * hRot;
    }
}
