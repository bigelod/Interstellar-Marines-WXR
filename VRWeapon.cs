using UnityEngine;
using System.Reflection;

public class VRWeapon : MonoBehaviour
{
    public bool rightDominant = true;
    RangedWeapon rw;
    Transform muzzle;

    void Awake()
    {
        rw = GetComponent<RangedWeapon>();
        if (rw != null) muzzle = rw.GetMuzzleFlashTransform();
    }

    void LateUpdate()
    {
        if (rw == null || !rw.IsLocallyControlled) return;
        int hand = rightDominant ? XRAPI.RIGHT : XRAPI.LEFT;
        Vector3 p;
        Quaternion r;
        if (!XRAPI.XR_GetControllerPose(hand, out p, out r)) return;

        p = new Vector3(p.x, p.y, -p.z);
        Vector3 e = r.eulerAngles;
        r = Quaternion.Euler(-e.x, -e.y, e.z);

        if (!VRManager.flipHands) r = r * Quaternion.Euler(0f, 180f, 0f);

        Vector3 headWorld = Vector3.zero;
        Quaternion headWorldRot = Quaternion.identity;
        bool foundHead = false;

        Camera[] all = Camera.allCameras;
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetComponent<VRHeadTracker>() != null)
                {
                    headWorld = all[i].transform.position;
                    headWorldRot = all[i].transform.rotation;
                    foundHead = true;
                    break;
                }
            }
        }
        if (!foundHead && VRManagerState.VRRoot != null)
        {
            Transform refT = VRManagerState.VRRoot.transform;
            Vector3 hPos = Vector3.zero; Quaternion hRot = Quaternion.identity;
            XRAPI.XR_GetHeadPose(out hPos, out hRot);
            Vector3 lean = new Vector3(hPos.x, hPos.y, -hPos.z);
            Vector3 he = hRot.eulerAngles; hRot = Quaternion.Euler(-he.x, -he.y, he.z);
            Vector3 baseHead = refT.position + refT.up * 1.62f;
            headWorld = baseHead + refT.rotation * lean;
            headWorldRot = refT.rotation * hRot;
        }

        p = headWorld + headWorldRot * p;
        r = headWorldRot * r;

        Transform model = null;
        if (rw.WeaponModel != null) model = rw.WeaponModel.transform;
        if (model != null)
        {
            model.position = p;
            model.rotation = r;
        }
        if (muzzle != null) muzzle.rotation = r;
    }
}
