# FINDINGS.md — defects found by reading the code against the spec

Audit date **2026-10-30**, against `Docs/general_wxr/` and the shipped game
assemblies. Ranked by consequence. Nothing here has been confirmed on a
headset; each entry says how to confirm it cheaply.

Marks: **[code]** read from source, **[reflected]** checked against the game
assemblies, **[spec]** from `general_wxr`, **[unverified]** believed not proven.

---

## 1. ~~`MODE_VR = 0` is an off switch, not a flat-screen mode~~ — NOT A DEFECT

**Resolved 2026-10-30. The code is correct and this audit was wrong.**

I flagged `MODE_VR = 0` in `SendFirstFrame` (`XRAPI.cs:185`) and
`UpdateVRModeFromGameState` (`XRAPI.cs:442`) as an accidental off switch,
because `XRAPI_V0_4_DESC.md` described `0` only as "disabled — the runtime
sends no UDP data at all" and warned that a stray `0` looks broken.

The owner has tested this on a Quest 3 and `0` is the **deliberate and better
choice for menus**:

- the pose stream stops, which is correct when nothing is consuming it;
- the runtime's own mouse control of the screen comes back, so flat menus are
  usable with no extra work;
- the display leaves immersive presentation, so the player sees the whole
  screen at once instead of reading a menu through a VR projection.

`2` and `3` are for when you want a flat window *and* still want the pose data.
This port does not.

That is a general protocol fact, not a per-game one, so it has been folded back
into `Docs/general_wxr/` as its `CLAUDE.md` asks:
`XRAPI_V0_4_DESC.md` section 4 (the `MODE_VR` table), `FIRST_STEPS.md` stage 7,
and a new row in the `README.md` symptom table. All three now cite this port as
the source.

The two cautions that survive, and that the code already satisfies: keep
sending the outbound packet every frame or the stream cannot be turned back on
(`UpdateVRModeFromGameState` runs every frame), and hold the last good pose
across the gap when returning to `1` rather than zeroing (`hasData` is never
cleared, so it does).

---

## 2. The sync pixel camera selection is fragile — DEFERRED

**Status: the owner reports the sync pixel working well on device at last
test, and is re-checking it on the next one. Do not act on this yet.** The
code observations below stand on their own, but they predict a failure the
owner has not seen, so the reading is more likely wrong than the hardware.
Read it as "what to look at if sync ever misbehaves", not as a bug to fix.

**`SyncPixelQuad.cs:55-101`.**

`FindTopCamera()` picks the enabled camera with the greatest `depth` and
parents the quad to it. Three problems compound:

1. **`HUDCamera` renders to a `RenderTexture`** (`cleanToBendTarget`)
   **[reflected]**. A quad parented to it is drawn into that texture and never
   reaches the window, so the runtime samples whatever is actually at (0,0) of
   the real frame. The pixel is then silently absent.
2. **The choice is a coin flip.** Per `Docs/MetaAI/`, `HUDCamera`,
   `HelmetCamera` and `CAM Camera` all sit at depth 1 while `WorldCamera` is at
   depth 0. `FindTopCamera` uses a strict `>`, so with three cameras tied at
   the maximum the winner is whichever `Camera.allCameras` happens to return
   first — and it is re-evaluated every `LateUpdate`.
3. **`CAM Camera` is the NGUI camera and is orthographic.** The sizing maths at
   `SyncPixelQuad.cs:89` uses `Mathf.Tan(cam.fieldOfView * 0.5f)`, which is
   meaningless for an orthographic camera, so if that camera wins the quad is
   mis-sized as well as mis-placed.

Fix: select the highest-depth enabled camera **with `targetTexture == null`**,
prefer a deterministic tie-break, and handle `cam.orthographic` in the sizing
maths. The quad must land on the camera that actually writes the window.

**Confirm:** `HUDTreeDumper` on F9 already prints every camera; log
`cam.name`, `cam.depth` and `cam.targetTexture == null` from `PlaceQuad` once.

