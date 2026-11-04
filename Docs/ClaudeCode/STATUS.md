# STATUS.md — where the port stands

Originally assessed 2026-10-30 by reading every source file and checking the
reflective calls against the shipped game assemblies. **Last updated 2026-11-03,
after three rounds of device testing on a Quest 3.**

The original audit's header said nothing in it had been confirmed on a headset.
That is no longer true, and the device tests have been more informative than the
code reading was — see [What the device tests settled](#what-the-device-tests-settled).

## Confirmed on device

| Setting | Value | When |
|---|---|---|
| `hudScale` | **0.60** | 2 Nov 2026 |
| `screen_fov` | **90** (vs 98 rendered) — proves the runtime consumes the field | 2 Nov 2026 |
| `occlusion_culling` | **0** cured the blackouts; **1 is fine** once they were fixed at source, so it is back on | 3 Nov 2026 |
| Black screens | **Gone**, and respawn is clean | 3 Nov 2026 |
| `pain_bar_inset` | **1.00** — the panel is wider than it is tall, so the full frame width is in view | 3 Nov 2026 |
| Sync pixel at 150 px | Read correctly, and was a large red square in the player's view. Now **10 px**, per spec | 3 Nov 2026 |

> **Changes made on 2026-10-30, after the owner reviewed the original audit.**
> All build clean, none were device-tested at the time:
>
> - **Handedness and normalisation moved to ingest** (FINDINGS #6) — one
>   conversion in `XRAPI.ParseString`, per-use copies deleted.
> - **Haptics implemented** (FINDINGS #4) — real counters in the packet, a
>   per-frame decay tick, and a buzz on the dominant hand when the ammo counter
>   drops (6 frames) or the player switches weapons (10 frames).
> - **FINDINGS #1 withdrawn** — `MODE_VR = 0` for menus is correct and
>   deliberate; the general docs were the thing that was wrong, and have been
>   corrected in `Docs/general_wxr/`.
> - **FINDINGS #2 and #3 deferred** — the owner reports the sync pixel working
>   on device and is re-checking next test. *(Superseded 3 Nov: #3 was confirmed
>   on device and is now fixed — see below.)*
>
> - **HUD scaling reconnected** (FINDINGS #11) — the HUD is confirmed visible in
>   gameplay, so FIXED29 holds. `VRHudSafeArea` now shrinks the screen-anchored
>   HUD toward the centre; the previous attempts failed because the HUD is uGUI,
>   not NGUI as `Docs/MetaAI/` says.

Branch `main`, last commit `e648e72` "ReadMe updated". **Nothing since has been
committed.** The working tree is well ahead of the last commit in substance: it
carries every device-driven fix from the 1–3 Nov tests plus nine new source
files (`VRClipGuard.cs`, `VRConfigFile.cs`, `VRHeadLamp.cs`, `VRHudSafeArea.cs`,
`VRInputGate.cs`, `VRPainOverlay.cs`, `VRPostFX.cs`, `VRTuneMenu.cs`,
`VRFovTuner.cs`), all untracked.

`Docs/` itself is untracked — this doc set and the two sets it sits beside are
not yet committed.

## Build

`dotnet build` → **0 errors, 3 warnings**, with the five DLLs present in
`Libs/`. The remaining warnings are cosmetic (one unreachable statement, one
unused `catch` variable, one write-only field). The two that pointed at real
findings are gone: `targetFovX` / `targetFovY` are now consumed via `screen_fov`
(#7).

The csproj carries `<Compile Remove="Release/**/*.cs" />` and the same for
`Docs/`. This is **load-bearing**: the decompiled game source lives in
`Release/dnSpyResults/`, and the .NET SDK globs `**/*.cs` by default, so without
those lines the build both breaks and would compile game code into the
redistributable.

## Subsystem status

| Subsystem | State | Notes |
|---|---|---|
| XrApi 0.4 client, inbound | **Working** | Correct packet layout, one socket, freezes on last pose. Normalisation and handedness now done once at ingest (#6). No staleness test |
| XrApi handshake | **Partial** | Writes `version` only if absent; no `vr` file; never reads `system`, so no headset detection (FINDINGS #12) |
| XrApi outbound | **All six fields real** | Haptics wired (#4); FOV now config-driven via `screen_fov` and formatted invariantly (#7) |
| Presentation mode switching | **Correct as written** | `MODE_VR = 0` for menus is deliberate — it stops the unused pose stream, restores runtime mouse control and drops out of immersive presentation (#1, withdrawn) |
| Sync pixel | **Rewritten 3 Nov** | #3 confirmed on device — it died on respawn. It rode the highest-depth camera (`PostBloomEffect`, a child of `MainMenu(Clone)`) and `static bool alreadyCreated` was never reset, so nothing rebuilt it. Now owns a `DontDestroyOnLoad` camera (depth 1000, clears nothing, `cullingMask = 0`) and paints via immediate-mode `GL`. #2 is moot — it no longer selects a camera at all |
| Window / borderless | **Partial** | Still no borderless enforcement, but `render_width` / `render_height` now apply a resolution once at startup (default 1280x960). The rest of #5 stands |
| Interaction ray | **Rewritten 3 Nov, untested** | Brackets, hint and the Use action all come off the dominant hand. `GUIInteracter.UpdateRaycast` is replaced; the Use **edge** is withheld and `VRHandInteract` performs the toggle, because the action itself is inlined in `Marine.ProcessInput` and cannot be patched as a method |
| Shadows | **Rewritten 3 Nov, untested** | Only the helmet lamp casts them (`VRLightTamer`); `shadow_resolution` defaults to Low |
| HUD text resolution | **Rewritten 3 Nov, untested** | `HUDCamera.hudScale` is a supersample factor, and the mod was pinning it to 1.0 — *below* the game's own 1.1 default. Now follows `hud_supersample`, default 2.0 |
| 6DOF head tracking | **Working** | Lean clamped to 0.30 m; yaw composes through the helmet parent. Handedness fixed at ingest (#6) — re-verify with head tilted sideways while looking up |
| Weapon / hands pose | **Working, unconfirmed on device** | Third iteration; head position for height, body rotation for orientation — the fix for the hands-swing-with-head bug `general_wxr/TIPS.md` names from this project. `Docs/MetaAI/CURRENT-PROGRESS.md` still lists it as unconfirmed |
| Haptics | **Implemented, untested** | Dominant hand, decayed per frame: 6 frames when `GetAmmoCount()` drops, 10 frames on a weapon switch (#4) |
| Input mapping | **Working** | All 22 `KeyBind` names verified to exist. `Aim` is blocked and unbound (#9); `ToggleFlashlight` is driven as a level, not an edge (#10) |
| Mouse / keyboard blocking | **Mostly working** | Both `InputManager` entry points plus six raw `UnityEngine.Input` methods. `IsInputUp` is unpatched (#8) |
| Helmet shell hiding | **Working** | Layer 8 `CH_Marine1pHelmet*` renderers disabled; `Glass_HUD` deliberately spared |
| HUD glue to VR head | **Working** | `VRGlassAnchor` reparents the glass mesh per frame, preserving the Animator pose; restores it when the visor opens |
| HUD scaling | **Working, confirmed on device** | `VRHudSafeArea` gives the canvas content a smaller parent rect, which is the one approach that moves edge-anchored widgets. `hudScale` drives it; **0.60 confirmed 2 Nov** (#11) |
| Occlusion culling / view distance | **Root cause found 3 Nov** | The blackout was never culled geometry — `ToggleOcclusion.OnBecameInvisible` *disables every light* in a room that culls out. `occlusion_culling` now defaults to **0**; `far_clip` lowered 80 → 60 to pay for it. `VRClipGuard` is the safety net if culling is turned back on |
| Damage feedback | **Replaced for VR, 3 Nov** | The game's full-screen flash is one IMGUI `GUI.Box` at alpha 0.25, and IMGUI runs once per *camera* — seven of them, so ≈ 0.87 opacity. Suppressed to alpha 0 and replaced with two peripheral red bands, per Shogo-VR `ab012bc`. Drawn from the mod's overlay camera, exactly once per frame |
| In-headset tuning | **Working, confirmed on device** | `VRTuneMenu` IMGUI panel, shown in menus or when paused. Every config key has a row; `VRConfigFile` writes each change straight back to `config.txt`, which is required because `PollConfig` would otherwise revert it within 2s |
| Helmet lamp | **Reworked 3 Nov** | One mod-owned copy parented to the head camera (`VRHeadLamp`); per-weapon `flashLightGO` forced off always. Fixes the stacking brightness the owner hit on 2 Nov, which was one lamp per weapon each reparented to the head |
| Compass | **Implemented, unverified** | Head yaw instead of body yaw; the four-argument `UpdateTicks` invocation could not be checked without `UnityEngine.UI` |
| No head recoil / bob / lurch | **Implemented** | Four `RangedWeapon` view-rotation patches and three `Helmet` bob patches, all gated on `inVR` |
| Flashlight reparenting | **Superseded** | See "Helmet lamp" above |
| Visual tweaks | **Implemented, not gated** | Forces fog on with hardcoded values, AA off, shadow cascades off, vsync off — no config key, no VR-only justification recorded for the fog. `VRPostFX` additionally re-asserts AA/TAA, SSAO and DoF off every 2s, since the game's own video menu does not persist them. TAA matters most: it jitters the projection matrix and blends the sync pixel |
| Diagnostics | **Good where it exists** | `HUDTreeDumper` on F9 is excellent. `verboseDebug` has no runtime switch (#12) |
| Menus / lobby in VR | **Not started** | Separate cameras at (-1000, 22, 0); currently handled by trying to leave VR entirely, which is finding #1 |

## Where the README and `Docs/MetaAI/` disagree with the code

> **The engine is Unity 5.6.7, not Unity 4.** BepInEx logs
> `Running under Unity v5.6.7.15207619` on startup. Every doc in this repo,
> this set included, previously said Unity 4 — inherited from `Docs/MetaAI/`
> and never checked. My own stated reason ("`UnityEngine.dll` is a CLR 2.0
> assembly") was bad evidence: that is the .NET 3.5 scripting runtime, which
> Unity 5.x also uses, and says nothing about the engine version.
>
> This matters because it changes what is available. uGUI, `RectTransform`,
> the PostProcessing stack v1, `CanvasScaler` and `Cursor.lockState` are all
> Unity 5-era APIs, and their presence had been treated as surprising. It also
> means `LangVersion 3` in the csproj is a deliberate project choice rather
> than an engine constraint — the C# 3 restrictions are real, but self-imposed.


Worth reading before trusting either. The code is what ships.

| Claim | Where | Reality |
|---|---|---|
| "Haptics on shoot (right controller 6-8 frames)" | `README.md` Features | Was never called and the packet field was a literal `0`. Now implemented, but driven by the **ammo counter** rather than the Fire input, so the README wording still needs updating (#4) |
| "MODE_VR=0 flat screen" | `README.md` How it works | Accurate. `general_wxr` described `0` only as "disabled" and has been corrected (#1) |
| "Lean clamped to 12cm" | `README.md` How it works | `0.30f` in `VRHeadTracker.cs:26` |
| "`VRWeapon.cs` — RH→LH `pos.z=-z, quat(-x,-y,z,w)`" | `README.md` Project files | The code converts through Euler angles, not quaternion components — which is not the same operation once pitch and roll combine (#6) |
| "`XRAPI.cs` — … `D:/xrtemp` fallback" | `README.md` Project files | No such path exists in the code; only `Z:/tmp/xr` |
| "Red pixel quad top-left (10x10)" | `README.md` How it works | 150 px, which is a deliberate and sound choice (the runtime samples one pixel; extra size survives a shifted corner and WinlatorXR's letterboxing) — but the doc and the code disagree. As of 3 Nov it is not a quad either: it is an immediate-mode `GL` rect, so "quad" is doubly wrong |
| "Post-processing disabled for VR: motionBlur, chromaticAberration, depthOfField" | `README.md` Features | No such code exists. `Docs/MetaAI/` records that `EnforceNoPostFX` was removed because it killed the HUD blend; the README still advertises it |
| "HUD scaler works" | commit `a514f71` | Was read and discarded. Now drives `VRHudSafeArea` (#11) |
| `RenderSettings.fogStartDistance` / `fogMode` "don't exist in Unity 4" | `MetaAI/BUG-FIXES.md`, `AGENT-ADVICE.md` | They do exist, the mod uses them, and it compiles. **[reflected]** See the corrections section of FINDINGS.md |
| `CleanToBlendTarget` | throughout `Docs/MetaAI/` | **Accurate** — that is the RenderTexture's `.name`; `cleanToBendTarget` is the field holding it. An earlier revision of this audit wrongly called it a misspelling **[dnSpy]** |
| "HUD only visible when paused (active bug)" | `MetaAI/CURRENT-PROGRESS.md` | Superseded. FIXED29 keeps `HUD UI Root` at 1.0, which is the stated fix for exactly that bug — so this entry describes the state *before* the current working tree |

## What the device tests settled

Three rounds on a Quest 3 (1, 2 and 3 Nov 2026) plus the decompiled game source
in `Release/dnSpyResults/` overturned more of this audit than the code reading
established. Each of these was diagnosed wrongly first, so the correction is
recorded alongside the answer.

**The pitch-black screen is lights being switched off, not geometry being
culled.** `ToggleOcclusion` (decompiled) hangs off a renderer and does this:

```csharp
private void OnBecameInvisible() {
    foreach (Light light in this.unityLights)           light.enabled = false;
    foreach (GameObject go in this.occludedGameObjects) go.SetActive(false);
}
```

When culling hides a room's marker renderer, every light in that room is
disabled. The geometry still draws — it is unlit. This explains three things the
culled-geometry theory never did: why `occlusion_culling=0` cured it on device,
why the darkness **survived a respawn** (nothing re-enables those lights until
the marker renderer is visible again), and why fixing it from the spawn-time
near-clip patch kept failing.

It also kills the obvious optimisation. Collapsing the far clip to "bound the
cost" of running unculled was this audit's first answer and it is backwards:
`OnBecameInvisible` fires for **frustum** culling too, so a far clip that snaps
shut switches *more* lights off. A low steady `far_clip` is fine — the player
walks through it predictably — but it must not be modulated per frame.

**`HelmetOccluder` was never the cause, and hiding it was a mistake.** A comment
in `Patches.cs` had claimed for months that it caused the black screen; on 2 Nov
that was finally implemented, and the live `hud_tree.txt` disproved it the same
run. It is layer 14 (`UIOverlay`), which only `HelmetCamera` draws, and
`WorldCamera`'s culling mask excludes that layer — so it cannot draw over the
world at all. Being runtime-spawned it cannot feed baked occlusion data either.
Its renderer is enabled again; only its shadow casting is off, which was its one
real reach into the world.

**The red damage flash is an IMGUI-per-camera bug.**
`MarineController.OnGameUI` draws it as one full-screen `GUI.Box` tinted alpha
`0.25`. IMGUI is not camera-bound — Unity runs the GUI pass once per camera, and
there are seven. `1 − 0.75⁷ ≈ 0.87`, very nearly opaque. On a monitor that reads
as "the screen went red"; filling a headset it reads as the lights going out.
Replaced with peripheral bands drawn from the mod's own overlay camera, which is
the one place in the build that runs exactly once per frame.

**`screen_fov` is consumed by the runtime.** 90 works on device against 98
rendered. This retired a planned rewrite: declaring a narrower FOV than the
camera renders shows the same scene shrunk toward the centre of the view at full
resolution, which fits everything inside the visible part of the panel with no
viewport rect, no black borders, no lost pixels, and without moving the sync
pixel.

**The flashlight stacked because each weapon owns its own `flashLightGO`.** Every
weapon switch reparented another one to the head, and brightness accumulated
until a respawn cleared them. One mod-owned copy now; the weapons' own lamps are
forced off permanently.

## Where this audit has been wrong

Kept deliberately, because the pattern is the useful part: in every case the
mistake was reasoning about the scene graph or the engine instead of reading
`hud_tree.txt` or the decompiled source.

| Claim | Corrected by | Reality |
|---|---|---|
| `MODE_VR = 0` in menus is a bug | Owner, 30 Oct | Deliberate and better. FINDINGS #1 withdrawn |
| `Docs/MetaAI/` misspelled `CleanToBlendTarget` | dnSpy | Both spellings right — field `cleanToBendTarget`, `.name` `"CleanToBlendTarget"` |
| `InputManager.IsInputUp` should be patched (#8) | `Toggler.cs` | Would invert `ToggleFlashlight`, `ToggleLaser`, `Aim` and `Crouch`. Finding reversed |
| `ToggleFlashlight` will strobe (#10) | `Toggler.cs` | Not a defect; the proposed edge fix would have lit it for one frame |
| The engine is Unity 4 | BepInEx startup log | Unity **5.6.7**. My supporting evidence ("CLR 2.0 assembly") was also wrong — that is the scripting runtime |
| IMGUI needs "its own camera" | Unity docs | `OnGUI` is not camera-bound at all. This is also the root of the damage-flash bug |
| `HelmetOccluder` causes the black screen | `hud_tree.txt` | Wrong camera, wrong layer, and runtime-spawned. See above |
| Collapse the far clip inside geometry | `ToggleOcclusion.cs` | Would switch off *more* lights. Removed the same day it was written |

## What is genuinely impressive here

Worth saying, because the findings list is long and the hard part is done.

The HUD work is real engineering. Interstellar Marines renders its HUD by
bending an NGUI render target onto a curved glass mesh through two materials
and three cameras, and the mod glues that whole pipeline to a VR head without
breaking the damage-shutdown and visor-open behaviour that gives the game its
identity. `Docs/MetaAI/BUG-FIXES.md` lists seven distinct dead ends on HUD
scaling alone. Every one of them is still in the code as a reverted no-op with
a comment explaining what it did — which is why this audit could be written at
all, and is a much better record than a clean file would have been.

The weapon pose has converged on the right answer too: head position for
height, body rotation for orientation, detached to the scene root. That is the
exact fix `general_wxr/TIPS.md` prescribes for a bug it cites *from this
project by name*.

## What to do next

See [NEXT_STEPS.md](NEXT_STEPS.md).

Of the four "non-negotiable" items in `general_wxr/CLAUDE.md`, three are now
done: quaternion normalisation and the single handedness swap (both at ingest,
#6), and the sync pixel (rewritten 3 Nov). **Borderless window enforcement is
the one still missing** — no code touches the window at all (#5).

Then, in order:

1. **Device-test the 3 Nov build.** Four independent changes landed at once,
   against the "one change per deploy" rule, because they were diagnosed
   together: `occlusion_culling=0` + `far_clip=60`, the peripheral damage bands,
   the global sync pixel, and `HelmetOccluder`'s renderer going back. If the
   build misbehaves, that is the order to bisect in.
2. **Find the `far_clip` floor on device.** It is live in the VR Settings panel.
   Watch for distant rooms visibly lighting up as you approach — that is
   `ToggleOcclusion`, and it is the signal the value is too low.
3. **Borderless window** (#5).
4. **Commit.** Nothing since `e648e72` is committed, including this doc set and
   nine source files.

Unstarted and still unassigned: binding `Aim` to a button (needs the owner's
call on which), `LeanLeft` / `LeanRight` from physical lean, reading
`Z:/tmp/xr/system` for headset auto-detection, and reconciling the root
`README.md` with the table above.
