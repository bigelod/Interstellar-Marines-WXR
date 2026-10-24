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

        // XRAPI stores raw OpenXR RH - convert both pos and rot
        // Pos: flip Z. Rot: invert pitch/yaw. Same as head, same frame.
        p = new Vector3(p.x, p.y, -p.z);
        Vector3 e = r.eulerAngles;
        r = Quaternion.Euler(-e.x, -e.y, e.z);

        // Yaw fix - Quest 3 vs Pico is YAW, not roll (0,0,180)
        // flipHands from config is the switch
        if (VRManager.flipHands) r = r * Quaternion.Euler(0f, 180f, 0f);

        // Controller pose is ABSOLUTE tracking space, same as head - NOT HMD-local
        // (XRAPI.ParseString: lPos/rPos/hPos all in same space)
        // So NO headWorld + headRot * p - that double-counts head.
        // Direct: VRRoot is the tracking origin at Marine feet
        if (VRManagerState.VRRoot != null)
        {
            Transform refT = VRManagerState.VRRoot.transform;
            p = refT.TransformPoint(p);
            r = refT.rotation * r;
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
