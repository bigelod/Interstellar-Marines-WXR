# CURRENT_FEATURES.md — what IMVRMod does today

Written for the owner and the next contributor, as of **2026-11-03**.

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
| Near/far clip and FOV | **[device]** | Near `0.05`, far `45`, vertical FOV `98`. Fog is derived from the far clip, 45%-95% of it, so the view is walled off by murk just before the clip plane instead of having geometry pop through it |

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
| HUD rendered at 2x | **[built]** | `hud_supersample` writes `HUDCamera.hudScale`, which is a **supersample factor**, not a size: the game sizes both HUD render targets at `Screen * hudScale` and divides the canvas scaler's reference resolution by the same number. Same fit, twice the pixels per glyph |
| Interaction ray comes off the hand | **[built]** | Brackets, hint text and the Use action all raycast from the dominant hand rather than `Camera.main`. See "Interaction" below |

## Interaction

Both halves of the game's interaction read `Camera.main` — the head:

- `Marine.ProcessInput` raycasts from `Camera.main` over `pushButtonRange` against
  `ButtonLayerMask` and calls `UseButton.Toggle` / `ToggleRoot`. It is **inlined** in a
  very large method, so there is no method to patch.
- `GUIInteracter.UpdateRaycast` runs the same ray, plus a 0.25 m `SphereCastAll`
  fallback, to drive the brackets and the `[E]` hint.

In flat play the crosshair *is* the head, so one ray serves both. In VR, reaching for a
wall panel while watching the corridor is the normal thing to do, so the ray comes off
the dominant hand instead.

| Piece | State | Notes |
|---|---|---|
| Hand ray | **[built]** | `VRHands.GetDominantPose` — the same frame the weapon model is placed in, so the ray leaves the muzzle |
| Brackets and hint | **[built]** | `GUIInteracter.UpdateRaycast` replaced. Calls the game's own private `GetUseButton`, so door-power and team-mate rules are unchanged |
| The Use action | **[built]** | The Use **edge** is withheld from the game in VR (`InputManager_IsInputDown_Patch`) and `VRHandInteract.OnUsePressed` performs the toggle, replicating `ProcessInput`'s branch exactly |
| Press-and-hold panels | **[built]** | The Use **level** still goes through untouched — `Marine` clears `buttonBeingHeldDown` when `IsInput(Use)` goes false, and that is what releases a held panel |
| Fallback | **[built]** | No hand pose (no packets, no VR frame) falls back to the head camera, so interaction never becomes unreachable |

## Shadows and lights

Only the player's helmet lamp casts shadows. `VRLightTamer` sweeps every `Light` in the
scene every 2 s and sets `shadows = None` on all of them except the ones under
`VRHeadLamp.LampRoot`, which get `Hard`.

It is a sweep rather than a patch because lights are spawned by the level, by prefabs,
by weapon fire and by `ToggleOcclusion` turning rooms back on, with no common entry
point. It writes **only** `Light.shadows` — never `Light.enabled`, which is
`ToggleOcclusion`'s business and where the pitch-black screen comes from.

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
| Red sync pixel | **[device]** | **10 px** square, top-left, red channel carries the frame id, green stays 0. It was 150 px until 3 Nov, which worked and was also plainly visible to the player; `sync_pixel_size` can raise it again |
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
| Use / interact | Right A | **level only** — the edge is withheld and the mod performs the toggle itself, off the hand ray |
| Reload | Right B | edge |
| Crouch | Left grip (hold) | level → hold-to-crouch via `Toggler` |
| Sprint | Left thumb click | level |
| Helmet toggle | Left X | edge |
| Flashlight | Left Y (press to toggle) | level → hold-to-light via `Toggler` |
| Silencer | Left trigger double-tap (within 0.35 s) | edge |
| Fire mode | Left trigger hold > 0.6 s, 1 s cooldown | edge |
| Menu / mission overview | Left menu | level |
| **Tuning menu** | **Pause → click "VR Settings"** | clickable overlay, see below |

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
| `render_width` / `render_height` | `1280` / `960` | Render resolution, applied once at startup. `0` leaves the window alone |
| `hud_supersample` | `2.0` | HUD render-target supersampling. Same fit, more pixels. First thing to wind back if frames are short |
| `far_clip` | `45` | View distance. The fog follows it |
| `occlusion_culling` | `1` | Back on as of 3 Nov. `VRClipGuard` still forces it off while the camera is inside geometry |
| `shadow_distance` / `shadow_resolution` | `15` / `0` | Govern the helmet lamp, which is the only light in the level that casts shadows at all |
| `sync_pixel_size` | `10` | Edge length of the red patch, in framebuffer pixels |

