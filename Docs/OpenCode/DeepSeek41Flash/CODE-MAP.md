# CODE-MAP.md — how IMVRMod runs, at commit `17a5f92`

A reading map, not a tutorial. Where this disagrees with `Docs/MetaAI/`, the
source wins; where it disagrees with `Docs/ClaudeCode/`, the source at this
commit wins, because that audit predates three commits.

## File roles

| File | Role |
|---|---|
| `Plugin.cs` | BepInEx entry point, config parsing, build stamp, resolution, UDP init, Harmony patch-all, global sync pixel, input runner, tuning menu |
| `XRAPI.cs` | XrApi **0.4** client: handshake, receive thread, parse, pose accessors, outbound packet, haptics, mode switching |
| `Patches.cs` | Every Harmony patch **and** `VRGlassAnchor` / `VRHUDAnchor` / `VRVisualTweaks` / `VRInputRunner` |
| `VRHeadTracker.cs` | Applies head pose to `WorldCamera`, parks `VRRoot` on the Marine, calls `VRClipGuard` |
| `VRHands.cs` | Single source of the dominant-hand world pose, shared by gun and interaction |
| `VRWeapon.cs` | Places the weapon model and muzzle, fires haptics on ammo drop |
| `VRHandInteract.cs` | Hand-ray interaction, replicates the game's Use toggle |
| `VRInput.cs` | Maps XrApi buttons/sticks to `KeyBind` overrides |
| `VRInputGate.cs` | One answer to "may native mouse/keyboard pass through this frame" |
| `VRHudSafeArea.cs` | Shrinks the uGUI HUD content by parenting it to a smaller rect |
| `VRPostFX.cs` | Surgically disables AA/TAA, AO, DoF every 2 s |
| `VRLightTamer.cs` | Silences every light's shadows except the helmet lamp |
| `VRHeadLamp.cs` | Owns one helmet lamp parented to the head camera |
| `VRClipGuard.cs` | Forces occlusion culling off while the camera is inside geometry; applies far clip and fog |
| `VRWorldScaler.cs` | Renders `WorldCamera` into a low-res RT and blits it full-screen; the HUD cameras stay at window resolution |
| `VRPainOverlay.cs` | Suppresses the game's per-camera red flash and draws peripheral bands once |
| `SyncPixelQuad.cs` | Global overlay camera + immediate-mode `GL` red sync pixel |
| `VRConfigFile.cs` | Writes one key back to `config.txt`, preserving comments and duplicates |
| `VRTuneMenu.cs` | Clickable in-headset settings panel |
| `VRFovTuner.cs` | Arrow-key FOV stepper while paused; superseded by the menu but harmless |
| `HUDTreeDumper.cs` | F9 scene/camera/field dump to `hud_tree.txt` |

## Boot sequence — `VRManager.Awake`

1. Probes for `Application.isBatchMode` and sets `BEPINEX_BATCH_CHECK=0` if it is
   missing. **Vestigial**; the comment still says "Unity 4 compat", but the
   engine is 5.6.7. [src]
2. Wires `VRManagerLogger`. **Bug:** `LogInfo` is assigned to `LogDebug` first,
   then to `LogInfo`; the second wins, so `VRManagerLogger.LogDebug` is
   unreachable. Harmless, but there is no debug channel through that name. [src]
3. `LogBuildStamp()` — first log line, `RELEASE`/`DEBUG` plus the assembly write
   time. This satisfies the `general_wxr` build-stamp rule. [src]
4. `ResolveConfigPath()` — `BepInEx/plugins/IMVR/config.txt`, then the plugin
   root, then the DLL directory. [src]
5. `ParseConfig()` — reads every key, then `VRInput.SetConfig(deadzone)`. [src]
6. `ApplyRenderResolution()` — one `Screen.SetResolution` call if the current
   size differs. The owner runs fullscreen in the container, so there is no
   borderless pass. [src]
7. `XRAPI.XR_Init()` — resolves the handshake directory, writes `version` and
   `vr`, reads `system` if present, then opens the receive and send sockets. [src]