---

## 3. The sync pixel is lost after the first level change — DEFERRED

**Same status as #2: deferred pending the owner's next device test.** Of the
two sync findings this is the more concrete, because it is a plain reading of a
static that is never reset rather than an inference about camera ordering — and
its symptom is specifically "fine at first, degrades later", which a single
session might not surface. Worth checking across a level transition when sync
is next looked at.

**`SyncPixelQuad.cs:11-14`.**

```csharp
static bool alreadyCreated = false;
void Awake()
{
    if (alreadyCreated) { GameObject.Destroy(this); return; }
    alreadyCreated = true;
```

The guard is a `static` that is never reset. The quad is parented to the head
camera, which is destroyed with the level; `Helmet_InitFirstPersonCamera_Patch`
dutifully adds a fresh `SyncPixelQuad` to the new camera, and that instance
destroys itself because the static is still `true`.

So: sync pixel on the first level, **no sync pixel on every level after it**.
That matches the shape of "it felt fine at first and then got sickening",
which is worth asking the owner about directly.

Fix: drop the static and instead check whether a live quad already exists, or
reset the flag from the same patch that recreates `VRRoot`.

**Confirm:** load a level, note the quad, load a second level, look again.

---

## 4. Haptics are entirely dead code — IMPLEMENTED 2026-10-30, untested

**Status: written, builds, not yet tested on device.** The owner confirmed
haptics were never implemented and asked for a buzz on the dominant hand
whenever the ammo counter drops. What now exists:

- `XRAPI.SendHapticUpdate` interpolates the real `leftHapticFrames` /
  `rightHapticFrames` counters into the packet instead of the literal `0 0`.
- `XRAPI.TickHaptics()` decays both counters by one per frame, called from
  `VRInput.UpdateFrame` **after** the packet is sent, so a buzz set this frame
  is reported at full duration once before it starts running down. This is the
  model `general_wxr` prescribes: a duration in frames, topped up while the
  condition holds, never cleared by sending `0`.
- `VRWeapon.LateUpdate` reads `rw.GetAmmoCount()` **[reflected: `public int
  GetAmmoCount()` on `RangedWeapon`]**, compares against the previous frame,
  and calls `SendHapticVibration(dominantHand, 6)` on any decrease.

Driving it off the ammo counter rather than the `Fire` input is deliberate and
better than what the old README promised: full-auto buzzes once per round,
a dry trigger pull does not buzz at all, and a shot the game refused — out of
ammo, mid-reload — produces no phantom kick. A reload *raises* the count, so
only decreases count, and the first frame merely primes the baseline.

- `VRInput.SetSlot` buzzes the dominant hand for **10** frames on a weapon
  switch, so a switch and a shot are distinguishable by feel, and so drawing a
  weapon with fewer rounds is a drop the player can feel a reason for. Only a
  real stick-driven switch comes through `SetSlot`; the `Slot01` priming at
  spawn sets the override directly and does not buzz.

Overlapping buzzes are safe: `SendHapticVibration` takes the max of the pending
and the new duration, so a switch during a burst reads as one longer buzz
rather than two competing ones.

Note that a switch cannot in fact produce a *spurious* shot buzz in this
design, because `VRWeapon` is a per-weapon component and each instance tracks
its own `lastAmmo` — the newly drawn weapon compares against its own previous
count, not the one you just put away. The switch haptic is therefore a feature
in its own right rather than a mask for a false positive.

The two durations are `HAPTIC_SHOT_FRAMES` in `VRWeapon.cs` and
`HAPTIC_SWITCH_FRAMES` in `VRInput.cs`. If 6 reads as too weak or too long on
device, that is one constant; a shotgun or a heavier weapon might deserve more,
which would want `magazineSize` or the weapon name to pick a value.

Original finding, for the record:

**`XRAPI.cs:210`, and the whole call graph.**

