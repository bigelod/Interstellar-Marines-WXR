# ARCHITECTURE.md — what IMVRMod actually does at runtime

Read from the source on 2026-10-30. Where this contradicts `Docs/MetaAI/` or
the root `README.md`, the code is what shipped; see [STATUS.md](STATUS.md) for
the list of disagreements.

## Environment facts, confirmed

- **Unity 4** (`UnityEngine.dll` is a CLR 2.0 assembly), **BepInEx 5**,
  Harmony, `net35`, `LangVersion 3`, x86. **[code]**
- `dotnet build IMVRMod.csproj -c Release` **succeeds** from this repository
  with the five DLLs in `Libs/`: 0 errors, 5 warnings. **[verified]**
- `Libs/`, `bin/`, `obj/` and `Release/` are all gitignored, so no game
  assemblies are committed. The licensing rule in `general_wxr/CLAUDE.md` is
  satisfied. **[verified]**
- The game's input enum is `InputManager+KeyBind`, and the mod's string-keyed
  override dictionary is matched against `keyBind.ToString()`. **[reflected]**

## Boot sequence

`VRManager.Awake` (`Plugin.cs:34`) — the BepInEx entry point:

1. Probes for `Application.isBatchMode` and sets an environment variable if
   it is missing. This is vestigial; nothing BepInEx reads looks at it. **[code]**
2. `LoadConfig()` — reads `config.txt` from `BepInEx/plugins/IMVR/`, then the
   plugin root, then the DLL's own directory. Parses `dominant_hand`,
   `turn_speed`, `deadzone`, `flipHands`. **It does not parse `hudScale`** —
   that key is read independently and much later by
   `VRGlassAnchor.LoadHudScaleFromConfig` (`Patches.cs:345`), on its own
   2-second poll, from a different and longer list of candidate paths.
3. `XRAPI.XR_Init()` — handshake directory, UDP sockets, receive thread.
4. `harmony.PatchAll()`.
5. Creates a `DontDestroyOnLoad` `IMVR_InputRunner` holding `VRInputRunner`,
   whose `Update` is the mod's input and mode-switch tick.

The logger wiring at `Plugin.cs:49-52` assigns `LogInfo` twice — first to
`LogDebug`, then to `LogInfo`. The second assignment wins, so `LogDebug` is
unreachable through `VRManagerLogger`. Harmless, but it means there is no
debug-level channel. **[code]**

## XrApi client (`XRAPI.cs`)

Targets **XrApi 0.4**, as `general_wxr/CLAUDE.md` requires.

**Handshake.** Writes `0.4` into `Z:/tmp/xr/version`, and only if the file does
not already exist (`XRAPI.cs:63-91`). It never writes the `vr` file and never
reads `system`. **[code]**

**Inbound.** Binds the first free port of `{7872, 7873}` and breaks, so only
one socket — correct; binding both would double every packet. **[spec]** A
background thread blocks on `Receive` with a 500 ms timeout and parses ASCII.
The parser requires at least 30 tokens, skips a leading `client*` prefix, and
reads the 28-float layout, frame id, 19-character bool string and HMD name
exactly as `XRAPI_V0_4_DESC.md` section 3 describes. Trailing tokens are
ignored, which is the right call. Flags 19/20 (`IS_IMMERSIVE`, `IS_SBS`) are
not read.

**Pose accessors.** `XR_GetHeadPose` / `XR_GetControllerPose` return the last
packet under a lock, and once `hasData` is true it is never cleared — so the
mod freezes on the last good pose rather than zeroing, as it should. **[spec]**
There is no staleness test, so a dead stream looks identical to a still player.

**Outbound (port 7278).** `<L haptic> <R haptic> <MODE_VR> <MODE_3D> <FOVX> <FOVY>`,
sent from `SendHapticUpdate`, which runs every frame via
`VRInput.UpdateFrame` → `UpdateVRModeFromGameState`. The haptic fields are
**hardcoded to `0 0`** (`XRAPI.cs:210`) and the FOV fields to `104.5 104.5`,
while `targetFovX` / `targetFovY` are assigned and never read (two CS0414
warnings).

