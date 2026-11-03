using UnityEngine;

// The single decision for "should the game's own mouse and keyboard pass through
// untouched this frame". Every input-blocking patch asks this, so the answer cannot
// drift between them - it used to be a three-line preamble copy-pasted nine times, and
// the Cursor.visible bug survived as long as it did partly because one copy differed.
public static class VRInputGate
{
    public static bool AllowNative()
    {
        if (!VRManagerState.inVR) return true;
        if (Marine.LocalMarine == null) return true;
        try { if (GameManager.IsPaused) return true; } catch { }

        // ===== WHY GameUI AND NOT Cursor.visible =====
        // The game decides cursor lock from exactly these two, in GlobalManager:
        //     Screen.lockCursor = !IsShowingMouseBlockingGUI && !IsShowingModalGUI
        // so they are its own authoritative "a UI wants the pointer" signal. Unlike
        // Cursor.visible - which is permanently true under Winlator and caused the
        // MODE_VR=0 bug - these are driven by the game's UI state and are trustworthy.
        //
        // This matters more than it looks: uGUI buttons are clicked through the
        // EventSystem's StandaloneInputModule, which polls Input.GetMouseButton and
        // Input.GetMouseButtonDown. Blocking those makes every menu unclickable. That
        // went unnoticed while MODE_VR was pinned to 0 by the Cursor.visible bug, because
        // inVR was false almost always and the blockers never actually ran. Fixing the
        // mode bug switched them on for the first time and the menus went dead with it.
        try
        {
            GameUI ui = GameUI.Instance;
            if (ui != null && (ui.IsShowingMouseBlockingGUI || ui.IsShowingModalGUI)) return true;
        }
        catch { }

        return false;
    }
}
