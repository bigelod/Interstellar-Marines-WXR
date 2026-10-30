# CURRENT_PROGRESS.md — In-Progress Features

## 1. HUD — Only Visible When Paused (Active Bug)
- **Status:** Broken in gameplay, works when paused.
- **Evidence (`hud_tree.txt`):** Glass is glued correctly — `CH_Marine1pHelmetGlass_HUD` parent=`WorldCamera`, localPos `(0,0,0)`, rot `(1,359.7,0)`, `HelmetCamera` depth 1 culling 16384 enabled, `HUDCamera` depth 1 culling 65536 target `CleanToBlendTarget 1400x1080`, `glassIntensity=1`, `hudScale=1`, `currentVisorState=Closed`.
- **Cause:** `VRGlassAnchor.LateUpdate` — when paused `inGameplay=false` -> `RestoreUniversalScale()` returns `HUD UI Root` to 1.0 and HUD appears via `CAM Camera` directly. When in gameplay it scales `HUD UI Root` to `vrHudScale` (0.65 in tree: `HUDCamera` local -10 -> world -6.5, `UI Canvas` local 5.4 -> world 3.5) which breaks `HUDCamera`'s bend pipeline — glass shows blank.
- **Latest fix:** `Patches_FIXED29.cs` — set `vrHudScale=1.0`, keep `HUD UI Root` at 1.0, do not offset it. HUD will be full-size but visible. Next step is safe shrink via UV, not root scale.
- **Next:** Test FIXED29 in gameplay, confirm HUD stays visible unpaused, then re-attempt 0.8 shrink via `doubleGlassMaterial` only.

## 2. HUD Scaling — 0.8 / 0.65 Attempts
- **Status:** Partially working at 0.6, then regressed.
- **History:** Scaling `HUD UI Root` by 0.6 worked once — user said "Yay you did it!" — screenshot showed HUD centered smaller. Then `FIXED20` made it config-driven (`hudScale=0.8` in `config.txt`, re-checked every 2s), `FIXED21` fixed `Path.Combine` 3-arg error, `FIXED22` added centering (+15 X to align 501 -> 516).
- **Current:** Reverted to 1.0 in FIXED29 to restore visibility. User wants ~80% original size. Config `hudScale` still exists but is forced to 1.0 in FIXED29.

## 3. Gun / Hands — No Hands / At Floor
- **Status:** Gun upright when head-relative, but at floor when absolute, and moves with head when parented.
- **Current attempt:** `VRWeapon_FIXED3.cs` — head position + body rotation, detach to scene root (not `VRRoot`), `if (!flipHands) r = r * Quaternion.Euler(0,0,180)` roll 180 for Quest 3.
- **Not yet confirmed:** User has not confirmed gun is visible at hand height with latest fix.

## 4. Resolution — 1400x1400 / 1080x1080
- **Status:** Attempted in FIXED23-25 via `Screen.SetResolution` + RT resize, broke everything (black screen / HUD missing). Fog errors also hit (`RenderSettings.fogStartDistance` / `fogMode` don't exist in Unity 4, `Path.Combine` 3-arg, CS8024 top-level statements).
- **Current:** Rolled back to FIXED28/29 (no `vrResolution`, no `EnforceNoPostFX`, no fog). `hud_tree.txt` still shows `currentResolution=(1400,1080)` — leftover from previous attempt.

## 5. Post-Processing — Enforce Disabled
- **Status:** Attempted, reverted.
- **Attempt:** `EnforceNoPostFX()` every frame disabling `PostProcessingBehaviour` / `Bloom` / `Blur` / `CameraFX` on all cameras — killed `HUDCamera` bloom/bend.
- **Next:** Gate to `WorldCamera` only, test after HUD visibility is fixed.

## 6. Input Blocking — Mostly Done, Needs Re-verify
- **Status:** Implemented in `Patches.cs` FIXED4, not yet re-tested with latest `Patches_FIXED29`.
- **Implementation:** Patched `Input.GetAxis`, `GetAxisRaw`, `GetKey`, `GetKeyDown`, `GetMouseButton`, `GetMouseButtonDown` to return 0/false in VR gameplay only, menus/paused allowed. Removed `Cursor.visible` check that leaked input via Winlator.

## 7. Sync Pixel — Done
- **Status:** Working. 3x bigger version confirmed visible in screenshot (small on 3840-wide render target but visible). `150f / pixelWidth`, fallback `0.12f`, `syncLayer=31`.

## 8. Docs — This Set
- `PROJECT_PLAN.md`, `FUTURE_GOALS.md`, `CURRENT_PROGRESS.md`, `BUG_FIXES.md` — created in this session.