**Mode switching.** `UpdateVRModeFromGameState` (`XRAPI.cs:380`) decides
in-game versus menu/paused from `GameManager.IsRunningGame`,
`Marine.LocalMarine`, `Marine.IsAlive`, `GameManager.IsPaused`, the cursor lock
state and `Time.timeScale`, then sends `MODE_VR = 1` or `MODE_VR = 0`, always
with `MODE_3D = 0` (mono — correct for this port's monocular goal). The `0` is
the problem; see FINDINGS #1.

## Per-level setup — `Helmet_InitFirstPersonCamera_Patch` (`Patches.cs:41`)

This single Postfix is where almost everything is wired, and it runs again on
every level load. In order:

1. `XRAPI.ForceVRMode()` — sets `MODE_VR = 1` immediately.
2. Destroys the previous `VRRoot` and makes a new one, deliberately *not*
   `DontDestroyOnLoad`. `VRRoot` is **not** a camera parent: it is a loose
   transform used only as a body-yaw reference, and it is positioned by
   `VRHeadTracker.LateUpdate` rather than by its owner.
3. Adds `VRHeadTracker` and `SyncPixelQuad` to `Helmet.playerCamera`
   (`WorldCamera`). **[reflected: `Helmet.playerCamera` exists]**
4. Disables renderers on layer 8 named `CH_Marine1pHelmet*` — the physical
   helmet shell, which becomes a black disc in front of a VR camera.
   Explicitly does *not* touch `Glass_HUD`.
5. Finds any transform named `*Glass_HUD*`, attaches `VRGlassAnchor`, and
   reparents it to the head camera **preserving its local pose** (an Animator
   drives the `helmetClose` pose; forcing identity causes flicker).

A second Postfix on the same method, `Helmet_VR_NearClip` (`Patches.cs:253`),
sets near clip `0.18`, far clip `80`, FOV `98`, nudges the camera up by
`(0, 0.08, 0.05)` — guarded so repeat calls cannot accumulate — and calls
`VRVisualTweaks.ApplyGlobal()`, which **force-enables linear fog** with
hardcoded distances and a blue-grey colour, plus `antiAliasing = 0`,
`shadowCascades = 0`, `vSyncCount = 0`. None of that is config-gated.

## Frame order

```
Update
  VRInputRunner.Update      -> VRInput.UpdateFrame()
                                 reads XRAPI state, fills overridesBool,
                                 turns the body by right stick X, then
                                 UpdateVRModeFromGameState() -> UDP send
  HUDCamera.Update          -> Postfix HUDCamera_VR_Shrink_Patch (once per instance)
LateUpdate
  VRHeadTracker.LateUpdate  -> parks VRRoot on the Marine, then sets camera
                               localPosition / localRotation from head pose
  VRWeapon.LateUpdate       -> places WeaponModel from the dominant controller
  VRGlassAnchor.LateUpdate  -> reparents the glass mesh, restores HUD state
  SyncPixelQuad.LateUpdate  -> re-places the quad
OnPreRender
  VRHUDAnchor.OnPreRender   -> copies head pose onto the HUD camera
```

`VRHUDAnchor` exists and looks correct, but **nothing ever adds it** —
`Patches.cs:103` is a comment explaining that adding it to `HUDCamera` caused
flicker, and the `AddComponent` call was removed. It is dead code kept as a
record. **[code]**

## Head tracking (`VRHeadTracker.cs`)

`Awake` and `Start` both capture the camera's local pose; `Start` is the one
that counts, because the near-clip Postfix has run by then. `LateUpdate` then:

- parks `VRRoot` at the Marine's position with yaw-only rotation;
- converts head position by negating Z, clamps the result to **0.30 m** with
  `Vector3.ClampMagnitude`, and adds it to the captured base position;
- converts head rotation as `Quaternion.Euler(-e.x, -e.y, e.z)` from the
  packet quaternion's Euler angles, and applies it as `baseLocalRot * hRot`.

Because the camera is a child of the helmet, which is a child of the Marine,
head rotation composes with body yaw for free. The handedness conversion is
done here *and separately* in `VRWeapon`, from raw packet values each time —
not once at ingest. See FINDINGS #4.

## Weapon (`VRWeapon.cs`)

Added by a Postfix on `RangedWeapon.AttachToCamera` (`Patches.cs:135`) — which
is the method that parents `WeaponModel` to the head camera in flat mode, so
`VRWeapon.LateUpdate` has to undo it. It walks up the model's parent chain
looking for a `VRHeadTracker` and, if it finds one, calls
`SetParent(null, true)` — to the scene root, **not** to `VRRoot`, which is
destroyed on level change and would take the gun with it.

Pose composition, the third iteration of this code:

```
p = headWorldPosition + bodyRotation * controllerPosition
r = bodyRotation * controllerRotation
```

Head *position* for height, **body** rotation for orientation. This is what
makes the gun stop swinging when the player looks around — the bug
`general_wxr/TIPS.md` cites by name from this project. The muzzle transform
gets rotation only, so the shot still originates from the body and the logical
fire path is untouched.

`flipHands=false` (Quest 3) applies a **roll** of 180° (`r * Euler(0,0,180)`);
`true` (Quest 2 / Pico) leaves it. Yaw 180 would aim the gun backwards.

## Input (`VRInput.cs` plus the blocking patches)

`VRInput.UpdateFrame` clears and refills `overridesBool`, a
`Dictionary<string,bool>` keyed by `KeyBind` names. **All 22 names the mod
writes are real `KeyBind` members** — `MoveForward`, `MoveBackward`,
`StrafeLeft`, `StrafeRight`, `Run`, `Jump`, `Crouch`, `Fire`, `Reload`, `Use`,
`ToggleSilencer`, `RateOfFire`, `ToggleFlashlight`, `ToggleHelmet`,
`Slot01`–`Slot03`, `MissionOverview`, and the `Gamepad*` duplicates.
**[reflected — no typos, nothing silently dead]**

The full enum also contains `Aim`, `GamepadAim`, `GamepadFire`, `ToggleLaser`,
`ThrowGrenade`, `LeanLeft`, `LeanRight`, `Slot04`, `PushToTalk`, `Next`,
`ToggleNames`, `ToggleChat` and others, none of which the mod binds.
**[reflected]**

Smooth turn is applied by rotating the `Character` transform directly from
right-stick X, not through a `KeyBind`.

Two layers of blocking, both gated on `inVR && LocalMarine != null && !IsPaused`:

- `InputManager.IsInput` / `IsInputDown` prefixes (`Patches.cs:157`, `:194`):
  a VR override that is `true` forces `__result = true`; menu-ish names
  (`Menu`, `Confirm`, `Cancel`, `Pause`, `Back`, `Select`) fall through to the
  original; a long `Contains` list of gameplay names is forced `false`; any
  other key present in the dictionary is forced `false`.
- `UnityEngine.Input` prefixes for `GetAxis`, `GetAxisRaw`, `GetKey`,
  `GetKeyDown`, `GetMouseButton`, `GetMouseButtonDown` (`Patches.cs:597-679`),
  because the game reads raw Unity input in places and bypasses `InputManager`
  entirely. The `Cursor.visible` test that used to gate these was removed —
  Winlator always shows a cursor, so the test leaked input every frame.

`InputManager.IsInputUp` exists and is **not** patched — deliberately. See
FINDINGS #8: patching it would invert every `Toggler`-driven bind.

### How the game reads each bind

Verified against the decompiled source. **[dnSpy]** This decides whether the
mod must publish an override as a one-frame **edge** or a held **level**, and
getting it backwards produces an input that silently does nothing.

| Bind | Read as | Mod publishes | Consumer |
|---|---|---|---|
| `MoveForward` / `Backward` / `StrafeLeft` / `StrafeRight` | `IsInput` | level | `Marine` |
| `Fire` | `IsInput` | level | `Marine` |
| `Jump` / `GamepadJump` | `IsInput` | level | `Marine` |
| `Run` / `GamepadSprint` | `IsInput` | level | `Marine` |
| `ThrowGrenade`, `LeanLeft`, `LeanRight` | `IsInput` | *unbound* | `Marine` |
| `Crouch` / `GamepadStance` | **`Toggler`** | level = hold-to-crouch | `Marine` |
| `Aim` | **`Toggler`** | *unbound, and blocked* | `Marine` |
| `ToggleFlashlight` / `ToggleLaser` | **`Toggler`** | level = hold-to-light | `WeaponLight` |
| `Reload` / `GamepadReload` | `IsInputDown` | edge | `Marine` |
| `ToggleSilencer` | `IsInputDown` | edge | `Marine` |
| `ToggleHelmet` | `IsInputDown` | edge | `Marine` |
| `RateOfFire` | `IsInputDown` | edge | `RangedWeapon` |
| `Slot01`–`Slot04` | `IsInputDown` | edge | `Marine` |
| `Use` / `GamepadUse` | `IsInputDown` **and** `IsInput` | edge, then level after 0.6s | `Marine` (hold-to-use) |
| `MissionOverview` | `IsInputDown` | level | `Marine`, HellWeek mode only |
| `ToggleChat`, `FriendsList` | `IsInputUp` | *unbound* | `GameUI` |

Two things fall out of this table:

- **`Use` is the subtle one.** `Marine` reads `IsInputDown` to begin an
  interaction and `IsInput` to keep a held button depressed, which is why
  `VRInput` publishes an edge and then re-asserts a level after 0.6s.
- **`Slot01`–`Slot04` are only in the dictionary on the frame of a switch.**
  On every other frame they are absent, so `IsInputDown` falls through to the
  real keyboard. Harmless on a headset, but it is a genuine leak in the
  blocking layer.


## HUD — read this before touching anything

The visor HUD is not a canvas drawn over the screen. It is a three-stage
render, and breaking any stage makes it vanish with no error:

1. NGUI UI is rendered by `CAM Camera` (parent `Fake 3D UI Root`) and by
   `HUDCamera` (parent `HUD UI Root`) into `HUDCamera.cleanToBendTarget`.
   Both spellings are correct and refer to the same object: the **field** is
   `cleanToBendTarget`, and `HUDCamera` then sets that RenderTexture's `.name`
   to **`"CleanToBlendTarget"`**, which is what a scene dump shows. **[dnSpy]**
   Use the field name in code and the object name when reading `hud_tree.txt`.
2. `HUDCamera` bends that texture via `bendMaterial` into
   `bendToDoubleGlassTarget`, then projects it with `doubleGlassMaterial` onto
   the glass mesh. **[reflected: both materials and both render textures
   exist, as do `hudScale`, `lastHudScale`, `glassIntensity`, `glassOffset`
   and `mainCanvasScaler`]**
3. The glass mesh `CH_Marine1pHelmetGlass_HUD` is rendered by `HelmetCamera`,
   a child of `WorldCamera`.

`VRGlassAnchor` (`Patches.cs:338`) is what makes this work in VR. Each frame it
reparents the glass mesh to the head camera while the visor is closed, and puts
it back under the original `Helmet` parent when the visor is open — so raising
the helmet hides the HUD exactly as it does in flat mode. Visor state is read
reflectively from `Helmet.currentVisorState`, comparing `ToString() == "Closed"`.
**[reflected: field exists, type `VisorState`]**

The rest of that class is a museum of failed shrink attempts, every one of
which is now a no-op that restores an original value: `hudScale`, UV tiling on
`doubleGlassMaterial`, the HUD camera frustum, `Camera.rect` (which produced a
cyan border), and `HUD UI Root` scale. The last one *worked visually* but broke
the bend pipeline, so the HUD appeared only while paused, and FIXED29 reverted
it. `hudScale` was read, validated, cached and then never applied. It now feeds
**`VRHudSafeArea`** (`VRHudSafeArea.cs`), which is the one mechanism that is not
a no-op: it inserts a stretched `RectTransform` between each HUD canvas and its
children and scales that about its centre, so edge-anchored widgets move inward
instead of staying pinned. The HUD is **uGUI** — `Assembly-CSharp` references
`UnityEngine.UI` **[reflected]** — which is why canvas-level scaling never moved
the anchored elements, and why `Docs/MetaAI/` calling it NGUI sent six attempts
down the wrong path.

`GUICompass_VR_HeadYaw_Patch` (`Patches.cs:550`) replaces the compass update so
it follows head yaw rather than body yaw, by invoking the private `UpdateTicks`
with four arguments. The three label fields and both height offsets it reflects
on exist. **[reflected]** The four-argument `UpdateTicks` signature could not be
verified here, because `GUICompass` depends on `UnityEngine.UI`, which is not in
`Libs/`. **[unverified]**

## Diagnostics

`HUDTreeDumper.cs` dumps the GameObject tree, every camera and selected fields
to the log and to `hud_tree.txt` beside the DLL, on **F9**, via a
`DontDestroyOnLoad` runner that deliberately ignores `verboseDebug`. This is
the most valuable tool in the repository and the reason the HUD work
progressed at all.

## In-headset tuning

Two values can be changed without a rebuild, which matters because the owner
tests on a standalone headset where each deploy is expensive:

- **`hudScale`** and every other config key are re-read from `config.txt` every
  2 seconds by `VRManager.PollConfig`, called from `VRInputRunner.Update`.
- **`screen_fov`** can additionally be stepped by 10 from inside the headset:
  pause the game, tap up or down. `VRFovTuner` (`VRFovTuner.cs`) reads the
  **arrow keys**, not the VR thumbstick, and that is not a shortcut — while
  paused the mod sends `MODE_VR = 0`, at which point the runtime sends no UDP
  packets at all, so `XRAPI` is frozen on its last packet and has no live stick
  data. The runtime's screen-mode input emulation maps the stick to the arrow
  keys, so the keyboard is the only live channel in that state. The raw-input
  block in `Patches.cs` already allows arrows through while paused.

  The new value is written back into `config.txt` — every matching line, not
  just the first, so a stale duplicate cannot let the 2-second poll undo it.

`VRManagerState.verboseDebug` is `false` and there is **no config key to turn
it on** — it can only be changed by recompiling. `general_wxr/CLAUDE.md` asks
for one verbose flag, off by default; the flag exists but has no switch.
