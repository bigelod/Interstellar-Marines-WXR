# NEXT-WORK.md — ranked plan, updated 2026-11-07

The rule from `Docs/general_wxr/CLAUDE.md` still applies: **one behavioural
change per deploy, verified by the owner on the Quest 3 before the next one.**

The two items that led the previous version of this file are now settled:
**borderless is accepted as handled by the owner's fullscreen container**, and
**the XrApi handshake is implemented** in the working tree. What remains is a
device pass, a commit, and the small open items below.

---

## Done in the 7 Nov pass

- **Handshake:** `version` (`0.4`) and `vr` (`VR`) are written unconditionally;
  `TickHandshake` repairs them every 2 s if the runtime wipes the directory;
  `system` drives `flipHands=auto`; an explicit `true`/`false` overrides; `vr` is
  deleted on shutdown; desktop fallback is `xrtemp` next to the DLL.
- **Config:** ships `flipHands=auto`.
- **Borderless:** accepted as handled by fullscreen. No code change.
- **Docs:** root `README.md`, the `general_wxr` notes and this set reconciled
  with the code at `17a5f92` plus the handshake fix. `Docs/ClaudeCode/` and
  `Docs/MetaAI/` are archived and were left untouched.

---

## Now — device-test the 7 Nov build

This is the only step before new code. The build adds the handshake /
auto-detection fix on top of `17a5f92`'s sprint-bob change and the 3 Nov batch.

Ask for one report covering:

1. **Handshake:** the log shows `Handshake Z:/tmp/xr version=0.4 vr=VR` and
   `Headset: make=... product=... -> ...`. Delete the `vr` file externally and
   confirm it is recreated within a couple of seconds.
2. **Detection:** `flipHands` follows the detected model. On the owner's Quest 3
   that should be `false` / upright. Force `flipHands=true`, then `false`, and
   confirm the config value wins over detection. Return it to `auto`.
3. **Sprint bob:** no bob when jogging or sprinting; walking and weapon bob
   still present.
4. **3 Nov batch:** peripheral damage bands, helmet lamp shadows, interaction
   ray, HUD supersampling at 2.0, haptics.

If it misbehaves, bisect in this order: handshake (`version`/`vr`/`system`) →
sprint-bob fields → 3 Nov batch. Do not add a fourth change to the same deploy.

**Handshake edge cases to watch:**

- `system` absent at init: the mod should log the default Quest 3 mapping and
  keep retrying every 2 s, not fail.
- Runtime restarted under the game: the directory is wiped, then our watchdog
  recreates `version`/`vr`. If the runtime has already read a missing
  `version`, it will not re-read it; a game restart may still be required. Log
  the repair so this is distinguishable from a dead socket.
- Pico / Quest 2: `HOLLYWOOD` maps to upside-down. If the headset reports an
  unexpected codename, the log will name it; add the mapping rather than
  guessing in the config.

## Next — the cheap hardening

