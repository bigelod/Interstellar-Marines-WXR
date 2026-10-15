using UnityEngine;

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

        // 1. Same invert as HMD
        Vector3 e = r.eulerAngles;
        r = Quaternion.Euler(-e.x, -e.y, e.z);

        // 2. RH->LH position
        p = new Vector3(p.x, p.y, -p.z);

        // 3. Fix 180° backwards
        //r = r * Quaternion.Euler(0f, 180f, 0f);

        // Known bug: Quest 3 vs Pico 4 Ultra hands are 180deg rolled
        if (!VRManager.flipHands) r = r * Quaternion.Euler(0f, 0f, 180f);
        // 5. HMD-local -> world (app sends local to HMD)
        Transform refT = VRHeadTracker.VRRoot;
        Transform origin = refT != null ? refT.parent : null;
        if (origin != null)
        {
            p = origin.TransformPoint(p);
            r = origin.rotation * r;
        }

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
