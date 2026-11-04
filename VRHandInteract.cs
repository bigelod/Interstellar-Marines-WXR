using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Interact with what your HAND is pointing at, not what your head is pointing at.
//
// Both halves of the game's interaction read Camera.main:
//
//   Marine.ProcessInput  - the Use action itself. Inline in a very large method:
//       GameObject gameObject = Camera.main.gameObject;
//       Physics.Raycast(gameObject.transform.position, gameObject.transform.forward,
//                       out hit, this.pushButtonRange, this.ButtonLayerMask)
//   GUIInteracter.UpdateRaycast - the HUD brackets and the "[E] Open" hint, same ray
//       plus a 0.25 m SphereCastAll fallback that only opens the brackets.
//
// In flat play the crosshair IS the head, so one ray serves both. In VR the head is a
// free-moving camera and reaching for a wall panel while watching the corridor is the
// normal thing to do, so the ray has to come off the hand.
//
// ===== WHY IT IS DONE THIS WAY =====
// GUIInteracter.UpdateRaycast is a method, so it is simply replaced. The Use action is
// not - it is a dozen lines inlined in the middle of ProcessInput, and reaching it would
// mean a transpiler against a method that half this mod already patches by name. So
// instead: the Use EDGE is withheld from the game in VR (see InputManager_IsInputDown_
// Patch) and this file performs the toggle, replicating ProcessInput's branch exactly -
// Toggle on a direct UseButton, ToggleRoot on a root one or on an OwnedCollider's owner.
//
// The Use LEVEL still goes through untouched, which matters: Marine clears
// buttonBeingHeldDown when IsInput(Use) goes false, so hold-to-use panels still release
// correctly without this file knowing anything about them.
//
// Both paths fall back to the head camera when there is no hand pose - no packets yet,
// or no VR frame to place the hand in - so interaction never becomes unreachable.
public static class VRHandInteract
{
    // True when the ray came from the hand; false when it fell back to the head. Only
    // used for logging the first transition.
    static bool lastFromHand = false;
    static bool loggedSource = false;

    // Where the interaction ray starts and which way it points. The hand pose comes from
    // VRHands, which is the same frame the weapon model is placed in - so the ray leaves
    // the muzzle, and what the player sees the gun pointing at is what they interact with.
    public static bool GetRay(out Vector3 origin, out Vector3 direction)
    {
        origin = Vector3.zero;
        direction = Vector3.forward;

        if (VRManagerState.inVR)
        {
            Vector3 p;
            Quaternion r;
            if (VRHands.GetDominantPose(out p, out r))
            {
                origin = p;
                direction = r * Vector3.forward;
                Note(true);
                return true;
            }
        }

        Camera head = VRHands.FindHeadCamera();
        if (head == null) head = Camera.main;
        if (head == null) return false;
        origin = head.transform.position;
        direction = head.transform.forward;
        Note(false);
        return true;
    }

    static void Note(bool fromHand)
    {
        if (loggedSource && fromHand == lastFromHand) return;
        lastFromHand = fromHand;
        loggedSource = true;
        try { VRManagerLogger.LogInfo("[IMVR] Interaction ray source -> " + (fromHand ? "dominant hand" : "head camera (no hand pose)")); } catch { }
    }

    // Called from VRInput on the rising edge of the Use button, because the game's own
    // edge is withheld in VR. Replicates Marine.ProcessInput's Use branch against the
    // hand ray; anything it does not match is simply not a button, exactly as before.
    public static void OnUsePressed()
    {
        if (!VRManagerState.inVR) return;
        try
        {
            Marine m = Marine.LocalMarine;
            if (m == null) return;

            Vector3 origin, dir;
            if (!GetRay(out origin, out dir)) return;

            RaycastHit hit;
            if (!Physics.Raycast(origin, dir, out hit, m.PushButtonRange, m.ButtonLayerMask)) return;

            UseButton b = hit.collider.transform.GetComponent<UseButton>();
            if (b != null) { b.Toggle(m); return; }

            if (hit.collider.transform.root == null) return;

            b = hit.collider.transform.root.GetComponent<UseButton>();
            if (b != null) { b.ToggleRoot(m); return; }

            // A squadmate: the collider is owned by someone else's Marine, and the
            // UseButton lives on that Marine rather than on the collider.
            OwnedCollider oc = hit.collider.gameObject.GetComponent<OwnedCollider>();
            if (oc == null || oc.Owner == null) return;
            Marine other = oc.Owner.GetComponent<Marine>();
            if (other == null) return;
            b = other.GetComponent<UseButton>();
            if (b != null) b.ToggleRoot(m);
        }
        catch { }
    }
}

// Replaces GUIInteracter.UpdateRaycast so the brackets and the hint text track the hand
// ray rather than Camera.main. Keeping the display on the same ray as the action is not
// cosmetic: the brackets closing is the only feedback the player has that the thing they
// are pointing at will actually respond to a press.
//
// The original's shape is preserved exactly - a direct raycast sets raycastHit (brackets
// closed, hint shown), and a 0.25 m SphereCastAll fallback sets sphereCastHit (brackets
// open, no hint). GetUseButton is the game's own, called by reflection, so all of its
// door-power and team-mate rules come along unchanged.
[HarmonyPatch(typeof(GUIInteracter), "UpdateRaycast")]
class GUIInteracter_VR_HandRay_Patch
{
    // The original's own fallback radius, kept so the brackets open at the same
    // tolerance they do in flat play.
    const float SPHERE_RADIUS = 0.25f;

    static MethodInfo getUseButton;
    static bool resolved = false;

    static bool Prefix(GUIInteracter __instance)
    {
        if (!VRManagerState.inVR) return true;

        if (!resolved)
        {
            resolved = true;
            getUseButton = AccessTools.Method(typeof(GUIInteracter), "GetUseButton", new System.Type[] { typeof(Collider) });
            if (getUseButton == null) VRManagerLogger.LogWarn("[IMVR] GUIInteracter.GetUseButton not found - brackets stay on the head ray");
        }
        // Without the game's own filter this would light the brackets on used-up buttons
        // and powerless doors, so fall back to the stock method rather than guess.
        if (getUseButton == null) return true;

        Vector3 origin, dir;
        if (!VRHandInteract.GetRay(out origin, out dir)) return true;

        try
        {
            Marine m = Marine.LocalMarine;
            if (m == null) return true;
            LayerMask mask = m.ButtonLayerMask;
            float range = m.PushButtonRange;

            Traverse t = Traverse.Create(__instance);
            UseButton found = null;
            bool direct = false;
            bool sphere = false;

            bool missed = true;
            RaycastHit hit;
            if (Physics.Raycast(origin, dir, out hit, range, mask))
            {
                UseButton b = getUseButton.Invoke(__instance, new object[] { hit.collider }) as UseButton;
                if (b != null && !b.used) { found = b; direct = true; missed = false; }
            }

            if (missed)
            {
                RaycastHit[] all = Physics.SphereCastAll(origin, SPHERE_RADIUS, dir, range, mask);
                for (int i = 0; i < all.Length; i++)
                {
                    UseButton b = getUseButton.Invoke(__instance, new object[] { all[i].collider }) as UseButton;
                    if (b != null && !b.used) { found = b; sphere = true; }
                }
            }

            t.Field("hitButton").SetValue(found);
            t.Field("raycastHit").SetValue(direct);
            t.Field("sphereCastHit").SetValue(sphere);
        }
        catch
        {
            return true;
        }
        return false;
    }
}