5. **Add a runtime `verbose` key** [#12]. `verboseDebug` is still a
   recompile-only flag. `general_wxr/CLAUDE.md` asks for exactly one flag, off by
   default, reachable without editing files on the headset. The tuning menu is
   the natural place.
6. **Find the `far_clip` floor on device.** It is live in VR Settings. Watch
   for distant rooms visibly lighting up as you approach — that is
   `ToggleOcclusion`, and it means the value is too low.
7. **Commit.** HEAD is `17a5f92`; the handshake fix, the README/docs pass and
   this set are uncommitted. `Docs/general_wxr/PC_INPUT.md` is also untracked.
8. **Leave `Docs/ClaudeCode/` and `Docs/MetaAI/` archived.** They are useful
   history and should not be "fixed". The root `README.md` and `CLAUDE.md` now
   point at this set as the current delta; keep that pointer accurate.

## Stage C — make the advertised features real

9. **Bind `Aim`** [#9]. `Marine` drives it through a `Toggler`, so a held level
   gives hold-to-ADS with no edge handling. Needs a button, which is the owner's
   call, not an agent's.
10. **Menus / lobby** — decide whether `MODE_VR=0` is the shipping answer (it
    currently works and is deliberate) or whether `MODE_VR=3` / VR-aware menu
    cameras are worth the work.
11. **`LeanLeft` / `LeanRight` from physical lean**, if wanted. They are unbound
    and not blocked today.

## Stage D — housekeeping, any time

12. Delete the unreachable block at `Patches.cs:278` [#12] (CS0162).
13. Fix the logger wiring in `Plugin.cs`: `LogInfo` is assigned twice, so
    `LogDebug` is unreachable.
14. Delete `VRHUDAnchor` or move the "do not add this" explanation onto the
    class. It is dead code at `Patches.cs:739`.
15. Remove `VRHeadTracker.hasBase` (CS0414) or use it.
16. Remove the unused `amplitude` parameter from `SendHapticVibration`, or make
    it mean something.
17. Reconcile `shadow_distance`: the code default is 10, but `config.txt` ships
    15, so the config wins. Decide which is intended.

## Stage E — open questions that need the owner, not an agent

- **HUD scale:** is 0.60 final, and does the skip list spare every world-space
  marker in every level?
- **`Aim` / iron sights:** which button or gesture? The left hand is out.
- **Fog:** `VRVisualTweaks.ApplyFog` forces linear fog with hardcoded values and
  no config gate. Is that intended as a view wall, an atmosphere choice, or a
  leftover?
- **Performance:** measure on-device with Android/WinlatorXR tooling, not by
  instrumenting the mod. The first candidates to wind back are
  `hud_supersample=2.0`, `render_width/height`, and shadows.
- **`occlusion_culling` default:** 1 is back on and behaves on device with
  `VRClipGuard`; keep unless the 7 Nov build regresses.

## Risks I would watch, but would not change blind

1. **`VRHudSafeArea`'s "1.0 is a no-op" is only true before first install.**
   Changing `hudScale` from 0.60 to 1.0 live leaves the containers and
   reparented children in place; they render at scale 1, which looks right but
   is not literally an uninstall.
2. **`VRHudSafeArea` re-adopts new children every 2 s.** A widget that spawns
   later and is not caught by the substring skip list will be pulled into the
   safe-area container. Worth one device check on a level with dynamic markers.
3. **`ApplyRenderResolution` is called from `Helmet_VR_NearClip`, which returns
   early when not in VR.** The startup call covers the normal case; a menu
   transition that reapplies the game's saved resolution is the edge.
4. **The handshake watchdog cannot make the runtime re-read `version`.** If the
   runtime restarts, wipes the directory and reads a missing `version` before
   our 2 s tick, the stream stays silent until the next runtime start. The
   repair log is the evidence; a game restart is the workaround.
5. **The sync pixel's no-packet fallback uses `Time.frameCount % 256`.** In
   menus `MODE_VR=0`, so the runtime should not be reading it, but if it is
   still capturing, the id changes arbitrarily. Probably harmless and
   deliberate for colour calibration.
6. **`Patches.cs` modifies the visor transform's material** (`mainTexture=null`,
   alpha 0.12) and no doc explains it. It is near the HUD pipeline; treat it
   with the same suspicion as any glass change.
7. **`VRHandInteract` replicates a branch of `Marine.ProcessInput` by hand.**
   It builds, but it is the kind of code that drifts if the interaction rules
   change. Re-read it first if doors or panels stop responding in VR.

## What I would not do

- Do not scale `HUD UI Root`, touch `HUDCamera`'s glass fields, or disable Bloom
  to chase frames. All have already cost days.
- Do not patch `InputManager.IsInputUp` in isolation.
- Do not replace the helmet HUD with a normal Canvas.
- Do not collapse `far_clip` per frame.
- Do not parent the weapon to `VRRoot`.
- Do not add synthetic `SendInput` / `PostMessage` input. Use the game's own
  functions or the state its input code already reads (`PC_INPUT.md`).
- Do not treat the October audit as current. Check `17a5f92` and the handshake
  change first.