8. `Harmony.PatchAll()`.
9. `SyncPixelQuad.Ensure()` — creates the `DontDestroyOnLoad` overlay camera
   once. [src]
10. `VRWorldScaler.Ensure()` — creates the depth-0.5 blit camera once. [src]
11. Creates `IMVR_InputRunner` with `VRInputRunner` and `VRTuneMenu`. [src]

## Frame order

```
Update (IMVR_InputRunner)
  VRManager.PollConfig            every 2 s: re-read config.txt
  XRAPI.TickHandshake             every 2 s: repair version/vr, read system
  VRFovTuner.Update               arrows while paused; writes config back
  VRPostFX.Enforce                every 3 s: AA/TAA, AO, DoF off
  VRLightTamer.Enforce            every 4 s: only helmet lamp casts shadows
  VRPainOverlay.Enforce           clamp the game's red flash to alpha 0
  VRInput.UpdateFrame
      read XrApi state
      fill overridesBool
      rotate body from right stick X
      slot cycling / recentre / Use edge
      XRAPI.UpdateVRModeFromGameState  -> MODE_VR + outbound 7278 packet
      XRAPI.TickHaptics                -> decay after the send
  VRWorldScaler.Tick           enable the low-res compositor only in VR gameplay

HUDCamera.Update postfix
  only when inVR: distort = 0; hudScale = hud_supersample (on change)

LateUpdate
  VRHeadTracker.LateUpdate
      park VRRoot on the Marine (yaw only)
      camera.localPosition = base + eye offset + clamped lean
      camera.localRotation = baseLocalRot * headRot
      VRClipGuard.Apply(cam)
      VRWorldScaler.Apply(cam)  -> WorldCamera.targetTexture = low-res RT
  VRWeapon.LateUpdate
      ammo-drop haptic
      place model from VRHands.GetDominantPose
      muzzle.rotation only
  VRGlassAnchor.LateUpdate
      load hudScale, apply VRHudSafeArea
      glue glass mesh to head camera when visor Closed
      restore original parent when visor Open
      do NOT scale HUD UI Root

OnRenderImage (VRWorldScaler's depth-0.5 camera)
  clear to black, then Graphics.Blit low-res WorldCamera RT -> Unity's destination,
  before the HUD cameras; never reads the destination it writes

OnPostRender (SyncPixelQuad's depth-1000 camera)
  VRPainOverlay.Draw   peripheral bands, once per frame
  GL red rect          top-left, exact pixel coordinates, no blend/filter
```

`SyncPixelQuad` has **no `LateUpdate`** in the current code, contrary to
`Docs/ClaudeCode/ARCHITECTURE.md`'s frame diagram. It reads the frame id in
`Update` and paints in `OnPostRender`. [src]

`VRHUDAnchor` still exists at `Patches.cs:739` but **nothing adds it**; the
comment at `Patches.cs:169` explains that adding it to `HUDCamera` caused
flicker. Treat it as dead code. [src]

## Patch inventory

