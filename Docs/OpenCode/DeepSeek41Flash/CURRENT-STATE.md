# CURRENT-STATE.md — the port as of 2026-11-07

This file is a snapshot, not a history. For how the code got here, read the
archived `Docs/MetaAI/`; for the full audit of the 30 Oct tree, read the
archived `Docs/ClaudeCode/`.

All source claims are against commit **`f593154` "Project transfer to DeepSeek
V4.1 Flash"**, plus the uncommitted performance/housekeeping work.

## Repository and build

| Fact | Value | Mark |
|---|---|---|
| Branch | `main` | [src] |
| HEAD | `f593154` (handshake + docs pass committed) | [src] |
| Commits after `e648e72` | `177dee4`, `e9474a2`, `17a5f92`, `f593154` | [src] |
| Working tree | **housekeeping + low-res world scaler + UDP receiver, uncommitted** | [src] |
| `Docs/` tracked | 29 files (including `PC_INPUT.md`) | [src] |
| Build | `dotnet build IMVRMod.csproj -c Release` — **0 errors, 0 warnings** (7 Nov housekeeping) | [build] |
| Engine | Unity **5.6.7**, BepInEx 5, Harmony, `net35`, `LangVersion 3`, x86 | [docs] |
| Plugin version | `0.3.2` | [src] |
| Default configuration | `Release`, forced in `Directory.Build.props` | [src] |

The October audit had 5 warnings, then 3. The 7 Nov housekeeping removed the
last three: the unreachable input block, the unused catch variable and the
unused `hasBase` field. The build is now clean.

### Committed in `f593154`

- **Handshake completed** in `XRAPI.cs`: `version` and `vr` written
  unconditionally; `TickHandshake` repairs them every 2 s if the runtime wipes
  the directory; `system` is read for headset detection; `flipHands=auto` uses
  it while an explicit `true`/`false` overrides; desktop fallback is `xrtemp`
  next to the DLL; `vr` is deleted on shutdown.
- **Docs updated**: root `README.md`, the `general_wxr` notes and this set.
  `Docs/ClaudeCode/` and `Docs/MetaAI/` are **archived and deliberately
  unchanged**.
- **Borderless window** is accepted as handled by the owner's fullscreen
  container, not by mod code.

### Working-tree changes after `f593154` (uncommitted)

- **Config:** `shadow_distance=10`, `world_render_scale=0.5`, and
  `hud_supersample=1.1` (back to the game default).
- **Low-res world / full-res HUD** (`VRWorldScaler.cs`): `WorldCamera` renders
  into a 640x480 target, a depth-0.5 camera blits it, and the HUD cameras stay
  at 1280x960. Based on the XOPS-WXR composite. The composite camera now clears
  the backbuffer (the world camera used to be the only Skybox clear) and the
  game's `CopyToScreenRT` is disabled while the RT is active: it copies the
  deferred result into `RenderTexture.active`, which **is** the world RT, so it
  fed the frame back into itself and bloom turned that into a white screen. It
  is restored on pause and at `world_render_scale=1.0`.
- **UDP receiver:** reusable receive buffer, socket drain (newest packet wins),
  allocation-free ASCII parser, `AboveNormal` thread priority, and
  `Environment.TickCount` instead of Unity `Time.*` on the receive thread.
- **Periodic work staggered:** post-FX 3 s, HUD safe-area scan 3 s, light sweep
  4 s, so their allocating `FindObjectsOfType` / `GetComponentsInChildren` calls
  do not land on one frame.
- **Housekeeping:** removed the unreachable input block, fixed the logger
  wiring, deleted dead `VRHUDAnchor`, removed `hasBase`, and dropped the unused
  haptic amplitude parameter. Build is 0 warnings, 0 errors.

## What changed since the ClaudeCode audit

`Docs/ClaudeCode/STATUS.md` says "Nothing since `e648e72` has been committed."
That is no longer true. Four commits landed afterwards:

| Commit | Subject | What it actually contains |
|---|---|---|
| `177dee4` | Game improvements | The 3 Nov batch: `VRHandInteract`, `VRHeadLamp`, `VRClipGuard`, `VRPostFX`, `VRInputGate`, `VRTuneMenu`, `VRConfigFile`, `VRFovTuner`, wider config, pain overlay, input-gate centralisation |
| `e9474a2` | Further progress | HUD safe area / supersample wiring, `VRHands`, sync-pixel rewrite, `VRLightTamer`, `VRPainOverlay`, docs update, large config expansion |
| `17a5f92` | Less motion sickness | Zeroes the sprint-bob scales and the sprint amplitude curve on `RangedWeapon`; changes the default `shadowDistance` from 15 to 10; adds OpenCode to the README credits |
| `f593154` | Project transfer to DeepSeek V4.1 Flash | The 7 Nov handshake completion, the README/docs reconciliation, and the OpenCode notes. The performance/housekeeping work is **after** this commit and still uncommitted |

