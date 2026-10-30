# PROJECT_PLAN.md — Interstellar Marines VR Mod (IMVRMod)

## Overview
Mod **Interstellar Marines** (Unity 4, C# 3, BepInEx) for VR — Meta Quest 3 primary, Quest 2 / Pico secondary.

Testing is purely on PC (not on-device) using **XrUDP Tester** for head/controller data. The mod is loaded as `IMVRMod.dll` with a `config.txt` next to the DLL containing `flipHands`, `hudScale`, `vrResolution`, etc.

## Goal
Make flat Interstellar Marines playable in VR without breaking the game's helmet fantasy:

- The player wears the marine helmet in-game. In flat mode the helmet visor is a neon green holographic HUD that shuts down / reboots on damage and affects hearing/visuals. In VR the headset *is* the visor — so the HUD must be glued to the VR camera like the helmet visor in flat mode, while preserving helmet gameplay (damage shutdown, reboot, audio muffling, visor open/closed).
- VR head tracking, VR gun/hand pose, VR input blocking, and a sync pixel for tracking must all work together on the same `WorldCamera`.

## Core Systems

### 1. Head Tracking — `VRHeadTracker.cs` / `XRAPI.cs`
- Sets `WorldCamera` position/rotation from XR head pose each frame.
- `XRAPI.cs` `XR_GetHeadPose` falls back to `Input.GetAxis("Mouse X")` if no XR data — that fallback is what the mod replaces.
- `WorldCamera` is depth 0, parent `Helmet(Clone)`, has `VRHeadTracker`, `SyncPixelQuad`.

### 2. Sync Pixel — `SyncPixelQuad.cs`
- A red quad in the top-left corner of the render target for external tracking (Winlator / XrUDP).
- `syncLayer = 31`, `cullingMask = 1 << 31`, `syncPixelSize` fixed to be visible on 3840-wide render targets.

### 3. VR Gun / Hands — `VRWeapon.cs`
- Gun pose comes from the right controller.
- Raw controller pose is upside-down on Quest 3 vs Quest 2/Pico — handled by `flipHands` config.
- Must not double-count head pose — `RangedWeapon.AttachToCamera()` parents `WeaponModel` to `WorldCamera` (head-driven), so the mod must detach and re-parent.

### 4. Helmet / HUD — `Patches.cs` + `HUDTreeDumper.cs`
- **Helmet:** Physical helmet `CH_Marine1pHelmet` / `CH_Marine1pHelmetGlass` (layer 8, children of `Helmet -> Root -> Helmet`) sits below the VR camera in VR — a black disc — and must be hidden, *not* the HUD glass.
- **Neon HUD:** `CH_Marine1pHelmetGlass_HUD` (layer 0, children meshes layer 14, mats `CH_1pHelmet_HUD_NGUI` / `CH_1pHelmet_HUD_NGUI_Reflection`, `Animator`, `currentVisorState=Closed`, `helmetAnimation=helmetClose`) is the neon HUD surface.
- **Pipeline:** NGUI UI (`CAM Camera` depth 1, culling 65536, parent `Fake 3D UI Root` at 516,2,0; `HUD UI Root` at 501,2,0) is rendered by `HUDCamera` (depth 1, culling 1568/65536, target `CleanToBlendTarget` 1280x720 / 1400x1080) via `doubleGlassMaterial` + `bendMaterial` onto the glass mesh, which is rendered by `HelmetCamera` (depth 1, culling 16384 UIOverlay, parent `WorldCamera`).
- **Menus:** `SettingsMenuCamera` depth 6, `SettingsVideoMenuCamera` depth 7, `MainMenuCamera` depth 5, `LobbyCamera` depth 8 — all culling 32 (UI), layer 5, pos (-1000,22,0) — separate from NGUI HUD and must stay full-screen.
- **Dumper:** `HUDTreeDumper.cs` — dumps GameObject tree / cameras / fields to log + `hud_tree.txt` next to DLL on F9 (ignores `verboseDebug` for F9, `HUDTreeDumpRunner` with `DontDestroyOnLoad`).

### 5. Input Blocking — `Patches.cs`
- In VR gameplay, mouse/keyboard must be disabled — the game is driven by XR controllers only.
- Menus / paused (`LocalMarine==null` or `GameManager.IsPaused`) must still allow mouse/keyboard.

## Config
`config.txt` next to DLL:
```
flipHands=false   # false = Quest 3 upright, true = Quest 2/Pico upside-down
hudScale=0.65     # HUD UI Root scale (currently reverted to 1.0 to restore visibility — see CURRENT_PROGRESS)
vrResolution=1400  # desired square resolution for Quest 3 — not yet safely implemented
```

## Architecture
- `Patches.cs` — main Harmony patches: `Helmet.InitFirstPersonCamera`, `HUDCamera`, `VRHUDAnchor`, `VRGlassAnchor`, input blocking, `VRVisualTweaks`.
- `VRHeadTracker.cs`, `VRWeapon.cs`, `SyncPixelQuad.cs`, `HUDTreeDumper.cs`, `XRAPI.cs` — standalone components added to `WorldCamera` / `Helmet`.
