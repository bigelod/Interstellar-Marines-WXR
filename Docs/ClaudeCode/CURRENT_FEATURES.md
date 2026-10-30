# CURRENT_FEATURES.md — what IMVRMod does today

Written for the owner and the next contributor, as of **2026-10-30**.

This is the "what does it do" reference. The neighbouring files answer
different questions: [ARCHITECTURE.md](ARCHITECTURE.md) is *how* it works at
runtime, [STATUS.md](STATUS.md) is *how finished* each part is,
[FINDINGS.md](FINDINGS.md) is *what is wrong with it*, and
[NEXT_STEPS.md](NEXT_STEPS.md) is *what to do next*.

Verification marks, because the distinction has mattered repeatedly:

- **[device]** — seen working on a Quest 3 through WinlatorXR by the owner.
- **[built]** — compiles and reads correctly, not yet confirmed on hardware.
- **[dnSpy]** — the game-side behaviour was read from decompiled source in
  `Release/dnSpyResults` (gitignored, never committed).

---

## Head and view

| Feature | State | Notes |
|---|---|---|
| 6DOF head tracking | **[device]** | Position and rotation from the HMD, applied to `WorldCamera` |
| Lean | **[device]** | Clamped to **0.30 m** magnitude. No roomscale walking — the body is driven by the stick |
| Body yaw composition | **[device]** | The camera is a child of the helmet, which is a child of the Marine, so head yaw adds to body yaw for free |
| Handedness + normalisation | **[built]** | Converted **once, at ingest** in `XRAPI.ParseString`. Quaternions normalised unconditionally |
| No head recoil / bob / lurch / exhaustion | **[device]** | Four `RangedWeapon` view-rotation patches and three `Helmet` bob patches, all gated on `inVR` |
| Near/far clip and FOV | **[device]** | Near `0.18`, far `80`, vertical FOV `98` |

## Weapon and hands

| Feature | State | Notes |
|---|---|---|
| Weapon model at the controller | **[device]** | Head *position* for height, **body** rotation for orientation — so turning your head does not swing the gun |
| Shot still fires from the body | **[device]** | Only the muzzle *rotation* follows the hand; the logical fire path is untouched |
| Survives level changes | **[device]** | Detached to the scene root, never to `VRRoot` — `VRRoot` is destroyed per level and would take the gun with it |
| `flipHands` | **[device]** | `false` = Quest 3 (applies a 180° **roll**), `true` = Quest 2 / Pico. Roll, not yaw — yaw would aim the gun backwards |

## HUD

The visor HUD is the game's identity and the hardest-won part of this mod.

| Feature | State | Notes |
|---|---|---|
| HUD glued to the VR head | **[device]** | The glass mesh is reparented to the head camera each frame, preserving its Animator pose |
| Visor open hides the HUD | **[built]** | Restores the mesh to its original `Helmet` parent, so raising the helmet hides the HUD exactly as in flat play |
| HUD shrunk into the readable area | **[device]** | `VRHudSafeArea` gives the canvas content a smaller parent rect, so **edge-anchored** widgets move inward instead of staying pinned. Driven by `hudScale` |
| World-space markers left alone | **[device]** | Waypoint diamonds keep pointing at what they point at — skipped by name |
| Compass follows head yaw | **[built]** | Replaces `GUICompass.UpdateCompass`, which uses body yaw. The 4-argument `UpdateTicks` signature is confirmed **[dnSpy]** |
| Physical helmet shell hidden | **[device]** | Layer 8 `CH_Marine1pHelmet*` renderers disabled — otherwise it is a black disc in front of the camera |
| Flashlight beam follows the head | **[built]** | `WeaponLight`'s light is reparented to the head camera |

## Haptics

Dominant hand only. Durations are **frames**, topped up while a condition
holds and decayed one per frame; overlapping buzzes take the longer of the two.

| Trigger | Frames | State |
|---|---|---|
| Ammo counter drops (a round actually spent) | 6 | **[built]** |
| Weapon switch | 10 | **[built]** |

Driving the shot buzz off the ammo counter rather than the Fire input means
full-auto buzzes once per round, a dry trigger pull is silent, and a shot the
game refused produces no phantom kick.

## Presentation

| Feature | State | Notes |
|---|---|---|
| XrApi **0.4** client | **[device]** | One socket on 7872, 28-float layout, 19-char bool string |
| Freeze on dropped packets | **[device]** | Holds the last good pose rather than zeroing |
| `MODE_VR = 1` in gameplay | **[device]** | Immersive VR |
| `MODE_VR = 0` in menus and pause | **[device]** | **Deliberate** — see "Things that look wrong but are not" |
| `MODE_3D = 0` (mono) | **[device]** | Monocular 6DOF is the goal of this port, not a fallback |
| Red sync pixel | **[device]** | 150 px square, top-left, red channel carries the frame id, green stays 0 |
| `screen_fov` | **[built]** | How wide an angle the runtime paints the frame across. Does **not** have to match the rendered FOV — declaring less shows the same scene, shrunk toward the centre, at full resolution |

## Controls

