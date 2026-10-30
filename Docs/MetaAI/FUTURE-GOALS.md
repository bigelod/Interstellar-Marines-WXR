# FUTURE_GOALS.md — Remaining Features (Not Yet Started)

> Features that have not been started yet. In-progress work lives in `CURRENT_PROGRESS.md`. Bugs already fixed live in `BUG_FIXES.md`.

## 1. HUD Scaling — Safe Shrink to ~80%
- [ ] Shrink HUD to ~80% original size (user wants 80% vs current 60% attempt) without breaking the bend pipeline.
- [ ] Make `hudScale` in `config.txt` the single source of truth, re-checked every 2s for live tweaking without restart.
- [ ] Do NOT scale `HUD UI Root` — that breaks `HUDCamera`'s `CleanToBlendTarget` bend (see `CURRENT_PROGRESS.md`). Investigate shrinking via `doubleGlassMaterial` UV / `CanvasScaler` / NGUI `Camera` frustum only.
- [ ] Keep centering correct: `HUD UI Root` at 501 vs `Fake 3D UI Root` at 516 — 15-unit X offset when scaled — without moving the root to a wrong offset.

## 2. Resolution — Force Quest 3 Resolution
- [ ] Force `1400x1400` or `1080x1080` (square) for Quest 3 — current attempt via `Screen.SetResolution` + RT resize broke HUD.
- [ ] Resize `HUDCamera`'s `CleanToBlendTarget` / `BendToDoubleGlassTarget` (currently 1400x1080) in sync with `Screen` resolution, without resizing while RTs are bound.
- [ ] Investigate `hudScale` vs resolution: `hudScale` currently only changes RT quality (1280 -> 768) not real estate.

## 3. Post-Processing / Performance — Safe Reduction
- [ ] Reduce post-processing, ensure AA / AO / DoF disabled and stay disabled.
- [ ] Gate it: only disable on `WorldCamera` / `PlayerCamera`, never on `HUDCamera` / `HelmetCamera` / `CAM Camera` — `EnforceNoPostFX()` every frame killed the HUD blend.
- [ ] Add config toggle for `allowMSAA=false`, fog, Bloom, Blur, `CameraFX`, `PostProcessingBehaviour`.
- [ ] Measure: Quest 3 frame time in heavy scenes with HUD on.

## 4. Gun / Hands — Correct Hand Pose
- [ ] Gun visible at hand height with correct orientation: head position for height + body rotation for orientation, not head rotation.
- [ ] Detach `WeaponModel` from head chain to scene root (not `VRRoot` — `VRRoot` is destroyed on level change and takes the gun with it).
- [ ] Test flip: `flipHands=false` = Quest 3 upright, `flipHands=true` = Quest 2/Pico.

## 5. VR Menus & Lobby
- [ ] Make main menu / settings / lobby cameras VR-aware — currently separate cameras at (-1000,22,0) that bypass VR head tracking.
- [ ] Ensure pause menu HUD is visible in VR gameplay, not only when paused (current bug — see `CURRENT_PROGRESS.md`).

## 6. Helmet Gameplay Preservation
- [ ] HUD shuts down / reboots on damage, affects hearing/visuals — verify in VR that `Helmet` damage still drives `glassIntensity`, `AudioLowPassFilter`, and visor state.
- [ ] Visor up (`currentVisorState != Closed`) hides HUD like flat mode — currently implemented but needs re-testing after HUD scale fix.

## 7. Comfort / Polish (Not Started)
- [ ] Two-handed gun / off-hand, reload gesture, weapon switching.
- [ ] Haptics, controller vibration on damage / shooting.
- [ ] Recenter / calibration, IPD / eye height, vignette on movement.
- [ ] Performance logging: FPS, RT size, draw calls with HUD on/off.