| Patch | Target | Purpose |
|---|---|---|
| `Helmet_InitFirstPersonCamera_Patch` | `Helmet.InitFirstPersonCamera` (reflection) | Recreates `VRRoot` per level; adds `VRHeadTracker`; `SyncPixelQuad.Ensure`; hides layer-8 helmet shell; keeps `HelmetOccluder` renderer, shadows off; attaches `VRGlassAnchor` to `*Glass_HUD*`; reparents glass to the camera preserving pose |
| `RangedWeapon_AttachToCamera_Patch` | `RangedWeapon.AttachToCamera` (reflection) | Adds `VRWeapon`; **newest commit:** zeroes sprint-bob scales and the sprint amplitude curve |
| `InputManager_IsInput_Patch` | `InputManager.IsInput` (reflection, 1 arg) | Allow native in menus/paused; VR override true wins; block gameplay names |
| `InputManager_IsInputDown_Patch` | `InputManager.IsInputDown` | Same, plus **withholds the Use edge** in VR so `VRHandInteract` can own it |
| `NoHeadRecoil_VR_Patch`, `_Simple_`, `_Lurch_`, `_Exhaustion_` | `RangedWeapon` view-rotation methods | Restore the pre-call values in VR; no head recoil/bob/lurch |
| `Helmet_VR_NoVisorBob`, `_NoDynamicBob`, `_NoBob` | `Helmet` bob methods | Skip entirely in VR |
| `Helmet_VR_NearClip` | `Helmet.InitFirstPersonCamera` (second postfix) | `VRVisualTweaks.ApplyGlobal`; re-assert resolution; near/far/FOV; visor transform nudge |
| `WeaponLight_LateUpdate` | `WeaponLight.LateUpdate` | `VRHeadLamp.Ensure` / `SetOn`; forces per-weapon lamps off |
| `HUDCamera_VR_Shrink_Patch` | `HUDCamera.Update` | Distort 0, `hudScale` follows `hud_supersample`; never touch glass/culling/clear |
| `GUICompass_VR_HeadYaw_Patch` | `GUICompass.UpdateCompass` | Invoke private `UpdateTicks` with head yaw; skip original body-yaw version |
| `Helmet_VR_NearClip_2` | `Helmet.SetRenderMode` | Re-assert near clip on render-mode changes |
| `InputManager_IsInput_Debug` | `InputManager.IsInput` | Verbose logging only |
| `UnityInput_GetAxis_Block`, `GetAxisRaw`, `GetKey`, `GetKeyDown`, `GetMouseButton`, `GetMouseButtonDown` | raw `UnityEngine.Input` | Block mouse/turn/WASD/arrows/space/shift/mouse buttons when `VRInputGate.AllowNative()` is false |
| `VRInputRunner` | — | Per-frame order above |

## XrApi client — `XRAPI.cs`

- **Version 0.4.** One receive socket, first free of `{7872, 7873}`, then
  `break`. Binding both would double every packet. [src]
- **Handshake (completed 7 Nov):**
  - Resolves `Z:/tmp/xr`; if `Z:` is absent, uses `xrtemp` next to the DLL.
  - Writes `version` (`0.4`) and `vr` (`VR`) **unconditionally**.
  - `TickHandshake` (2 s, from `VRInputRunner.Update`) recreates the directory
    and rewrites either file if the runtime wipes them.
  - Reads `system`: `EUREKA`/`PANTHER` -> Quest 3, `HOLLYWOOD` -> Quest 2,
    `SEACLIFF` -> Quest Pro, `MONTEREY` -> Quest 1. Defaults to Quest 3.
  - `flipHands=auto` follows detection; an explicit `true`/`false` overrides.
  - Deletes `vr` on clean shutdown; leaves `version`.
  [src] — full detail and the device checklist are in
  [HANDSHAKE.md](HANDSHAKE.md).
- Receive thread blocks with a 500 ms timeout. It receives into a reusable
  `byte[2048]` and **drains the socket, parsing only the newest packet**. The
  parser is allocation-free: it scans ASCII bytes for 28 floats, the frame id
  and the 19 bool characters instead of `Split` + `float.Parse`. It skips a
  leading `client*`, ignores trailing tokens and the HMD name. [src]
- The receive thread uses `Environment.TickCount`, not Unity `Time.*`, and runs
  at `AboveNormal` priority. A malformed packet is skipped, not treated as a
  fatal socket error. [src]
- **Normalisation and handedness happen once, at ingest**, in
  `XRAPI.ToUnity`: quaternion normalised unconditionally, component flip
  `(-x,-y,z,w)`; vector `z` negated. Do not repeat it in consumers. [src]
- `hasData` is never cleared, so a dropped stream freezes on the last pose
  rather than zeroing. [docs, consistent with source]
- Outbound packet: `leftHapticFrames rightHapticFrames modeVR mode3D fovX fovY`,
  built with `InvariantCulture` (a comma-decimal locale would otherwise corrupt
  every field after the FOV). [src]
