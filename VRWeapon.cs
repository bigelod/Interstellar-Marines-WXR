using UnityEngine;
using System.Reflection;

public class VRWeapon : MonoBehaviour
{
    public bool rightDominant = true;
    RangedWeapon rw;
    Transform muzzle;

    // Haptics: buzz the dominant hand whenever the ammo counter drops.
    // Driving it off the counter rather than off the Fire input means it fires once
    // per round actually spent - so full-auto buzzes per shot, a dry trigger pull
    // buzzes not at all, and a shot the game refused (no ammo, reloading, jammed)
    // produces no phantom kick.
    const int HAPTIC_SHOT_FRAMES = 6;
    int lastAmmo = -1;

    void Awake()
    {
        rw = GetComponent<RangedWeapon>();
        if (rw != null) muzzle = rw.GetMuzzleFlashTransform();
    }

    void LateUpdate()
    {
        if (rw == null || !rw.IsLocallyControlled) return;
        int hand = rightDominant ? XRAPI.RIGHT : XRAPI.LEFT;

        // Ammo went down = a round was spent. A reload or a weapon swap raises the
        // count, so only a decrease counts, and the first frame only primes lastAmmo.
        try
        {
            int ammoNow = rw.GetAmmoCount();
            if (lastAmmo >= 0 && ammoNow < lastAmmo)
            {
                XRAPI.SendHapticVibration(hand, HAPTIC_SHOT_FRAMES);
            }
            lastAmmo = ammoNow;
        }
        catch { }

        Vector3 p;
        Quaternion r;
        if (!XRAPI.XR_GetControllerPose(hand, out p, out r)) return;

        // Pose arrives already normalised and already in Unity's frame - XRAPI.ToUnity
        // does the RH->LH swap once at ingest. Do not swap it again here.
        //
        // flipHands is NOT a handedness swap - it is a per-device controller
        // orientation quirk, so it stays here and is still correct to test.
        // Config only, no handsfix.txt - VRManager.flipHands is the toggle.
        // Converted pose is upside-down on Quest 3, so:
        // flipHands=false (Quest 3 default) = apply roll 180 to make it upright
        // flipHands=true  (Quest 2 / Pico) = leave it upside-down, that is the Quest 2 state
        // Roll, not yaw - yaw 180 flips aim backwards, roll flips gun upside-down only.
        if (!VRManager.flipHands) r = r * Quaternion.Euler(0f, 0f, 180f);

        Vector3 headWorld = Vector3.zero;
        Quaternion bodyRot = Quaternion.identity;
        bool foundHead = false;

        Camera[] all = Camera.allCameras;
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetComponent<VRHeadTracker>() != null)
                {
                    headWorld = all[i].transform.position;
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
            // Already converted at ingest - hPos is the lean directly
            Vector3 baseHead = refT.position + refT.up * 1.62f;
            headWorld = baseHead + refT.rotation * hPos;
        }
        // FIXED2: Use BODY rotation, not head rotation, for the gun.
        // p is head-relative (small) - VRRoot.TransformPoint(p) = feet + p = floor, invisible (that's why FIXED gave no hands).
        // headWorld + headWorldRot * p = correct height but head yaw moves gun (old bug).
        // headWorld (position only) + bodyRot * p = correct height AND head yaw does NOT move gun.
        if (VRManagerState.VRRoot != null) bodyRot = VRManagerState.VRRoot.transform.rotation;
        else if (Marine.LocalMarine != null) bodyRot = Quaternion.Euler(0f, Marine.LocalMarine.transform.eulerAngles.y, 0f);

        p = headWorld + bodyRot * p;
        r = bodyRot * r;

        Transform model = null;
        if (rw.WeaponModel != null) model = rw.WeaponModel.transform;
        if (model != null)
        {
            // Detach from WorldCamera (head) to VRRoot (body) so head doesn't drag it after LateUpdate.
            // Now p is headWorld-based, so this detach keeps it at eye height, not floor.
            if (model.parent != null) { Transform p2 = model.parent; while (p2 != null) { if (p2.GetComponent<VRHeadTracker>() != null) { model.SetParent(null, true); break; } p2 = p2.parent; } } // detach from head chain to scene root, NOT VRRoot - VRRoot is Destroy()ed on level change and would take gun with it
            model.position = p;
            model.rotation = r;
        }
        if (muzzle != null) muzzle.rotation = r;
    }
}