Every bind below is a real `InputManager.KeyBind` member, and the edge/level
column was verified against the game's own code **[dnSpy]** — publishing an
override the wrong way round produces an input that silently does nothing.

| Action | Binding | Published as |
|---|---|---|
| Move | Left stick, or left thumb directions | level |
| Turn | Right stick X (smooth, `turn_speed`) | direct transform rotate, not a bind |
| Weapon switch | Right stick Y up/down, cycles Slot01→02→03, 0.35 s cooldown | edge, on the switch frame only |
| Fire | Right trigger | level |
| Jump | Right grip | level |
| Use / interact | Right A | edge, then level after a 0.6 s hold |
| Reload | Right B | edge |
| Crouch | Left grip (hold) | level → hold-to-crouch via `Toggler` |
| Sprint | Left thumb click | level |
| Helmet toggle | Left X | edge |
| Flashlight | Left Y (press to toggle) | level → hold-to-light via `Toggler` |
| Silencer | Left trigger double-tap (within 0.35 s) | edge |
| Fire mode | Left trigger hold > 0.6 s, 1 s cooldown | edge |
| Menu / mission overview | Left menu | level |
| **FOV adjust** | **Up / down arrows while paused** | steps `screen_fov` by 10 |

Mouse and keyboard are blocked during VR gameplay at two levels — the
`InputManager` entry points and six raw `UnityEngine.Input` methods — but stay
live in menus and while paused, so ESC and the console still work.

**Not bound:** `Aim`, `ThrowGrenade`, `ToggleLaser`, `Slot04`, `LeanLeft`,
`LeanRight`, `PushToTalk`. `Aim` is additionally *blocked*, so iron sights are
currently unreachable in VR — see FINDINGS #9.

## Configuration

`config.txt` sits next to the DLL. **Every key is re-read from disk every 2
seconds**, so values can be tuned with the game running.

| Key | Default | What it does |
|---|---|---|
| `dominant_hand` | `right` | Which hand holds the weapon and receives haptics |
| `turn_speed` | `80` | Smooth-turn degrees per second |
| `deadzone` | `0.20` | Stick deadzone |
| `flipHands` | `false` | `true` for Quest 2 / Pico controller orientation |
| `hudScale` | `0.62` | HUD shrink toward the centre. `1.0` disables the mechanism entirely |
| `screen_fov` | `104.5` | Angle the runtime paints the frame across. Lower = smaller and more central |

`screen_fov` is also adjustable **inside the headset**: pause, tap up/down. The
chosen value is written back into `config.txt`, so there is nothing to copy
down afterwards.

## Diagnostics

| Tool | How |
|---|---|
| Scene / camera / field dump | **F9** → `hud_tree.txt` next to the DLL. Ignores the verbose flag deliberately |
| Safe-area decisions | Logged on first scan: every canvas and child adopted or skipped |
| Camera's real FOV | Logged once per level — vertical, horizontal and aspect — for comparison against `screen_fov` |
| `screen_fov` changes | Logged on each step |

`VRManagerState.verboseDebug` gates the rest and has **no config key** — it can
only be changed by recompiling. See FINDINGS #12.

---

## Things that look wrong but are not

Each of these has been proposed as a fix at least once, and each would break
something that currently works. Read before "tidying" any of them.

**`MODE_VR = 0` in menus is correct.** The spec describes `0` only as
"disabled — the runtime sends no UDP data at all", which reads like a bug. On
device it is the better choice: it stops a pose stream nothing is consuming,
restores the runtime's own mouse control of the screen, and drops the frame out
of immersive presentation so a whole menu is readable at once. It is also what
makes the in-headset FOV tuner possible, since the runtime maps the thumbstick
to arrow keys in that mode. Folded back into `Docs/general_wxr/`.

**`InputManager.IsInputUp` must stay unpatched.** `Toggler` — which drives
`ToggleFlashlight`, `ToggleLaser`, `Aim` and `Crouch` — flips its state on the
*release* edge. Because the mod never fakes a release, `isToggled` stays
`false` forever and the logic collapses to clean hold-to-activate. Patching
`IsInputUp` inverts all four. FINDINGS #8.

**`ToggleFlashlight` is published as a held level, not an edge.** That looks
inconsistent with every other momentary action in `VRInput.cs`. It is correct
for the same `Toggler` reason; the "obvious" edge fix would light the torch for
a single frame. FINDINGS #10.

**`hudScale` does not scale the canvas.** Scaling the canvas, its
`CanvasScaler`, the HUD camera frustum, its `rect`, the glass mesh, or
`HUD UI Root` have all been tried and all failed — the last one breaks the bend
pipeline so the HUD only appears while paused. The HUD is uGUI, and
edge-anchored widgets move only when their **parent rect** changes. FINDINGS #11.

**The weapon uses body rotation, not head rotation.** Using head rotation looks
more natural in the code and makes the gun swing whenever you look around —
the bug `Docs/general_wxr/TIPS.md` cites by name from this project.

**`VRRoot` is deliberately not `DontDestroyOnLoad`.** It is recreated per
level. Parenting the weapon to it instead of the scene root destroys the gun on
level change.