- Haptics are **durations in frames**, per hand. `SendHapticVibration` takes the
  max of pending and new; `TickHaptics` decays one per frame after the send.
  The `amplitude` parameter is accepted and ignored. [src]
- Mode switching: `MODE_VR=1` in gameplay, `0` in menus/pause. `MODE_3D` is
  always `0` (mono). The `Cursor.visible` test is deliberately gone and carries a
  comment explaining why it must not return. [src]
- `Shutdown` closes both sockets and aborts the receive thread; it does not
  delete a `vr` file, because none is written. [src]

## Head and view

- `VRHeadTracker.Awake` and `Start` both capture the camera's local pose;
  `Start` is the one that counts, because the near-clip postfix has run by then.
- `LateUpdate` parks `VRRoot` at the Marine's position with yaw-only rotation,
  then applies `baseLocalPos + eye + lean` and `baseLocalRot * headRot`.
- Lean is clamped to **0.30 m** magnitude. There is no roomscale walking; the
  body is turned by the right stick.
- The eye offset lives here (`eye_height`, `eye_forward`), not in the spawn
  patch, so it is live-tunable.
- `VRClipGuard.Apply` runs last, with the camera at its final position, so the
  inside-geometry linecast asks about where the eye actually ended up.

## Hands and weapon

- `VRHands.GetDominantPose` is the single frame:
  - pose already normalised and handedness-swapped at ingest;
  - `flipHands=false` (Quest 3) applies a **roll** `r * Euler(0,0,180)`; yaw
    would aim backwards. `auto` reads `system`; an explicit config value wins;
  - `pos = headWorld + bodyRot * (controllerPos + handHeight)`;
  - `rot = bodyRot * controllerRot`.
- **Body rotation, not head rotation.** That is what stops the gun swinging when
  the player looks around. This is the fix `general_wxr/TIPS.md` cites from this
  project by name.
- `VRWeapon` detaches `WeaponModel` from any ancestor carrying `VRHeadTracker`
  to the **scene root**, never to `VRRoot`. `VRRoot` is destroyed per level and
  would take the gun with it.
- Haptics are driven by `RangedWeapon.GetAmmoCount()` decreasing, not by the
  Fire input: one buzz per round actually spent, no buzz on a dry trigger, no
  phantom kick on a refused shot.
- The muzzle gets **rotation only**, so the logical shot still originates from
  the body.

## HUD — the part not to break

The visor is a three-stage render, and breaking any stage makes it vanish with
no error:

1. uGUI HUD is rendered by `HUDCamera` (and `CAM Camera`) into
   `HUDCamera.cleanToBendTarget`. The **field** is `cleanToBendTarget`; the
   RenderTexture's `.name` is **`"CleanToBlendTarget"`**. Both spellings are
   correct in their own context. [src + docs]
2. `HUDCamera` bends that target via `bendMaterial` into
   `bendToDoubleGlassTarget`, then projects it with `doubleGlassMaterial` onto
   the glass mesh.
3. `CH_Marine1pHelmetGlass_HUD` is rendered by `HelmetCamera`, a child of
   `WorldCamera`.

`VRGlassAnchor` (inside `Patches.cs`) does the VR glue:

- While `Helmet.currentVisorState == "Closed"`, reparent the glass mesh to the
  head camera **preserving** `originalLocalPos` / `originalLocalRot`; only
  re-parent when needed, or the Animator fights it and the HUD flickers.
- When the visor opens, restore the original `Helmet` parent and pose, so the
  HUD disappears exactly as in flat play.
- Each frame it restores the **original** values of every failed shrink lever:
  HUD camera frustum, `Camera.rect`, `doubleGlassMaterial` UV, and `HUD UI Root`
  scale. Those are no-ops kept as a record; do not "re-enable" them.
- The real shrink is `VRHudSafeArea.Apply`, which inserts a stretched
  `RectTransform` between each canvas and its children and scales that about its
  centre. Edge-anchored widgets then move inward **and** shrink.

