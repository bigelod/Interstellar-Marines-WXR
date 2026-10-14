# Interstellar-Marines-WXR
An attempt to do a "monocular VR" 6DOF experience for **Interstellar Marines** on **Standalones** via **WinlatorXR**

Created with the help of Meta.AI

Thanks to Luboš, GmoLargey, and others for their work on [WinlatorXR](https://github.com/WinlatorXR/WinlatorXR) and everyone else who had contributed to portable PC gaming via Winlator on other platforms

The text below is also written with help of Meta.AI and will be manually improved as the project continues and behavior solidifies

### How it works
- `Helmet.InitFirstPersonCamera()` -> `VRHeadTracker` attached to Helmet base (not just WorldCamera) so visor + camera move together. Lean clamped to 12cm, no roomscale walking.
- `RangedWeapon.AttachToCamera()` -> `VRWeapon` puts weapon model at dominant hand, muzzle stays on body (anti-cheat safe, only rotation = hand rotation)
- `InputManager` patched - VR overrides block keyboard/mouse for mapped actions, ESC/menus still work
-  XrAPI UDP: listens `127.0.0.1:7872` (head + hands), sends `127.0.0.1:7278` `L_HAPTICS R_HAPTICS MODE_VR MODE_3D FOVX FOVY` for haptics + first-frame wakeup + 2D/VR auto switch
-  Red 
pixel quad top-left (10x10) flashing with `HMD_SYNC` frame ID for WinlatorXR pacing

Based on [WinlatorXR/SixDOFinator_SampleProject](https://github.com/WinlatorXR/SixDOFinator_SampleProject) and [HaloCEWXR docs](https://github.com/winlatorxr/halocewxr) for haptics/first-frame.

## Features
- 6DOF head (yaw/pitch/roll correct, RH->LH fixed)
- Hands in front, no mirror, `flipHands` config flag optional for Pico/Quest 2 roll bug
- Weapon model at controller, logical fire from body
- Right stick up/down cycles Slot01->Slot02->Slot03 loop
- Haptics on shoot (right controller 6-8 frames)
- Auto 2D when not in-game: title screen / paused / no local character = `MODE_VR=2` flat screen, in-game = `MODE_VR=1`
- Post-processing disabled for VR: motionBlur, chromaticAberration, depthOfField
- Keyboard/mouse blocked for mapped VR inputs (prevents double-move)

## Default Controls

| Action | Binding |
|---|---|
| Move | Left stick + left thumb directions |
| Turn | Right stick X (smooth turn, `turn_speed` in config) |
| Weapon Switch | Right stick Y up/down cycles Slot01-03 loop (0.35s cooldown) - starts on Slot01 |
| Jump | Right Grip |
| Shoot | Right Trigger |
| Interact / Use | Right A |
| Reload | Right B |
| Helmet Toggle | Left X |
| Flashlight | Left Y |
| Silencer double-tap | Left Trigger double tap |
| Fire mode hold | Left Trigger hold >0.6s |
| Crouch | Left Grip hold |
| Sprint | Left Thumb click |
| Menu / Mission Overview | Left Menu |

Mouse/Keyboard still works for unmapped: ESC, console, etc.

## Setup - Release

**Recommended user steps:**
1. Install Interstellar Marines (Steam) via WinlatorXR's store mode
2. Install **BepInEx 5** x86: download `BepInEx_win_x64_5.4.23.5.zip`, extract to game root (where `InterstellarMarines.exe` is). Ensure winhttp.dll is allowed to use native, built-in in Wine config, then run game once to generate `BepInEx/` folders. Close game.
3. Download latest release `InterstellarMarinesWXR.zip` from Releases
4. Extract release into game root - should place `BepInEx/plugins/IMVR/IMVRMod.dll` + `config.txt`
5. Launch via WinlatorXR on Quest/Pico: use DXVK + Turnip, set resolution 1400x1400.

`config.txt` next to dll:
> dominant_hand=right
>
> turn_speed=45
> 
> deadzone=0.20
> 
> flipHands=0


## Build Advice - For Contributors

You need DLLs from your Interstellar Marines install to compile (Unity / .NET 3.5):

1. Copy to `Libs/` (create folder if need be):
      - `InterstellarMarines_Data/Managed/Assembly-CSharp.dll`
      - `InterstellarMarines_Data/Managed/UnityEngine.dll`
      - `InterstellarMarines_Data/Managed/uLink.dll'
      - `BepInEx/core/BepInEx.dll` (generated after first run)
      - `BepInEx/core/0Harmony.dll` (generated after first run)

2. Open `IMVRMod.csproj` in VS2022/Rider, target .NET Framework 3.5, x86
3. Build -> `IMVRMod.dll`
4. Output to `BepInEx/plugins/IMVR/`

**Project files:**
- `Plugin.cs` - BepInEx entry, config load, Harmony patch, input runner
- `XRAPI.cs` - UDP 7872 receive + 7278 send (first frame + haptics + MODE_VR switch), file markers, `D:/xrtemp` fallback
- `VRHeadTracker.cs` - fixes double offset bug, HMD RH->LH `-x,-y,z`, visor lock, post-processing disable
- `VRWeapon.cs` - RH->LH `pos.z=-z, quat(-x,-y,z,w)` + HMD-local to world via HelmetBase parent
- `VRInput.cs` - maps bools to IM KeyBinds, Slot01-03 loop, blocks KB/mouse for mapped
- `Patches.cs` - Harmony patches for Helmet, RangedWeapon, InputManager IsInput/IsInputDown + GetAxis + SyncPixelQuad
- `SyncPixelQuad.cs` - red/black quad top-left flashing with `frameId %2` (HMD_SYNC)