```csharp
string msg = string.Format(CultureInfo.InvariantCulture,
    "0 0 " + modeVR.ToString() + " " + mode3D.ToString() + " 104.5 104.5");
```

The two haptic fields are string literals. `leftHapticFrames` and
`rightHapticFrames` are accumulated by `SendHapticVibration` and
`SendHapticBoth` and then **never read by anything that builds a packet**.

And `SendHapticVibration` / `SendHapticBoth` **are never called** from
anywhere in the mod — grep finds only their own definitions. **[code]**

The root `README.md` lists "Haptics on shoot (right controller 6-8 frames)" as
a working feature. It cannot be.

Fix, in this order:
1. Interpolate the two counters into the packet.
2. Decay them each frame (`general_wxr` is explicit that haptics are a
   **duration in frames**, that sending `0` does not clear a running buzz, and
   that the runtime stores only values greater than zero — so keep topping the
   number up while the condition holds and let it run down). **[spec]**
3. Call it. The obvious hook is the same place `Fire` becomes true in
   `VRInput.UpdateFrame`, plus a knock on damage via `Helmet`.

---

## 5. The window is never made borderless or forced to fill the display

Nothing in the repository touches the window: no `Screen.SetResolution`, no
`Screen.fullScreen`, no `WS_CAPTION` stripping. **[code, by grep]**

`general_wxr/CLAUDE.md` lists this as a non-negotiable, and
`TIPS.md` explains why it is more than cosmetic: *"A title bar does not merely
look wrong — it moves the top-left corner, so the sync pixel lands on chrome
and head tracking silently degrades."* It also names this as the single most
common cause of "I implemented sync and nothing changed".

Mitigating: the setup instructions tell the user to run at 1400x1400 through
WinlatorXR, and the container may already present the window without chrome.
So this may be fine in practice — but it is unverified, it is not enforced, and
it interacts directly with findings #2 and #3.

`Docs/MetaAI/` records that an earlier attempt at `Screen.SetResolution` plus
render-target resizing broke the HUD badly (FIXED23-25). That is a reason to be
careful about **resolution**, not a reason to skip **borderless**. Treat them
as separate problems.

**Confirm:** one screenshot of the game window as WinlatorXR captures it.

---

## 6. Quaternions are never normalised, and handedness is converted per-use — FIXED 2026-10-30, untested

**Status: fixed, builds, not yet tested on device.** Both conversions now
happen exactly once, at ingest, in `XRAPI.ParseString`:

- `XRAPI.ToUnity(Quaternion)` normalises unconditionally and applies the
  right-to-left-handed flip as the component form `(-x, -y, z, w)`.
- `XRAPI.ToUnity(Vector3)` negates Z.
- `VRHeadTracker` and `VRWeapon` (including its fallback head reconstruction)
  now consume `XR_GetHeadPose` / `XR_GetControllerPose` directly, with the
  duplicated per-use conversions deleted and a comment on each saying not to
  re-add them.

`flipHands` stays in `VRWeapon` and is still correct to test there — it is a
per-device controller *orientation* quirk, not a handedness swap, so the
"never test the flag again" rule does not apply to it. Hand *dominance* is
still tested once, in `VRWeapon`, which is the single test the rule allows.

**What to watch for on device.** The new conversion is mathematically
equivalent to the old one for rotation about a single axis — pure yaw, pure
pitch and pure roll all come out identical, which I checked by hand — and
differs only once two axes combine. So pass/fail should be unchanged for simple
motion, and the test that distinguishes them is **tilting your head sideways
while looking up**, for both the view and the gun. If something looks wrong
that used to look right, this change is the first suspect.

Original finding, for the record:

**`XRAPI.cs:261-268`, `VRHeadTracker.cs:28-29`, `VRWeapon.cs:25-26`.**

Two separate rules from `general_wxr/CLAUDE.md`, both broken:

**"Normalise every quaternion on ingest, unconditionally."** `ParseString`
stores the four floats raw. The doc warns that tester packets are *"often
several percent long"*, and the listed symptom — *"a hand's basis is stretched
or asymmetric"* — is a non-unit quaternion. This also matters because the code
immediately takes `.eulerAngles` of these quaternions, which is not meaningful
for a non-unit value.

**"Swap handedness once, at ingest. Then never test the handedness flag again
for anything spatial."** The conversion happens in at least three places, all
from raw packet values, all spelled the same way:

```csharp
Vector3 e = r.eulerAngles;
r = Quaternion.Euler(-e.x, -e.y, e.z);
```

Two concerns beyond the duplication:

- This is a decompose-negate-recompose, not a mirror. The correct
  right-to-left-handed flip for a quaternion is the direct component form the
  root `README.md` actually claims the code uses — `(-x, -y, z, w)`. Going
  through Euler angles applies Unity's own rotation order on the way out, so
  the two agree for a single-axis rotation and **diverge once pitch and roll
  are combined**. Which is exactly the regime where tilting your head sideways
  while looking up feels wrong. **[unverified, and cheap to test]**
- The duplication means the conversion can drift between head and hands, as it
  already has once: `VRWeapon` fell out of step with `VRHeadTracker` through
  the FIXED1-3 iterations.

Fix: normalise in `ParseString`, convert to Unity's frame there too, and have
`XR_GetHeadPose` / `XR_GetControllerPose` hand out already-correct poses.
Then delete the per-use conversions. Do this as its own change — it touches
every spatial path in the mod.

---

## 7. The reported FOV does not match the FOV the game renders with — NOW TUNABLE 2026-10-30

**Status: `screen_fov` added to `config.txt`, live-reloaded every 2s. Builds,
not device-tested.**

This turned out to be more interesting than a correctness fix. The owner
reports that only roughly the central 45-55% of the rendered frame is visible
in the headset, with everything outside it effectively unseen, and proposed
shrinking the whole game render into that region.

`FOVX` / `FOVY` tell the runtime **how wide an angle to paint the captured
frame across** — and nothing requires that number to match what the camera
actually rendered. Declaring *less* than the true FOV shows the same scene,
shrunk toward the centre, **at full render resolution**. That is the same
visible result as shrinking the render into a sub-rect, without the ~64% pixel
loss, the clear-colour border, or moving the sync pixel.

So `screen_fov` is now a config knob, defaulting to the historical `104.5` so
this deploy changes no behaviour until it is turned down. The camera's real FOV
(vertical, horizontal and aspect) is logged once per level for comparison.

Two bugs fixed on the way:

- Both senders now build the packet through `XRAPI.BuildOutPacket`, which
  formats with `InvariantCulture`. The hardcoded `"104.5"` was a string literal
  and therefore safe; the moment it became a float, `targetFovX.ToString()`
  under a comma-decimal locale would have emitted `104,5` — splitting one field
  into two and corrupting every field after it.
- `targetFovX` / `targetFovY` are finally read, clearing both CS0414 warnings.

Original finding, for the record:

**`XRAPI.cs:210` sends `104.5 104.5`. `Patches.cs:265` sets
`playerCamera.fieldOfView = 98f`.**

`general_wxr` says these fields are *"the FOV the game is actually rendering
with, so the runtime can warp correctly"*, in **degrees**, applied only when
greater than 1. **[spec]** `104.5` is the value HWXR and `opentrackWXR` send
for their own cameras; it is not this game's value, and Unity's
`fieldOfView` is **vertical**, so the horizontal figure differs again by
aspect ratio.

The consequence is a warp that does not match the render — the world appears
slightly the wrong scale and shears when you turn. It is subtle, it is the kind
of thing that reads as "VR just feels a bit off", and it is a one-line fix.