### In-game tuning menu

**Pause the game and click "VR Settings".** Eye height, eye forward, hand
height, near clip, HUD scale, screen FOV, turn speed and deadzone each get a
`-` / value / `+` row. Every change applies immediately and is written back to
`config.txt`, so nothing needs copying down and nothing is lost on exit.

It is **clickable rather than key-driven** for a specific reason: it only shows
while paused, and while paused the mod sends `MODE_VR = 0`, which hands screen
control back to the runtime's own pointer emulation — so the mouse works. An
earlier arrow-key tuner produced no log output at all in testing, suggesting
the thumbstick does not emit arrow keys on this setup. The pointer is the
channel known to work in that state.

It is drawn with IMGUI, straight to the backbuffer after every camera, so it
cannot disturb the HUD bend pipeline. The panel is confined to the central 62%
of the frame, which is the region measured as actually visible in the headset.

The arrow-key tuner is still present and harmless, but the menu supersedes it.

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

**`HelmetOccluder` did not cause the pitch-black screen, and its renderer must
stay enabled.** A comment in `Patches.cs` claimed it did and told us to hide it;
on 2 Nov 2026 that was implemented, and the live `hud_tree.txt` immediately
disproved the claim:

```
HelmetOccluder layer=14  mat=Invisible_ShadowCaster
CAM WorldCamera  cullingMask=-123909  (Default,TransparentFX,...)   <- no UIOverlay
CAM HelmetCamera cullingMask=16384 (UIOverlay) clear=Depth parent=WorldCamera
```

Layer 14 is `UIOverlay`, drawn **only** by `HelmetCamera`, which draws the HUD
glass pass. `WorldCamera` cannot see the object at all, so it cannot black out
the world. Nor can it feed Unity's occlusion culling — it is spawned at runtime,
and only geometry present at bake time contributes to occlusion data. It is part
of the helmet/HUD pipeline; disabling it is exactly the tidying this document
exists to prevent. Its one real reach into the world was shadow casting
(`Invisible_ShadowCaster` around the head darkens the player's surroundings),
and that is now switched off specifically. `occluder_scale` exists to let the
owner try 2x on device, but it scales an invisible mesh in the HUD pass.

**Occlusion culling in this game switches the lights off, and that is the
blackout.** `ToggleOcclusion` (`Release/dnSpyResults/ToggleOcclusion.cs`) hangs
off a renderer and does this:

```csharp
private void OnBecameInvisible() {
    foreach (Light light in this.unityLights)           light.enabled = false;
    foreach (GameObject go in this.occludedGameObjects) go.SetActive(false);
}
```

When culling hides a room's marker renderer, every light in that room is
disabled. The geometry is still being drawn — it is simply unlit. That explains
what culling-everything never did: why `occlusion_culling=0` cured it on device,
and why the darkness **survived a respawn**, since nothing re-enables those
lights until the marker renderer becomes visible again.

`occlusion_culling` was therefore defaulted to **0** on 3 Nov, with `far_clip`
lowered to 60 to pay for it. On 3 Nov the owner confirmed on device that **1
behaves**, so it is back on — culling is what skips the draw calls, and 0 costs
frames rather than saving them. `VRClipGuard.cs` is now load-bearing rather than
a safety net: driven from `VRHeadTracker.LateUpdate`, it linecasts from the
Marine's chest to the final camera position each frame (layer mask built by
**name**, not index) and forces culling off while that line is blocked, which is
the only time culling can strand the player in the dark.

