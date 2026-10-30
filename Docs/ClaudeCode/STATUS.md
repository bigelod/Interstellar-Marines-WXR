# STATUS.md — where the port stands, 2026-10-30

Assessed by reading every source file and checking the reflective calls against
the shipped game assemblies. **Nothing below has been confirmed on a headset.**

> **Changes made the same day, after the owner reviewed this audit.** All build
> clean, none are device-tested:
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
>   on device and is re-checking next test.
>
> - **HUD scaling reconnected** (FINDINGS #11) — the HUD is confirmed visible in
>   gameplay, so FIXED29 holds. `VRHudSafeArea` now shrinks the screen-anchored
>   HUD toward the centre; the previous attempts failed because the HUD is uGUI,
>   not NGUI as `Docs/MetaAI/` says.

Branch `main`, last commit `a514f71` "Beta prep". Two files have uncommitted
changes: `Patches.cs` (FIXED29 — reverts `HUD UI Root` scaling to 1.0) and
`VRWeapon.cs` (FIXED2/3 — body rotation instead of head rotation, detach to
scene root). Both of those are the *newer* and better versions of that code, so
the working tree is ahead of the last commit in substance.

`Docs/` itself is untracked — this doc set and the two sets it sits beside are
not yet committed.

## Build

`dotnet build IMVRMod.csproj -c Release` → **0 errors, 5 warnings**, with the
five DLLs present in `Libs/`. Two of the warnings point at real findings
(`targetFovX` / `targetFovY` unused, see FINDINGS #7; unreachable code, see
FINDINGS #12).

## Subsystem status

| Subsystem | State | Notes |
|---|---|---|
| XrApi 0.4 client, inbound | **Working** | Correct packet layout, one socket, freezes on last pose. Normalisation and handedness now done once at ingest (#6). No staleness test |
| XrApi handshake | **Partial** | Writes `version` only if absent; no `vr` file; never reads `system`, so no headset detection (FINDINGS #12) |
| XrApi outbound | **All six fields real** | Haptics wired (#4); FOV now config-driven via `screen_fov` and formatted invariantly (#7) |
| Presentation mode switching | **Correct as written** | `MODE_VR = 0` for menus is deliberate — it stops the unused pose stream, restores runtime mouse control and drops out of immersive presentation (#1, withdrawn) |
| Sync pixel | **Owner reports working; two code smells deferred** | Camera selection can pick a render-target camera (#2); a never-reset static should lose it after a level change (#3). Both deferred pending the next device test |
| Window / borderless | **Not implemented** | No code touches the window at all (#5) |
| 6DOF head tracking | **Working** | Lean clamped to 0.30 m; yaw composes through the helmet parent. Handedness fixed at ingest (#6) — re-verify with head tilted sideways while looking up |
| Weapon / hands pose | **Working, unconfirmed on device** | Third iteration; head position for height, body rotation for orientation — the fix for the hands-swing-with-head bug `general_wxr/TIPS.md` names from this project. `Docs/MetaAI/CURRENT-PROGRESS.md` still lists it as unconfirmed |
| Haptics | **Implemented, untested** | Dominant hand, decayed per frame: 6 frames when `GetAmmoCount()` drops, 10 frames on a weapon switch (#4) |
| Input mapping | **Working** | All 22 `KeyBind` names verified to exist. `Aim` is blocked and unbound (#9); `ToggleFlashlight` is driven as a level, not an edge (#10) |
| Mouse / keyboard blocking | **Mostly working** | Both `InputManager` entry points plus six raw `UnityEngine.Input` methods. `IsInputUp` is unpatched (#8) |
| Helmet shell hiding | **Working** | Layer 8 `CH_Marine1pHelmet*` renderers disabled; `Glass_HUD` deliberately spared |
| HUD glue to VR head | **Working** | `VRGlassAnchor` reparents the glass mesh per frame, preserving the Animator pose; restores it when the visor opens |
| HUD scaling | **Mechanism exists, untested** | `VRHudSafeArea` gives the canvas content a smaller parent rect, which is the one approach that moves edge-anchored widgets. `hudScale` drives it; default 0.62 (#11) |
| Compass | **Implemented, unverified** | Head yaw instead of body yaw; the four-argument `UpdateTicks` invocation could not be checked without `UnityEngine.UI` |
| No head recoil / bob / lurch | **Implemented** | Four `RangedWeapon` view-rotation patches and three `Helmet` bob patches, all gated on `inVR` |
| Flashlight reparenting | **Implemented** | Moved to the head camera so the beam follows the eyes |
| Visual tweaks | **Implemented, not gated** | Forces fog on with hardcoded values, AA off, shadow cascades off, vsync off — no config key, no VR-only justification recorded for the fog |
| Diagnostics | **Good where it exists** | `HUDTreeDumper` on F9 is excellent. `verboseDebug` has no runtime switch (#12) |
| Menus / lobby in VR | **Not started** | Separate cameras at (-1000, 22, 0); currently handled by trying to leave VR entirely, which is finding #1 |

## Where the README and `Docs/MetaAI/` disagree with the code

Worth reading before trusting either. The code is what ships.

| Claim | Where | Reality |
|---|---|---|
| "Haptics on shoot (right controller 6-8 frames)" | `README.md` Features | Was never called and the packet field was a literal `0`. Now implemented, but driven by the **ammo counter** rather than the Fire input, so the README wording still needs updating (#4) |
| "MODE_VR=0 flat screen" | `README.md` How it works | Accurate. `general_wxr` described `0` only as "disabled" and has been corrected (#1) |
| "Lean clamped to 12cm" | `README.md` How it works | `0.30f` in `VRHeadTracker.cs:26` |
| "`VRWeapon.cs` — RH→LH `pos.z=-z, quat(-x,-y,z,w)`" | `README.md` Project files | The code converts through Euler angles, not quaternion components — which is not the same operation once pitch and roll combine (#6) |
| "`XRAPI.cs` — … `D:/xrtemp` fallback" | `README.md` Project files | No such path exists in the code; only `Z:/tmp/xr` |
| "Red pixel quad top-left (10x10)" | `README.md` How it works | 150 px, which is a deliberate and sound choice (the runtime samples one pixel; extra size survives a shifted corner) — but the doc and the code disagree |
| "Post-processing disabled for VR: motionBlur, chromaticAberration, depthOfField" | `README.md` Features | No such code exists. `Docs/MetaAI/` records that `EnforceNoPostFX` was removed because it killed the HUD blend; the README still advertises it |
| "HUD scaler works" | commit `a514f71` | Was read and discarded. Now drives `VRHudSafeArea` (#11) |
| `RenderSettings.fogStartDistance` / `fogMode` "don't exist in Unity 4" | `MetaAI/BUG-FIXES.md`, `AGENT-ADVICE.md` | They do exist, the mod uses them, and it compiles. **[reflected]** See the corrections section of FINDINGS.md |
| `CleanToBlendTarget` | throughout `Docs/MetaAI/` | **Accurate** — that is the RenderTexture's `.name`; `cleanToBendTarget` is the field holding it. An earlier revision of this audit wrongly called it a misspelling **[dnSpy]** |
| "HUD only visible when paused (active bug)" | `MetaAI/CURRENT-PROGRESS.md` | Superseded. FIXED29 keeps `HUD UI Root` at 1.0, which is the stated fix for exactly that bug — so this entry describes the state *before* the current working tree |

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

See [NEXT_STEPS.md](NEXT_STEPS.md). The short version: the four items in the
"non-negotiable" category of `general_wxr/CLAUDE.md` — sync pixel, borderless
window, quaternion normalisation, single handedness swap — are the ones that
are wrong or missing, and they are all small, mechanical changes. They should
go before any new feature.
