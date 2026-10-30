# NEXT_STEPS.md — ordered plan

`general_wxr/CLAUDE.md` asks for **one change per deploy**, verified by the
owner on hardware before the next one. This list is ordered so that each step
is independently testable and the cheap, high-certainty fixes come first.

Numbers in brackets refer to [FINDINGS.md](FINDINGS.md).

---

## Done on 2026-10-30 — awaiting the owner's device test

Both build clean. Neither is verified, so **both need testing before anything
else is layered on top**, and the handedness one in particular touches every
spatial path in the mod.

- **Handedness and normalisation moved to ingest** [#6]. Test case that
  distinguishes new from old: **head tilted sideways while looking up**, for
  both the view and the gun. Single-axis motion should be bit-identical to
  before.
- **Haptics** [#4], dominant hand: 6 frames on an ammo decrease, 10 frames on
  a weapon switch. Test: single shot, full-auto, dry trigger pull (should be
  silent), reload (should be silent), stick-cycling the three slots (one buzz
  per switch, longer than a shot), and spawning into a level (should be
  silent).

Also shipped and awaiting test: **`screen_fov`** [#7], config-driven and
steppable in the headset with up/down while paused.

Two items were **deleted** from this plan rather than done, because reading the
game source showed they were wrong: patching `IsInputUp` [#8] and making
`ToggleFlashlight` an edge [#10] would each have broken working inputs.

Also settled, with no code change: `MODE_VR = 0` for menus is correct [#1], and
`Docs/general_wxr/` has been corrected to say so. The sync-pixel findings
[#2, #3] are deferred at the owner's call.

---

## Stage A — instrument first, change nothing

The owner tests through WinlatorXR on a Quest 3, where there is no debugger and
each iteration costs a deploy. Three of the top findings are "probably" rather
than "certainly", and one log line settles each.

1. **Add a config-driven `verbose` key** [#12] and wire it to the existing
   `VRManagerState.verboseDebug`. One flag, off by default, exactly as the
   standing instructions ask. `HUDTreeDumper`'s F9 path already ignores it.
2. **Embed a build stamp** and log it on line 1 — `general_wxr/CLAUDE.md` is
   emphatic about this, and it is the difference between "the fix did nothing"
   and "you tested the old DLL". C# 3 has no `__DATE__`, so write the assembly
   version plus `File.GetLastWriteTime` of the executing assembly.
3. **Log the sync-pixel camera once**: name, depth, and whether
   `targetTexture == null`. This confirms or kills [#2] immediately.
4. **Log every outbound 7278 packet** under the verbose flag. This confirms
   [#1] and [#7] and gives the owner something to paste.

One deploy, no behaviour change, and the next three stages stop being guesses.

---

## Stage B — the non-negotiables

Each of these is in the list `general_wxr/CLAUDE.md` says must be right before
anything else. All are small.

5. **Force the window borderless and full-display, re-applied every frame**
   [#5] — but confirm with a screenshot first that WinlatorXR is not already
   presenting it that way. Keep this strictly separate from resolution changes,
   which have broken the HUD before. This is now the only item in this stage
   that is both outstanding and not deferred.

Deferred at the owner's call, to revisit when sync is next looked at on
hardware — do not pick these up unprompted:

- **Sync-pixel camera selection** [#2]: highest-depth enabled camera **with no
  render target**, deterministic tie-break, orthographic handled in the sizing
  maths.
- **Sync pixel surviving a level change** [#3]: drop the never-reset
  `static bool alreadyCreated`.
- **Give the sync quad its own layer again** [#12]: layer 31 as
  `MetaAI/PROJECT-PLAN.md` originally specified, and stop OR-ing layer 0 into
  the host camera's `cullingMask` — that mask is the one thing the HUD pipeline
  cannot tolerate being touched. This one is worth doing on its own merits even
  if sync is fine, because of what it does to `HUDCamera`.

---

## Stage C — make the advertised features real

11. **Bind `Aim`** [#9]. Now cheap: `Marine` drives it through a `Toggler`, so
    publishing a held level gives hold-to-ADS with no edge handling at all.
    Needs a button, which is the owner's call, not an agent's.
12. **Read `Z:/tmp/xr/system` and auto-detect the headset** [#12]. Line 2 is
    the product codename: `EUREKA` → Quest 3 (`flipHands=false`),
    `HOLLYWOOD` → Quest 2 (`flipHands=true`), `SEACLIFF` → Quest Pro.
    `general_wxr/README.md` carries the mapping and HWXR's defaults. This
    deletes a config key the user currently has to guess, and
    `general_wxr/CLAUDE.md` asks for exactly that kind of trade: fewer
    settings, better defaults.
13. **Write `version` unconditionally**, and write the `vr` file, deleting it
    on shutdown [#12]. The runtime wipes that directory at its own startup, so
    the current "only if absent" guard can leave the mod with no handshake at
    all.

---

## Stage D — housekeeping, any time

14. Delete the unreachable block at `Patches.cs:187` [#12].
15. Either delete `VRHUDAnchor` or move the explanation onto the class [#12].
16. Reconcile the root `README.md` with the code — the table in
    [STATUS.md](STATUS.md) lists every claim that is currently wrong. A README
    that over-promises costs the next contributor a day.
17. Fix the fog claim in `Docs/MetaAI/` (the render-target spelling needs no
    fix — see the corrections section of FINDINGS.md),
    or leave a pointer to the corrections section of
    [FINDINGS.md](FINDINGS.md). Those files are a session history and there is
    a case for leaving them as written — but then the correction needs to be
    findable from them.
18. Commit `Docs/`. Three doc sets exist and none of them are tracked.

---

## Stage E — open questions that need the owner, not an agent

These cannot be decided from the code.

- **HUD scaling** [#11] — now has a mechanism (`VRHudSafeArea`), so the open
  question is only whether 0.62 is the right number and whether the skip list
  correctly spares the world-space markers. Both are answered by one device
  test and tunable from `config.txt` without a rebuild.
- **`Aim` / iron sights** [#9]. Blocked and unbound, so unreachable in VR.
  Fixing it means finding a button on a hand that has run out of them, or a
  gesture — gun-to-face is the obvious one and is real work. The owner's call.
- **Fog.** `VRVisualTweaks.ApplyGlobal` force-enables fog with hardcoded
  distances and a colour, with no recorded reason and no config gate. Was this
  a performance measure, an atmosphere preference, or a leftover? It changes how
  the game looks for every player.
- **Menus and the lobby in VR.** Currently handled by leaving VR, which is the
  shape of finding [#1]. If [#8] lands as `MODE_VR = 3`, the runtime drives the
  mouse from head motion and flat menus become usable without any camera work —
  which may be the whole answer, and is much cheaper than making the menu
  cameras VR-aware.
- **Performance on device.** No measurement exists. `general_wxr/CLAUDE.md`
  rules out instrumenting the mod for this — frame time lives in the
  compositor and the translation layers, where the mod cannot see it — so this
  needs Android and WinlatorXR tooling, not code.

---

## A note on process

`Docs/MetaAI/AGENT-ADVICE.md` describes a working rhythm of sequentially
numbered `FIXED` files handed back as whole uploads. That made sense for a chat
assistant with no repository access. In a git repository it is worth dropping:
the `FIXED` numbers now live in comments that no longer match any file, and the
history they encode is what git is for. Keep the *comments* that explain why a
dead end was a dead end — they are the most valuable thing in `Patches.cs` —
but let commits carry the sequence.