The latest commit is the one to read before assuming the docs describe HEAD. It
does not.

### The sprint-bob change, specifically

`RangedWeapon_AttachToCamera_Patch` now reflects four fields on every weapon:

- `cameraSprintBobRotationXScale` → `0`
- `cameraSprintBobRotationYScale` → `0`
- `cameraSprintBobRotationZScale` → `0`
- `cameraBobSprintAmplitudeOverSpeed` → a flat `AnimationCurve(0,0)-(20,0)`

It deliberately leaves `sprintness` alone, because that same value also gates
walk bob via `(1 - sprintness - crouchedness)`. Net effect: **sprint view bob is
gone; walk, weapon and breath bob remain.** That is the owner's stated intent in
the commit message ("No more bobbing when jogging / sprinting") and is a comfort
fix, not a general "remove all bob" change.

## Config defaults — what actually runs

`config.txt` is read from next to the DLL and **re-read every 2 seconds** by
`VRManager.PollConfig`. Values in the file override the code defaults. The
tuning menu writes changes back through `VRConfigFile.Set`, which is required or
the 2 s poll reverts them.

| Key | Shipped value | Source default | Notes |
|---|---|---|---|
| `dominant_hand` | `right` | `right` | Hand that holds the weapon, receives haptics, and owns the interaction ray |
| `turn_speed` | `80` | `45` | Degrees/second, right stick X |
| `deadzone` | `0.20` | `0.20` | Applied to both sticks |
| `flipHands` | `auto` | `false` | `auto` = read `system`; explicit `false` = Quest 3, `true` = Quest 2/Pico. Applies a **roll**, not yaw |
| `hudScale` | `0.60` | `0.62` | Drives `VRHudSafeArea`, **not** `HUD UI Root` |
| `eye_height` | `0.16` | `0.16` | Metres above the helmet mount |
| `eye_forward` | `0.05` | `0.05` | Metres forward, clear of the chest |
| `hand_height` | `0.05` | `0.05` | Lifts the weapon with the eye |
| `light_height` / `light_forward` | `0.10` / `0.10` | same | Helmet lamp placement |
| `near_clip` | `0.05` | `0.05` | Low enough for a holo sight |
| `occlusion_culling` | `1` | `true` | `VRClipGuard` forces it off while inside geometry |
| `far_clip` | `45` | `45` | Fog follows it; `45%`–`95%` |
| `pain_alpha` | `0.55` | `0.55` | `0` disables the peripheral bands |
| `pain_bar_width` | `0.15` | `0.15` | Fraction of visible width |
| `pain_bar_inset` | `1.00` | `1.00` | Confirmed on device; full frame width is visible |
| `occluder_scale` | `1.0` | `1.0` | `HelmetOccluder`; layer 14, cannot affect the world |
| `screen_fov` | `90` | `104.5` | Runtime paint angle, **not** the camera FOV (98 vertical) |
| `render_width` / `render_height` | `1280` / `960` | same | Applied at startup and on spawn; `0` opts out |
| `world_render_scale` | `0.5` | `0.5` | 3D world renders at 640x480 inside the 1280x960 window; HUD stays full-res. `1.0` disables |
| `hud_supersample` | `1.1` | `1.1` | `HUDCamera.hudScale`; game default. The 0.5 world scale already doubles relative HUD sharpness |
| `shadow_distance` | `10` | `10` | Matches the code default now; only the helmet lamp casts |
| `shadow_resolution` | `0` | `0` | 0=Low … 3=VeryHigh; only the helmet lamp casts |
| `sync_pixel_size` | `10` | `10` | Framebuffer pixels; GL rect, not a quad |

One thing worth noticing:

1. **`hudScale` has two defaults.** `VRGlassAnchor`'s fallback is `0.62`, but the
   shipped config is `0.60`. The config wins; the code fallback only matters if
   the file is missing.

## Device-confirmed versus built-only

This is the most important table in the set, because the audit's own files
occasionally disagree with each other.

