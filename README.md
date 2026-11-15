# Interstellar-Marines-WXR
An attempt to do a "monocular VR" 6DOF experience for **Interstellar Marines** on **Standalones** via **WinlatorXR**.

Created with the help of Meta.AI and Claude Code Opus 5 (Medium effort) and OpenCode GO DeepSeek V4.1 Flash.

Thanks to Luboš, GmoLargey, and others for their work on [WinlatorXR](https://github.com/WinlatorXR/WinlatorXR) and everyone else who has contributed to portable PC gaming via Winlator on other platforms.

The port runs the game fullscreen in the container, so no window-chrome handling is needed. The mod targets **XrApi 0.4**, **mono** (`MODE_3D = 0`), and keeps the game's own 32-bit Unity 5.6.7 renderer.

## How it works

- `Helmet.InitFirstPersonCamera()` -> `VRHeadTracker` is attached to `WorldCamera`. Head position and rotation come from the HMD; lean is clamped to **0.30 m**, no roomscale walking. The eye offset (`eye_height`, `eye_forward`) is applied by the tracker so it is live-tunable.
- `RangedWeapon.AttachToCamera()` -> `VRWeapon` places the weapon from the dominant hand. The frame is built in `VRHands`: **head position for height, body rotation for orientation**, so looking around does not swing the gun. Only the muzzle rotation follows the hand; the logical shot still originates from the body.
- `InputManager` and six raw `UnityEngine.Input` methods are patched. `VRInputGate` blocks mouse/keyboard **only in VR gameplay**; menus and pause still allow native input so uGUI buttons and ESC work.
- XrApi UDP: listens on **127.0.0.1:7872** (or 7873, whichever is free — one socket, not both), and sends **127.0.0.1:7278** as `L_HAPTICS R_HAPTICS MODE_VR MODE_3D FOVX FOVY`.
- Handshake: writes `Z:/tmp/xr/version` (`0.4`) and `vr` (`VR`) unconditionally, repairs them if the runtime restarts and wipes the directory, and reads `system` to auto-detect the headset.
- `MODE_VR=1` in gameplay, `MODE_VR=0` in menus/pause. `0` is deliberate: it stops the unused pose stream, restores the runtime's own mouse control, and leaves immersive presentation so a whole menu is readable at once.
- Red sync pixel: a **10x10** immediate-mode `GL` rect in the top-left, drawn by the mod's own `DontDestroyOnLoad` overlay camera at depth 1000. No texture, blending, filtering, or gamma correction — the runtime reads the exact red value.
- HUD: the neon visor is the game's bent-glass pipeline. `VRGlassAnchor` glues the glass mesh to the VR head, preserving its Animator pose; `VRHudSafeArea` shrinks the uGUI content toward the centre; the physical helmet shell is hidden.
- Performance split: `VRWorldScaler` renders only `WorldCamera` at `world_render_scale` (default 0.5), composites that texture to screen as a rollable quad, and leaves `HelmetCamera` / `CAM Camera` at full window resolution. The world pays for 640x480; the HUD text does not.
- Occlusion: this game's culling switches room lights off (`ToggleOcclusion.OnBecameInvisible`), not just draw calls. `VRClipGuard` forces occlusion culling off while the camera is inside geometry and keeps `far_clip` steady.

## Features

- **6DOF head tracking** with yaw/pitch/roll; lean clamped to 0.30 m. With the low-res composite active, head roll is applied to the **world texture** (XOPS-WXR style): the world rolls and the visor HUD stays upright. The gun is counter-rotated so it stays fixed on screen with the HUD (`hands_roll=1`, default); set `hands_roll=0` for XOPS's gun-rolls-with-the-world variant. `head_roll=0` removes roll; `roll_sign` flips its direction.
- **Weapon at the dominant hand**, body-relative orientation, survives level changes.
- **`flipHands=auto`** — reads `Z:/tmp/xr/system` and maps `EUREKA`/`PANTHER` -> Quest 3, `HOLLYWOOD` -> Quest 2, `SEACLIFF` -> Quest Pro. An explicit `flipHands=true/false` overrides detection.
- **Haptics on the dominant hand**, driven by the ammo counter: 6 frames when a round is spent, 10 frames on a weapon switch. A dry trigger or a refused shot does not buzz.
- **Auto 2D/VR switching**: title screen, menus, pause, or no local Marine -> `MODE_VR=0`; in-game -> `MODE_VR=1`.
- **HUD scaling** via `VRHudSafeArea` (`hudScale`, default 0.60), which moves edge-anchored widgets inward instead of pinning them.
- **HUD text supersampling** (`hud_supersample`, default 1.1 — the game's own value) via `HUDCamera.hudScale`. The low-res world already makes the HUD about 2x sharper by comparison; 2.0 is available if the text still reads soft.
- **Low-res world, full-res HUD** (`world_render_scale` default 0.5, or fixed `world_width`/`world_height`): `WorldCamera` renders into a smaller target inside the 1280x960 window; a depth-0.5 camera composites it (rotated by head roll when enabled) and upscales it to the display, while the HUD/visor cameras still render at the full window resolution. The VR Settings **Camera res** row offers 480p/640x480/800x600/960x720/1024x768/1280x960 or window/off; **Overscan** enlarges the quad (default 1.25) to hide roll corners at the cost of cropping the world edges. `1.0`/off disables the mechanism. This is the XOPS-WXR split.
- **Full-res weapon over a low-res world**: while the composite is active, the gun moves to its own layer and a full-window camera (same pose/FOV as `WorldCamera`) draws it after the world quad. The world/body stay at the native target; the gun stays sharp. `gun_fullres=0` puts it back in the world pass.
- **Sync pixel** (10 px) for WinlatorXR frame pacing.
- **In-headset tuning menu**: pause, click **VR Settings**. Every knob is a row and every change is written back to `config.txt`.
- **Comfort**: sprint bob removed, head recoil/lurch/visor bob suppressed in VR, blinders-on-damage replaced with peripheral red bands.
- **Performance**: only the helmet lamp casts shadows; AA/TAA, ambient occlusion, and depth of field are forced off; Bloom is deliberately preserved because it is part of the HUD bend. The allocating periodic scans (post-FX, HUD safe area, light sweep) are staggered so they do not share a frame.
- **Keyboard/mouse blocked only in VR gameplay**; menus, pause, ESC, and the console still work.

## Default controls

| Action | Binding |
|---|---|
| Move | Left stick, or left thumb directions |
| Turn | Right stick X (smooth, `turn_speed`) |
| Weapon switch | Right stick Y up/down, cycles Slot01-03 (0.35 s cooldown) |
| Fire | Right trigger |
| Jump | Right grip |
| Interact / Use | Right A (hand ray) |
| Reload | Right B |
| Crouch | Left grip (hold) |
| Sprint | Left thumb click |
| Helmet toggle | Left X |
| Flashlight | Left Y (press to toggle) |
| Silencer | Left trigger double-tap (within 0.35 s) |
| Fire mode | Left trigger hold > 0.6 s |
| Menu / Mission Overview | Left menu |
| Recentre | Right thumb click |
| **Tuning menu** | **Pause -> click "VR Settings"** |

`Aim` (iron sights) is currently **unbound and blocked**, so ADS is unreachable in VR. `ThrowGrenade`, `ToggleLaser`, `Slot04`, `LeanLeft`, and `LeanRight` are also unbound. These are deliberate gaps pending an owner decision, not silent bugs.

## Setup — Release

1. Install Interstellar Marines (Steam) via WinlatorXR's store mode.
2. Install **BepInEx 5** x86: download `BepInEx_win_x64_5.4.23.5.zip`, extract to the game root (where `InterstellarMarines.exe` is). Ensure `winhttp.dll` is allowed to use native, built-in Wine config, then run the game once to generate the `BepInEx/` folders. Close the game.
3. Download the latest release `InterstellarMarinesWXR.zip` from Releases.
4. Extract the release into the game root — it should place `BepInEx/plugins/IMVR/IMVRMod.dll` and `config.txt`.
5. Launch via WinlatorXR on Quest/Pico: use DXVK + Turnip, fullscreen, and a resolution you are happy with (1280x960 is the current default; 1400x1400 is also common).

`config.txt` lives next to the DLL and is re-read every 2 seconds. The shipped defaults are:

```ini
dominant_hand=right
turn_speed=80
deadzone=0.20
flipHands=auto
hudScale=0.60
screen_fov=90
render_width=1280
render_height=960
world_render_scale=0.5
hud_supersample=1.1
near_clip=0.05
far_clip=45
occlusion_culling=1
sync_pixel_size=10
```

Every key, with comments and safe ranges, is in the shipped `config.txt`.

## Diagnostics

- **F9** dumps the GameObject tree, cameras, and selected fields to `hud_tree.txt` next to the DLL. It deliberately ignores the verbose flag, so the useful dump always works.
- The first line of the BepInEx log is a **build stamp** (`RELEASE`/`DEBUG` plus the assembly write time). If a change appears to do nothing, check this line first.
- `UpdateVRModeFromGameState` logs a one-line signature on every mode transition and names which signal moved (`localChar`, `runningGame`, `marine`, `alive`, `gmPaused`, `timeScale`, `uiWantsPointer`).
- `system` detection logs the make, product codename, mapped model, and chosen `flipHands`.
- `VRWorldScaler` logs the world render-target size on creation and on every resize.

## Build advice — contributors

You need DLLs from your Interstellar Marines install to compile (Unity / .NET 3.5):

1. Copy to `Libs/` (create it if needed):
   - `InterstellarMarines_Data/Managed/Assembly-CSharp.dll`
   - `InterstellarMarines_Data/Managed/UnityEngine.dll`
   - `InterstellarMarines_Data/Managed/uLink.dll`
   - `BepInEx/core/BepInEx.dll` (generated after first run)
   - `BepInEx/core/0Harmony.dll` (generated after first run)
2. Open `IMVRMod.csproj` in VS2022/Rider. The project is `net35`, `LangVersion 3`, x86.
3. Build -> `IMVRMod.dll`.
4. Output to `BepInEx/plugins/IMVR/`.

`Directory.Build.props` defaults the configuration to **Release**, so a bare `dotnet build` does not quietly produce a Debug DLL for the headset.

### Project files

- `Plugin.cs` — BepInEx entry, config load/poll, build stamp, resolution, Harmony patch-all, input runner, tuning menu.
- `XRAPI.cs` — XrApi 0.4 handshake (`version`, `vr`, `system` detection), UDP receive/send, pose conversion, haptics, mode switching.
- `VRHeadTracker.cs` — head pose on `WorldCamera`, lean clamp, `VRClipGuard`.
- `VRHands.cs` — the single dominant-hand world pose shared by the weapon and the interaction ray.
- `VRWeapon.cs` — weapon model placement and ammo-driven haptics.
- `VRHandInteract.cs` — hand-ray interaction and the Use toggle.
- `VRInput.cs` / `VRInputGate.cs` — XrApi buttons/sticks to `KeyBind` overrides, and the single native-input gate.
- `VRHudSafeArea.cs` — uGUI content shrink that does not touch the bend pipeline.
- `SyncPixelQuad.cs` — global overlay camera and red `GL` sync pixel.
- `VRPainOverlay.cs` — suppresses the game's per-camera red flash, draws peripheral bands once.
- `VRClipGuard.cs` — culling-off-inside-geometry, steady far clip, fog.
- `VRWorldScaler.cs` — low-res world render target + full-screen blit, with the HUD left at window resolution.
- `VRPostFX.cs` / `VRLightTamer.cs` / `VRHeadLamp.cs` — surgical performance work.
- `VRConfigFile.cs` / `VRTuneMenu.cs` / `VRFovTuner.cs` — live tuning.
- `Patches.cs` — Harmony patches plus `VRGlassAnchor` and `VRVisualTweaks`.
- `HUDTreeDumper.cs` — F9 scene/camera dump.

## Documentation

- `Docs/general_wxr/` — the game-agnostic WinlatorXR rules. Read `CLAUDE.md` before planning.
- `Docs/MetaAI/` — **archived** session history, including the HUD dead ends. Best record of *why*, not current fact.
- `Docs/ClaudeCode/` — **archived** 30 Oct audit through the 3 Nov device tests. Useful history; check the OpenCode notes for the current delta.
- `Docs/OpenCode/DeepSeek41Flash/` — current reconciliation, handshake record, code map, and ranked next work.
