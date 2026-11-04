using UnityEngine;

// Where the dominant hand is, in world space, in the frame the weapon already uses.
//
// This logic lived inside VRWeapon.LateUpdate and nowhere else, which was fine while the
// gun was the only thing that cared. Interaction now needs the same ray - pointing at a
// door panel and having it open has to agree with where the gun is visibly pointing, or
// the player is aiming with one frame and interacting with another. Two copies of a
// transform chain is exactly the "measure offsets in the frame you will use them in"
// trap from Docs/general_wxr/CLAUDE.md, so there is one copy and both callers use it.
//
// The chain, unchanged from VRWeapon:
//   pose from XRAPI (already normalised, already handedness-swapped at ingest)
//   -> flipHands roll correction (a per-device controller quirk, NOT a handedness swap)
//   -> offset by hand_height
//   -> rotated by BODY yaw, not head yaw, and anchored at the head's world position
//
// Body rotation is deliberate and is the single most important line here: using head
// rotation makes the gun swing whenever the player looks around, which is the bug
// Docs/general_wxr/TIPS.md cites by name from this project.
public static class VRHands
{
    public static int DominantIndex
    {
        get { return (VRManager.configDominant == "right") ? XRAPI.RIGHT : XRAPI.LEFT; }
    }

    // The VR camera is identified by its tracker component rather than by name, because
    // the Helmet rebuilds it on every spawn and level load.
    public static Camera FindHeadCamera()
    {
        Camera[] all = Camera.allCameras;
        if (all == null) return null;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].GetComponent<VRHeadTracker>() != null) return all[i];
        }
        return null;
    }

    // False when there is no pose to speak of - no packets yet, or no frame to place the
    // hand in. Callers fall back to the head; they must not use a zero pose, which would
    // put the hand at the world origin.
    public static bool GetDominantPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        Vector3 p;
        Quaternion r;
        if (!XRAPI.XR_GetControllerPose(DominantIndex, out p, out r)) return false;

        // Pose arrives already normalised and already in Unity's frame - XRAPI.ToUnity
        // does the RH->LH swap once at ingest. Do not swap it again here.
        //
        // flipHands is NOT a handedness swap - it is a per-device controller orientation
        // quirk. Roll, not yaw: yaw 180 would aim backwards, roll only flips the model.
        // A roll about the local Z axis leaves forward alone, so the interaction ray is
        // the same whichever way this falls.
        if (!VRManager.flipHands) r = r * Quaternion.Euler(0f, 0f, 180f);

        Vector3 headWorld = Vector3.zero;
        bool foundHead = false;

        Camera head = FindHeadCamera();
        if (head != null) { headWorld = head.transform.position; foundHead = true; }

        if (!foundHead && VRManagerState.VRRoot != null)
        {
            Transform refT = VRManagerState.VRRoot.transform;
            Vector3 hPos = Vector3.zero;
            Quaternion hRot = Quaternion.identity;
            XRAPI.XR_GetHeadPose(out hPos, out hRot);
            // Already converted at ingest - hPos is the lean directly.
            headWorld = refT.position + refT.up * 1.62f + refT.rotation * hPos;
            foundHead = true;
        }

        // No head and no VRRoot means no frame to place the hand in. Returning a pose
        // anchored at (0,0,0) is worse than returning nothing.
        if (!foundHead) return false;

        Quaternion bodyRot = Quaternion.identity;
        if (VRManagerState.VRRoot != null) bodyRot = VRManagerState.VRRoot.transform.rotation;
        else if (Marine.LocalMarine != null) bodyRot = Quaternion.Euler(0f, Marine.LocalMarine.transform.eulerAngles.y, 0f);

        // hand_height lifts the weapon with the eye, so raising the camera does not leave
        // the gun down by the chest where the body occludes it.
        pos = headWorld + bodyRot * (p + new Vector3(0f, VRManager.handHeight, 0f));
        rot = bodyRot * r;
        return true;
    }
}
