using UnityEngine;

public class VRHeadTracker : MonoBehaviour
{
    public static Transform VRRoot;
    public static Transform HelmetBase;
    Transform vrRoot;
    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    Transform visorTransform;
    Vector3 helmetInitialOffset;
    bool foundVisor = false;

    void Awake()
    {
        Transform helmetBase = transform;
        Helmet helmet = GetComponent<Helmet>();
        if (helmet == null)
        {
            if (transform.parent != null)
            {
                helmetBase = transform.parent;
                helmet = helmetBase.GetComponent<Helmet>();
            }
        }

        if (helmetBase != null)
        {
            HelmetBase = helmetBase;
            baseLocalPos = helmetBase.localPosition;
            baseLocalRot = helmetBase.localRotation;

            if (helmetBase.parent != null)
            {
                vrRoot = new GameObject("VRRoot_IM").transform;
                vrRoot.parent = helmetBase.parent;
                vrRoot.localPosition = baseLocalPos;
                vrRoot.localRotation = baseLocalRot;
                vrRoot.localScale = Vector3.one;
                helmetBase.parent = vrRoot;
                VRRoot = vrRoot;
            }

            Transform visor = helmetBase.Find("Root/Helmet");
            if (visor != null)
            {
                visorTransform = visor;
                helmetInitialOffset = visor.localPosition;
                foundVisor = true;
            }
            else
            {
                try
                {
                    var f = typeof(Helmet).GetField("visorTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null)
                    {
                        object v = f.GetValue(helmet);
                        visorTransform = v as Transform;
                        if (visorTransform != null)
                        {
                            helmetInitialOffset = visorTransform.localPosition;
                            foundVisor = true;
                        }
                    }
                    var offF = typeof(Helmet).GetField("helmetInitialOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (offF != null && visorTransform != null)
                    {
                        helmetInitialOffset = (Vector3)offF.GetValue(helmet);
                    }
                }
                catch { }
            }
        }
        else
        {
            baseLocalPos = transform.localPosition;
            baseLocalRot = transform.localRotation;
            vrRoot = new GameObject("VRRoot_IM").transform;
            if (transform.parent != null)
            {
                vrRoot.parent = transform.parent;
            }
            vrRoot.localPosition = baseLocalPos;
            vrRoot.localRotation = baseLocalRot;
            transform.parent = vrRoot;
            VRRoot = vrRoot;
        }

        try
        {
            PlayerCamera pc = GetComponent<PlayerCamera>();
            if (pc == null && helmetBase != null) pc = helmetBase.GetComponentInChildren<PlayerCamera>();
            if (pc != null)
            {
                UnityEngine.PostProcessing.PostProcessingBehaviour ppb = pc.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
                if (ppb != null && ppb.profile != null)
                {
                    if (ppb.profile.motionBlur != null) ppb.profile.motionBlur.enabled = false;
                    if (ppb.profile.chromaticAberration != null) ppb.profile.chromaticAberration.enabled = false;
                }
            }
        }
        catch { }
    }

    void LateUpdate()
    {
        Vector3 hPos;
        Quaternion hRot;
        if (!XRAPI.XR_GetHeadPose(out hPos, out hRot)) return;

        Vector3 e = hRot.eulerAngles;
        hRot = Quaternion.Euler(-e.x, -e.y, e.z);

        Vector3 lean = Vector3.ClampMagnitude(hPos, 0.15f);

        if (vrRoot != null)
        {
            vrRoot.localPosition = baseLocalPos + lean;
            vrRoot.localRotation = baseLocalRot * hRot;
        }

        if (foundVisor && visorTransform != null)
        {
            visorTransform.localPosition = helmetInitialOffset + lean * 0.5f;
            visorTransform.localRotation = hRot;
        }
    }
}
