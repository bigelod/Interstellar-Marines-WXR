using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public static class VRInput
{
    public static Dictionary<string, bool> overridesBool = new Dictionary<string, bool>();
    static float lastLeftTap = -10f;
    static bool leftTrigHeld = false;
    static float leftHoldTime = 0f;
    static float deadzone = 0.2f;

    static bool aHeld = false;
    static bool bHeld = false;
    static bool xHeld = false;
    static bool yHeld = false;

    static float holdTriggerCooldown = 0f;

    static int currentSlot = 1;
    static bool slotInitialized = false;
    static float lastSlotSwitch = -10f;
    static float slotCooldown = 0.35f;
    static bool rightStickUpHeld = false;
    static bool rightStickDownHeld = false;

    public static void SetConfig(float dz) { deadzone = dz; }

    public static void UpdateFrame()
    {
        uint lb, rb;
        Vector2 ls, rs;
        float lt, rt, lg, rg;
        XRAPI.XR_GetControllerState(XRAPI.LEFT, out lb, out ls, out lt, out lg);
        XRAPI.XR_GetControllerState(XRAPI.RIGHT, out rb, out rs, out rt, out rg);
        XRAPI.State state;
        XRAPI.GetBoolState(out state);

        if (Mathf.Abs(ls.x) < deadzone) ls.x = 0;
        if (Mathf.Abs(ls.y) < deadzone) ls.y = 0;
        if (Mathf.Abs(rs.x) < deadzone) rs.x = 0;
        if (Mathf.Abs(rs.y) < deadzone) rs.y = 0;

        overridesBool.Clear();

        Set("MoveForward", ls.y > deadzone || state.lThumbUp);
        Set("MoveBackward", ls.y < -deadzone || state.lThumbDown);
        Set("StrafeLeft", ls.x < -deadzone || state.lThumbLeft);
        Set("StrafeRight", ls.x > deadzone || state.lThumbRight);

        Character local = null;
        if (Character.AllCharacters != null)
        {
            foreach (Character c in Character.AllCharacters)
            {
                if (c != null && c.IsLocallyControlled) { local = c; break; }
            }
        }
        if (local != null && Mathf.Abs(rs.x) > 0.01f)
        {
            float speed = VRManager.turnSpeed;
            local.transform.Rotate(0, rs.x * speed * Time.deltaTime, 0);
        }

        //Set("Next", rs.y > 0.7f || state.rThumbUp);
        //Set("Previous", rs.y < -0.7f || state.rThumbDown);

        if (!slotInitialized)
        {
            currentSlot = 1;
            Set("Slot01", true);
            slotInitialized = true;
            lastSlotSwitch = Time.time;
        }

        // NEW: Right thumbstick up/down cycles Slot01-03 in loop
        bool stickUp = rs.y > 0.7f || state.rThumbUp;
        bool stickDown = rs.y < -0.7f || state.rThumbDown;

        if (Time.time - lastSlotSwitch > slotCooldown)
        {
            if (stickUp && !rightStickUpHeld)
            {
                // Up = next slot: 1->2, 2->3, 3->1
                currentSlot++;
                if (currentSlot > 3) currentSlot = 1;
                SetSlot(currentSlot);
                lastSlotSwitch = Time.time;
                Debug.Log("[IMVR] Slot up -> Slot0" + currentSlot);
            }
            else if (stickDown && !rightStickDownHeld)
            {
                // Down = prev slot: 1->3, 3->2, 2->1
                currentSlot--;
                if (currentSlot < 1) currentSlot = 3;
                SetSlot(currentSlot);
                lastSlotSwitch = Time.time;
                Debug.Log("[IMVR] Slot down -> Slot0" + currentSlot);
            }
        }
        rightStickUpHeld = stickUp;
        rightStickDownHeld = stickDown;

        Set("Jump", state.rGrip || rg > 0.7f);
        Set("GamepadJump", state.rGrip || rg > 0.7f);

        Set("Fire", state.rTrigger || rt > 0.5f);

        Set("Use", state.rA && !aHeld);
        Set("GamepadUse", state.rA && !aHeld);
        Set("Reload", state.rB && !bHeld);
        Set("GamepadReload", state.rB && !bHeld);
        Set("ToggleHelmet", state.lX && !xHeld);
        Set("ToggleFlashlight", state.lY && !yHeld);

        aHeld = state.rA;
        bHeld = state.rB;
        xHeld = state.lX;
        yHeld = state.lY;

        if (state.lTrigger && !leftTrigHeld)
        {
            if (Time.time - lastLeftTap < 0.35f)
            {
                Set("ToggleSilencer", true);
                lastLeftTap = -10f;
            }
            else
            {
                lastLeftTap = Time.time;
            }
            leftTrigHeld = true;
        }

        if (!state.lTrigger) { leftTrigHeld = false; Set("ToggleSilencer", false); }

        if (state.lTrigger)
        {
            leftHoldTime += Time.deltaTime;
            if (leftHoldTime > 0.6f && holdTriggerCooldown <= 0f)
            {
                Set("RateOfFire", true);
                holdTriggerCooldown = 1f;
            }
            else
            {
                Set("RateOfFire", false);
            }

            if (holdTriggerCooldown >= 0f)
            {
                holdTriggerCooldown -= Time.deltaTime;
            }
        }
        else
        {
            leftHoldTime = 0f;
            holdTriggerCooldown = 0f;
            Set("RateOfFire", false);
        }

        Set("Crouch", state.lGrip);
        Set("GamepadStance", state.lGrip);
        Set("Run", state.lThumbPress);
        Set("GamepadSprint", state.lThumbPress);
        Set("GamepadMenu", state.lMenu);
        Set("MissionOverview", state.lMenu);

        // Update VR/2D mode based on game state (title/paused = 2D)
        try { XRAPI.UpdateVRModeFromGameState(); } catch { }

    }

    static void Set(string name, bool val) { overridesBool[name] = val; }

    static void SetSlot(int slot)
    {
        Set("Slot01", false);
        Set("Slot02", false);
        Set("Slot03", false);
        Set("Slot0" + slot, true);
        currentSlot = slot;
    }
}

public class VRInputRunner : MonoBehaviour
{
    void Update() { VRInput.UpdateFrame(); }
}