### Confirmed on device (per the owner / ClaudeCode, 1–3 Nov)

| Feature | Value / evidence |
|---|---|
| `hudScale` | **0.60** — confirmed 2 Nov |
| `screen_fov` | **90** works against 98 rendered; proves the runtime consumes the field |
| Occlusion blackout | `occlusion_culling=0` cured it; **1 behaves** once the second cause was fixed; black screens gone, respawn clean |
| `pain_bar_inset` | **1.00** — panel is wider than tall, so full frame width is in view |
| Sync pixel | 10 px read correctly; previously 150 px also read but was visible to the player |
| HUD glue | Glass follows the head; preserves the Animator pose |
| HUD scaling | `VRHudSafeArea` moves edge-anchored widgets inward; 0.60 confirmed |
| In-headset tuning | `VRTuneMenu` shows on pause/menus; writes back to `config.txt` |
| 6DOF head tracking | Position and rotation; lean clamped to 0.30 m |
| `MODE_VR = 0` in menus | Deliberate and better: stops the pose stream, restores runtime mouse, leaves immersive presentation |
| Body yaw composition | Camera is under the helmet under the Marine, so yaw adds for free |
| `flipHands` roll fix | Quest 3 upright with `false`; Quest 2/Pico with `true` |
| Weapon at the controller | Head position for height, body rotation for orientation; survives level changes |
| Helmet shell hidden | Layer 8 `CH_Marine1pHelmet*` renderers off; `Glass_HUD` spared |

### Built, reads correctly, not confirmed on device

| Feature | Why it is still built-only |
|---|---|
| Interaction ray from the hand | Rewritten 3 Nov; brackets, hint and Use all come off the dominant hand. Needs a device pass |
| Shadows | `VRLightTamer` sweep + helmet lamp only; `shadow_resolution=Low`. Needs visual check |
| HUD text supersampling | `hud_supersample=1.1`; cost and legibility need a headset check against the 0.5 world scale |
| Haptics | Ammo-drop (6 frames) and weapon-switch (10 frames); never felt on device |
| Compass follows head yaw | Four-argument `UpdateTicks` invocation; `UnityEngine.UI` was not in `Libs/` for a compile-time check |
| Visor-open hides HUD | `VRGlassAnchor` restores the original parent when the visor opens; needs re-test |
| Menus / lobby in VR | Still handled by leaving VR (`MODE_VR=0`), not by making the menu cameras VR-aware |
| Borderless window | **Accepted** — the owner runs fullscreen; no chrome-stripping code is shipped |
| Low-res world / full-res HUD | Written 7 Nov; `world_render_scale=0.5` with a depth-0.5 composite. **First device report: runaway white glow.** Cause and fix are in the working tree (`CopyToScreenRT` self-copy + missing backbuffer clear); needs a re-check for camera ordering and HUD sharpness |
| UDP receiver | Rewritten 7 Nov: drain + allocation-free parser. Needs a device check for packet pacing and tracking feel |
| Handshake watchdog | Written 7 Nov; `version`/`vr` are repaired every 2 s after a runtime wipe. Needs a device check |
| Headset auto-detection | Written 7 Nov; `system` maps `EUREKA`/`PANTHER`/`HOLLYWOOD`/`SEACLIFF`, and `flipHands=auto` follows it. Needs a device check |

### A contradiction inside ClaudeCode

`CURRENT_FEATURES.md` marks the weapon model as **[device]**. `STATUS.md` says
"Working, unconfirmed on device". I would trust `CURRENT_FEATURES.md`'s
`[device]` only if the owner confirms it; the safer reading is that the *frame*
is right and the *final feel* still needs a test. Flagged rather than resolved.

## Which older claims are now wrong

### `Docs/MetaAI/`

Session history, and still the best record of *why*. It should not be read as
current fact.

| Claim | Reality |
|---|---|
| "Unity 4" | **Unity 5.6.7**; BepInEx logs it. `LangVersion 3` is a project choice, not an engine limit |
| "`RenderSettings.fogStartDistance` / `fogMode` don't exist" | They do; `Patches.cs:57-62` uses them and the project builds. The original errors were C# version/class-level-statement errors, not the fog API |
| "The HUD is NGUI" | It is **uGUI**; `Assembly-CSharp` references `UnityEngine.UI`, and `HUDCamera` has a `mainCanvasScaler` |
| "HUD only visible when paused" (active bug) | Superseded. FIXED29 kept `HUD UI Root` at 1.0; `VRHudSafeArea` now shrinks the canvas **content** |
| "`D:/xrtemp` fallback" | No such path. The on-device dir is `Z:/tmp/xr`; as of 7 Nov the desktop fallback is `xrtemp` next to the DLL |
| `PROJECT-PLAN.md` lists three config keys | `config.txt` now has roughly twenty |
| "Scaling `HUD UI Root` worked" | It worked visually once and broke the bend pipeline. Do not repeat it |