`HUDCamera.hudScale` is a **supersample factor**, not a size. The game sizes both
HUD render targets at `Screen * hudScale` and divides the `CanvasScaler`
reference resolution by the same number. The mod used to pin it to 1.0, which was
below the game's own 1.1 default; it now follows `hud_supersample`, default 1.1.
With `world_render_scale=0.5`, that already gives the HUD roughly twice the world
resolution; 2.0 is available but costs 4x the HUD fill.

The physical visor's dirty-glass texture is **deliberately removed** in the
near-clip postfix (`mainTexture = null`, colour alpha 0.12). The owner confirmed
it is only a dirty plastic/glass look and is not wanted in VR. Do not "restore"
it; the faint alpha tint is the remaining glass cue.

## Presentation and comfort

- **Sync pixel:** its own `DontDestroyOnLoad` camera at depth 1000,
  `clearFlags = Nothing`, `cullingMask = 0`, orthographic, forward. It paints a
  `GL` quad in screen-space pixels with blending off and green 0. Size comes
  from `sync_pixel_size` (default 10). When no packets have arrived it uses
  `Time.frameCount % 256` so the runtime can still calibrate. [src]
- **Pain flash:** `VRPainOverlay.Enforce` clamps `MarineController`'s flash to
  alpha 0; `Draw` paints two peripheral red bands from the sync-pixel camera,
  exactly once per frame. `pain_bar_inset` places them against the visible area.
- **Post-processing:** `VRPostFX` disables only `antialiasing` (covers FXAA and
  TAA), `ambientOcclusion` and `depthOfField` on every profile. Bloom, blur,
  chromatic aberration, colour grading, vignette, fog and motion blur are left
  alone. TAA matters most: it jitters the projection matrix and blends the sync
  pixel.
- **Shadows:** `VRLightTamer` writes only `Light.shadows`; it never touches
  `Light.enabled`, which is `ToggleOcclusion`'s business. The helmet lamp is the
  only light allowed to cast, and it casts `Hard`.
- **Occlusion:** `VRClipGuard` linecasts from the Marine's chest to the eye each
  frame. If a wall is between them, it sets `useOcclusionCulling = false`. It
  also applies the steady `far_clip` and moves the fog with it. It never
  collapses the far clip per frame, because `OnBecameInvisible` also fires for
  frustum culling and would switch *more* lights off.
- **Fog:** `VRVisualTweaks.ApplyFog` forces linear fog from 45% to 95% of
  `far_clip`, colour `(0.08, 0.10, 0.12)`. It is unconditional and not
  config-gated; `far_clip` is the knob.
- **Resolution:** `Screen.SetResolution(render_width, render_height,
  Screen.fullScreen)` at startup and on spawn. `render_width=0` opts out.
- **World scaler:** `VRWorldScaler` sets `WorldCamera.targetTexture` to a
  `Screen * world_render_scale` RT (default 0.5 = 640x480 in a 1280x960
  window). A depth-0.5 camera clears to black, then blits the RT to screen from
  `OnRenderImage` before `HelmetCamera` / `CAM Camera` at depth 1. The HUD
  cameras keep using `Screen` dimensions, so the visor and text stay
  full-resolution. `1.0` restores direct rendering. The game's
  `CopyToScreenRT` is disabled while the RT is active: it copies into
  `RenderTexture.active`, which is the RT, so it would feed the frame back into
  itself; it is restored on pause and at `1.0`.
- **Staggered periodic work:** config poll 2 s, post-FX 3 s, HUD safe-area
  scan 3 s, light sweep 4 s. `FindObjectsOfType` and `GetComponentsInChildren`
  allocate, so keeping them off the same frame avoids a recurring spike.
- **Window:** the owner runs the game fullscreen in the WinlatorXR container, so
  no borderless/chrome-stripping code is shipped. `Screen.SetResolution` is only
  a resolution knob; revisit only if a capture shows chrome over the sync pixel.

## Input

- `VRInput.UpdateFrame` clears and refills `overridesBool`, keyed by `KeyBind`
  names. All 22 names the mod writes are real members. [docs/reflected]
