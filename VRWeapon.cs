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
        // Read live rather than from rightDominant, so a dominant_hand change in
        // config.txt moves the buzz to the same hand VRHands is now posing.
        int hand = VRHands.DominantIndex;

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

        // The whole head/body/hand transform chain lives in VRHands now, because the
        // interaction ray has to come out of exactly the same frame as the gun - see
        // VRHandInteract.cs. Nothing about the maths changed when it moved.
        Vector3 p;
        Quaternion r;
        if (!VRHands.GetDominantPose(out p, out r)) return;

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