### `Docs/ClaudeCode/`

The best audit, **archived and unchanged**. The "status now" column below is my
reconciliation, not an edit to those files. One open item remains in the code:
the `verbose` flag.

| Claim at audit time | Where | Status now |
|---|---|---|
| "Nothing since `e648e72` is committed" | `STATUS.md` | **Fixed** — three commits after it; docs tracked except `PC_INPUT.md` |
| "The window is never touched" | `FINDINGS.md` #5 | **Accepted** — `Screen.SetResolution` exists, and the owner runs fullscreen; no borderless code is needed |
| Sync findings #2/#3 are "deferred" | `NEXT_STEPS.md` | **Fixed** — both resolved by the 3 Nov global-camera rewrite; the plan now says so |
| Sync quad mutates the host camera's `cullingMask` | `FINDINGS.md` #12 | **Fixed** — the quad owns a camera with `cullingMask = 0` |
| Stage A: add a build stamp | `NEXT_STEPS.md` | **Done** — `Plugin.LogBuildStamp` logs `RELEASE`/`DEBUG` and the assembly write time on line 1 |
| Stage A: add a `verbose` key | `NEXT_STEPS.md` | **Still open** — `verboseDebug` has no config key |

### Root `README.md`

The root README **was updated on 7 Nov**; the rows below record what it said at
audit time. `Docs/ClaudeCode/` and `Docs/MetaAI/` were left archived.

| README claim at audit time | Current truth |
|---|---|
| "Haptics on shoot (right controller 6-8 frames)" | Haptics are on the **dominant** hand, driven by the **ammo counter** (6 frames), plus 10 frames on a weapon switch |
| "Lean clamped to 12cm" | `0.30 m` in `VRHeadTracker.cs:28` |
| "Red pixel quad top-left (10x10)" | It is an immediate-mode `GL` rect drawn by a global overlay camera; size is configurable and defaults to 10 px |
| "`XRAPI.cs` — … `D:/xrtemp` fallback" | No `D:/xrtemp`; the fallback is `xrtemp` next to the DLL, used only when `Z:` is absent |
| "`VRWeapon.cs` — RH→LH `pos.z=-z, quat(-x,-y,z,w)`" | The handedness/normalisation conversion moved to `XRAPI.ToUnity` (ingest); `VRWeapon` delegates the frame to `VRHands` |
| "Post-processing disabled for VR: motionBlur, chromaticAberration, depthOfField" | `VRPostFX` disables **AA/TAA, ambient occlusion, depth of field** only. Bloom is deliberately left alone because it is part of the HUD bend; motion blur and chromatic aberration are not touched |
| "HUD scaler works" (commit title) | It works **now**, through `VRHudSafeArea`; at the time of that commit it was read and discarded |

## What I could not verify

- Whether the 7 Nov handshake works on device: that `system` is present when the
  mod reads it, that the codename maps correctly, that `version`/`vr` survive a
  runtime restart, and that an explicit `flipHands` still overrides detection.
  The owner runs fullscreen, so the window-chrome question is settled by
  workflow rather than by code.
- Whether `VRHudSafeArea`'s skip list spares every world-space marker in every
  level. It skips by substring (`waypoint`, `marker`, `objective`, `tracker`,
  `world`, `nametag`), which is a heuristic.
- Whether `VRWorldScaler`'s depth-0.5 blit lands before every HUD camera on the
  device, and whether `CAM Camera` / `HelmetCamera` still render at full window
  resolution in gameplay. A `hud_tree.txt` dump plus a screenshot settles it.
- Whether the new parser's packet mapping matches a real Quest 3 capture. The
  allocation-free parser was checked against the sample packet in
  `Docs/general_wxr/SixDOFinatorCalibration/SamplePoseData.txt`; the field
  mapping is unchanged from the old parser.
- The final feel of the interaction ray, haptics, shadows and HUD supersampling.
- Any frame-time measurement. Per `general_wxr/CLAUDE.md`, that belongs on-device,
  not in the mod.