- `VRInputGate.AllowNative()` returns true when not in VR, when
  `Marine.LocalMarine == null`, when `GameManager.IsPaused`, or when
  `GameUI.IsShowingMouseBlockingGUI` / `IsShowingModalGUI`. Every blocking patch
  asks it, so the answer cannot drift.
- `InputManager.IsInputUp` is **deliberately unpatched**. `Toggler` flips state on
  the release edge; because the mod never fakes a release, `isToggled` stays
  false and the logic collapses to clean hold-to-activate. Patching it would
  invert `ToggleFlashlight`, `ToggleLaser`, `Aim` and `Crouch`.
- `ToggleFlashlight` and `Crouch` are published as **held levels**, not edges,
  for the same `Toggler` reason. `Use` is special: the edge is withheld so
  `VRHandInteract` can toggle off the hand ray; the level still passes so a
  press-and-hold panel releases correctly.
- `Aim` is both **unbound and blocked** by `name.Contains("Aim")`, so iron sights
  are unreachable in VR. That is a product decision waiting on the owner, not a
  bug to quietly "fix".
- Raw `UnityEngine.Input` is blocked for mouse axes, WASD, arrows, space,
  shift and mouse buttons. Arrows pass while paused because `AllowNative()` is
  true there, which is what lets `VRFovTuner` work.

## Diagnostics

- `HUDTreeDumper` on **F9** writes `hud_tree.txt` next to the DLL via a
  `DontDestroyOnLoad` runner. It deliberately ignores `verboseDebug` so the
  useful dump always works.
- `verboseDebug` is `false` and has **no config key**; changing it requires a
  recompile. `general_wxr/CLAUDE.md` asks for one verbose flag, off by default,
  reachable at runtime.
- The build stamp on line 1 is the answer to "did I deploy the DLL I just
  built?".
- `UpdateVRModeFromGameState` logs a one-line signature on every transition and
  names which signal moved. This is the right shape for a mode bug: the old
  `Cursor.visible` problem was invisible because only the result was logged.

## Invariants — do not break these

1. **Never set `glassIntensity = 0`, `glassTexture = null`,
   `clearFlags = SolidColor`, or a transparent background on `HUDCamera`.** It
   kills the double-glass blend and the HUD disappears.
2. **Never scale `HUD UI Root`.** It moves `HUDCamera` and breaks the bend
   pipeline; the HUD then appears only while paused. Use `VRHudSafeArea`.
3. **Never disable Bloom / Blur on `HUDCamera`** in the name of performance.
   They are part of the bend. Disable only AA/TAA, AO and DoF, on profiles.
4. **Never force `localPos = (0,0,0)` / identity on `Glass_HUD`.** Its Animator
   pose is `(1, 359.7, 0)`; preserve the captured pose.
5. **Hide layer 8 `CH_Marine1pHelmet*`, not layer 14 `Glass_HUD`.** The glass
   HUD itself is layer 0 with layer-14 children; match by name.
6. **Never parent the weapon to `VRRoot`.** It is destroyed per level. Use the
   scene root.
7. **Never use head rotation for the weapon.** Use head position and body
   rotation.
8. **Never reinstate a `Cursor.visible` test** in input or mode logic. It is
   permanently true under Winlator.
9. **Never patch `InputManager.IsInputUp`** without replacing the whole
   press/release cycle; patching it alone inverts four working inputs.
10. **Never collapse `far_clip` per frame** to "bound the cost" of culling.
    `OnBecameInvisible` fires for frustum culling and would switch more lights
    off.
11. **Keep the sync pixel last, unblended, unfiltered, green 0.** Any blend or
    gamma correction corrupts the frame id the runtime reads.
12. **Do not "fix" the helmet by removing it.** The bent-glass visor is the
    game's identity.
13. **Keep `world_render_scale=1.0` as the rollback.** If the low-res composite
    ever draws over the HUD or leaves a stale frame, 1.0 restores direct
    `WorldCamera` rendering without a rebuild.