Fix: send the camera's real values. `targetFovX` / `targetFovY` already exist
as fields for exactly this and are unused (CS0414 ×2). Set them from
`playerCamera.fieldOfView` and the aspect ratio when the camera is configured.

---

## 8. `InputManager.IsInputUp` is unpatched — DO NOT "FIX" THIS

**Recommendation reversed 2026-10-30 after reading the game source. Leaving
`IsInputUp` unpatched is load-bearing, and patching it would break four
working inputs.**

The three input methods are **[dnSpy]**:

```csharp
IsInput(kb)     => Input.GetKey(GetKeyCode(kb))
IsInputDown(kb) => Input.GetKeyDown(GetKeyCode(kb))   // after gamepad axis bookkeeping
IsInputUp(kb)   => Input.GetKeyUp(GetKeyCode(kb))
```

The mod patches the first two. I previously recommended patching the third for
symmetry. That is wrong, because of `Toggler`:

```csharp
public bool IsInputToggled()
{
    if (InputManager.IsInputDown(keyBind)) keyDownBegin = Time.timeSinceLevelLoad;
    else if (InputManager.IsInputUp(keyBind)
             && Time.timeSinceLevelLoad - keyDownBegin < toggleThreshold)
        isToggled = !isToggled;

    if (InputManager.IsInput(keyBind)) return !isToggled;
    return isToggled;
}
```

`Toggler` drives **`ToggleFlashlight`**, **`ToggleLaser`** (`WeaponLight`),
**`Aim`** and **`Crouch`** (`Marine`). **[dnSpy]**

The state flips on the **release** edge. Because the mod never fakes a release,
`isToggled` stays `false` forever, and the whole thing collapses to the
`IsInput(keyBind) ? !isToggled : isToggled` line — a clean hold-to-activate.
That is exactly the behaviour the mod wants, and it is why holding left grip
gives hold-to-crouch today.

Patch `IsInputUp` to report the override's falling edge and every one of those
four inverts: releasing flips `isToggled` to `true`, after which `IsInput`
false returns `true` and the light stays on, the next press reads as off, and
the player is permanently out of phase.

So: **leave it unpatched.** The residual risk is only that a real keyboard
keypress can still reach a release-triggered action. In a headset there is no
keyboard, and `GameUI` uses `IsInputUp` only for `ToggleChat` and the Steam
friends list — neither of which the VR path drives.

If this is ever revisited, the correct move is to fake a *complete* press and
release cycle through all three methods, not to patch one in isolation.

---

## 9. `Aim` is blocked and never provided — iron sights are unreachable in VR

**`Patches.cs:181` and `VRInput.cs`.**

The block list contains `name.Contains("Aim")`, so `Aim` and `GamepadAim` are
forced `false` during VR gameplay. **[reflected: both are real `KeyBind`
members.]** Nothing in `VRInput` ever sets either.

Net effect: aiming down sights is impossible in VR, by construction. For a
tactical shooter whose whole identity is deliberate aiming, that is a feature
hole rather than a bug, but it is invisible in the code and worth stating.

The same reasoning applies to `ThrowGrenade`, `ToggleLaser`, `Slot04`,
`LeanLeft` and `LeanRight`, which are unbound — though those are not blocked,
so a keyboard still reaches them.

Suggestion: `Aim` on left trigger *hold* would be natural, but that is already
fire-mode. Left grip is crouch. The honest answer is that the left hand is out
of buttons and this needs the owner's call, not an agent's guess.

---

## 10. ~~`ToggleFlashlight` is driven as a level, not an edge~~ — NOT A DEFECT

**Resolved 2026-10-30 from the game source. The current code is correct, and
the "fix" I proposed would have broken it.**

I flagged `Set("ToggleFlashlight", flashlightOn)` (`VRInput.cs:118`) as
publishing a latched level where every other momentary action is edge
detected, and predicted a per-frame strobe.