`far_clip` is **45** independently of that — this is a horror game and the threats
are close. The fog is tied to it (`VRVisualTweaks.ApplyFog`, reapplied from
`VRClipGuard` so a live `far_clip` change moves the fog with it) and runs
45%–95% of the distance, so geometry is fully fogged before it reaches the clip
plane. The previous fixed 25–75 m range *ended beyond* a 45 m clip, which meant
the fade never finished and everything popped in at full contrast.

**Do not collapse the far clip to "bound the cost" of running unculled.** That
was this file's first answer and it is backwards: `OnBecameInvisible` fires for
**frustum** culling too, so a far clip that snaps shut switches *more* lights
off, not fewer. A low steady `far_clip` is fine — the player walks through it
predictably — but it must not be modulated per frame.

**The red damage flash is an IMGUI-per-camera bug, not a shader problem.**
`MarineController.OnGameUI` (`MarineController.cs:690`) draws the took-damage
flash as one full-screen `GUI.Box` tinted `tookDamageOverlayColor`, alpha
`0.25`. IMGUI is not camera-bound: Unity runs the GUI pass **once per camera**,
and the 2 Nov dump lists seven enabled cameras. Seven coats of 0.25 is
`1 − 0.75⁷ ≈ 0.87` — very nearly opaque. On a monitor that reads as "the screen
went red"; filling a headset's whole field of view it reads as the lights going
out.

In VR the sheet is therefore clamped to **alpha 0** — suppressed outright, rather
than patching `OnGameUI`, which also draws the respawn prompt and spawn
countdown — and replaced with two red bands in peripheral vision, the approach
proven in Shogo-VR (commit `ab012bc`). They differ from the Shogo version in
four ways: mono rather than one band per eye; alpha-blended, since Shogo's were
opaque only because that 2D API had no alpha; width *and* opacity scale with the
fade; and they are positioned against the **visible area** (`pain_bar_inset`,
keep in step with `hudScale`) rather than the framebuffer, because bands at the
true screen edge would be outside the player's view entirely.

Critically, they are drawn from the mod's own overlay camera in
`SyncPixelQuad.OnPostRender` — the one place in the build that runs **exactly
once per frame**, which is the specific thing the game's IMGUI flash got wrong.
They are painted *before* the sync pixel, and that ordering is now load-bearing:
`pain_bar_inset` **defaults to 1.0** as of 3 Nov, confirmed on device — the panel
is wider than it is tall, so the vertical crop is the binding one and the full
frame width is in view — which puts the left band's edge on `x = 0`, exactly over
the corner the runtime reads the frame id from.

**The sync pixel must not ride a game camera.** It used to be a component added
to the highest-depth camera with its quad parented to that camera's transform.
On 2 Nov that camera was `PostBloomEffect`, whose parent is `MainMenu(Clone)` —
so a respawn destroyed it, and a never-reset `static bool alreadyCreated` meant
nothing ever rebuilt it. It now owns a `DontDestroyOnLoad` GameObject with its
own `Camera` (depth 1000, `clearFlags = Nothing`, `cullingMask = 0`) purely to
get an `OnPostRender` after every game camera, and paints the patch in screen
space with immediate-mode `GL` — no texture sampling, no blending, no filtering,
which is what the XrApi spec requires since any blend corrupts the value the
runtime reads back.

**`VRRoot` is deliberately not `DontDestroyOnLoad`.** It is recreated per
level. Parenting the weapon to it instead of the scene root destroys the gun on
level change.