`WeaponLight` does not read the bind directly — it owns a `Toggler`
(see finding #8 for the body). Tracing the mod's held override through it:

| Call | Result | Why |
|---|---|---|
| `IsInputDown` | `true` every frame | override is `true`, patch forces it |
| `IsInputUp` | `false` | unpatched, reads a keyboard nobody is touching |
| `IsInput` | `true` | override is `true` |

`isToggled` therefore never flips, and the method returns
`!isToggled` = `true`. The light is on for exactly as long as the mod holds the
override, and goes off the frame it stops. **It works.**

The proposed edge fix would have broken it: a one-frame `IsInputDown` with
`IsInputUp` never firing means `isToggled` never flips either, so the light
would have come on for a single frame and gone out.

No change needed. A comment has not been added to `VRInput.cs` because the
explanation belongs with finding #8, which covers all four `Toggler` binds.
---

## 11. `hudScale` in `config.txt` does nothing — RECONNECTED 2026-10-30, untested

**Status: a working mechanism now exists and `hudScale` drives it. Builds, not
device-tested.**

The root cause of every failed attempt was a misdiagnosis recorded in
`Docs/MetaAI/`: the HUD is called "NGUI" throughout those notes, but
`Assembly-CSharp` references **`UnityEngine.UI`** and `HUDCamera` has a
`mainCanvasScaler` — it is **uGUI**. **[reflected]**

That explains the symptom `BUG-FIXES.md` records as a dead end: *"Changing NGUI
Camera frustum + CanvasScaler.scaleFactor only shrank compass, bottom elements
edge-anchored stayed."* In uGUI a widget anchored to the bottom-left stays at
the bottom-left however you scale the canvas, because its position is defined
against its **parent rect**, not in absolute screen units. Scaling the canvas
scales the glyphs and leaves the anchors alone — exactly what was seen.

So the fix is not to scale anything the previous attempts scaled. It is to give
the content a **smaller parent rect**: `VRHudSafeArea.cs` inserts one stretched
`RectTransform` between each HUD canvas and its children and scales that about
its centre. Edge-anchored children move inward *and* shrink. It touches no
camera, no material, no culling mask and no render target, so the bend pipeline
cannot notice it — which is what distinguishes it from all six earlier attempts.

Canvases and children whose names look like world markers are skipped, so the
diamond waypoints keep pointing at the things they point at.

`hudScale=1.0` is a true no-op: nothing is installed at all. That is the escape
hatch if this misbehaves on device.

Original finding, for the record:

### Original finding

**`Patches.cs:460` assigns `vrHudScale`; nothing reads it afterwards.**

`LoadHudScaleFromConfig` is a careful piece of work — four candidate paths,
three accepted key spellings, range validation, a 2-second live-reload poll —
feeding a variable that FIXED29 disconnected when it reverted `HUD UI Root`
scaling. `config.txt` ships with `hudScale=0.8`, and the most recent commit is
titled *"HUD scaler works"*.

This is the sharpest README-versus-code gap in the project and the thing most
likely to waste the next session: the knob is present, documented and inert.

It is **not** a bug to fix by reconnecting it — `Docs/MetaAI/` establishes that
scaling `HUD UI Root` is what breaks the bend pipeline and makes the HUD appear
only while paused. The fix is either a different mechanism or an honest comment
saying the key is reserved. See `NEXT_STEPS.md`.

---

## 12. Smaller things

- **Unreachable code**, `Patches.cs:187-189` (CS0162): three statements after an
  unconditional `return true`, left over from an earlier version of the
  override logic. Delete.
- **The sync quad mutates the host camera's `cullingMask`**
  (`SyncPixelQuad.cs:27`, `:83`): `cam.cullingMask |= (1 << 0)`. If the chosen
  camera is `HUDCamera`, this adds the default layer to a mask that
  `Docs/MetaAI/AGENT-ADVICE.md` and `Patches.cs:318` both insist must not be
  touched, and the HUD camera would start rendering the whole world into its
  blend target. The quad is also on **layer 0**, the world layer, so it is
  visible to any camera that renders the world. `Docs/MetaAI/PROJECT-PLAN.md`
  records the original design as `syncLayer = 31` with `cullingMask = 1 << 31`;
  the current code is a regression from that. Give the quad its own layer back.
- **Lean clamp is 0.30 m, not 12 cm.** `VRHeadTracker.cs:26` clamps to `0.3f`;
  the root `README.md` says "Lean clamped to 12cm".
- **`VRHUDAnchor` is dead code** — nothing adds it (`Patches.cs:103` explains
  why). Either delete it or leave a one-line comment on the class itself; right
  now the explanation is in a different file from the class.
- **`verboseDebug` has no switch.** It is a compile-time constant in practice.
  Add `verbose=1` to `config.txt` parsing — `general_wxr/CLAUDE.md` asks for
  exactly one such flag, and `HUDTreeDumper` deliberately bypasses it on F9 so
  the useful dump is unaffected either way.
- **`XR_GetHeadPose`'s desktop fallback reads `Input.GetAxis("Mouse X")`**
  (`XRAPI.cs:317`), which the mod itself patches to `0` during VR gameplay. It
  only runs before the first packet arrives, so it is harmless, but it is a
  fallback that cannot work.
- **No `vr` handshake file.** `XRAPI_V0_4_DESC.md` section 2 lists `vr`
  (contents `VR`) as written by the game to declare it wants VR, and deleted on
  clean shutdown. The mod writes only `version`. From 0.2 onward the mode moved
  to UDP, so this is probably vestigial for a 0.4 client — but the runtime
  **wipes the handshake directory at startup** **[spec]**, which also means the
  "only write `version` if absent" guard at `XRAPI.cs:77` can leave the mod
  with no `version` file at all if the runtime restarts under it. Write it
  unconditionally; it costs nothing.
- **No `system` file read, so no headset detection.** `Z:/tmp/xr/system` line 2
  is the product codename — `EUREKA` = Quest 3, `HOLLYWOOD` = Quest 2,
  `SEACLIFF` = Quest Pro **[spec]** — which is precisely the information
  `flipHands` exists to supply by hand. Reading it would let the mod set
  `flipHands` itself and remove a config key the user currently has to guess.
  `general_wxr/README.md` also notes it is the cleanest "am I on a headset or a
  desktop?" test available. This is the highest-value small addition in the
  project.

---

## Corrections to the existing docs

`Docs/MetaAI/BUG-FIXES.md` and `AGENT-ADVICE.md` both state that
`RenderSettings.fogStartDistance` and `fogMode` *"don't exist in Unity 4"* and
that touching fog gave compile errors.

**That is wrong.** Reflection over the shipped `UnityEngine.dll` shows
`RenderSettings` exposing `fog`, `fogMode`, `fogColor`, `fogDensity`,
`fogStartDistance` and `fogEndDistance`. **[reflected]** `Patches.cs:27-35`
uses four of them and the project builds with no errors. The CS8024 / CS0708 /
CS0825 errors that session hit were from class-level statements and C# version
problems, not from the fog API — the fog block was removed for the wrong
reason.

**A correction to this audit, not to `Docs/MetaAI/`.** An earlier revision of
this file claimed `Docs/MetaAI/` misspelled the render target as
`CleanToBlendTarget` when the field is `cleanToBendTarget`. That was wrong, and
the dnSpy source settles it: **both are right.** `HUDCamera` declares the field
`cleanToBendTarget` and then sets that RenderTexture's `.name` to
`"CleanToBlendTarget"`. **[dnSpy]** So the field name is what you reflect on,
and the object name is what a scene dump prints. `Docs/MetaAI/` was quoting a
dump and was accurate.

The C# constraints those docs list are otherwise accurate and worth keeping:
`LangVersion 3` means no interpolated strings, no `?.`, no `var` outside a
method body, and `Path.Combine` takes two arguments.
