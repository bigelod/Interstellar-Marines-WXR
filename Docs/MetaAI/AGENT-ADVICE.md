# AGENT_ADVICE.md — Handoff for Next AI Agent (IMVRMod)

Read `PROJECT_PLAN.md`, `CURRENT_PROGRESS.md`, `BUG_FIXES.md`, `FUTURE_GOALS.md` first. This file is the stuff that will bite you if you don't know it.

## Who the user is / how they work
- User is Daven Bigelow, modding Interstellar Marines for VR in his spare time. He usually tests on PC only, not on-device — using XrUDP Tester for head/controller data. He sends screenshots and short videos (rotation test, translation test) to verify. Trust the screenshots over your theory.
- He iterates fast and says things like "still no luck" or "nearly perfect but flickering" — that's not a complaint, that's the signal for the next FIXED number. Don't ask clarifying questions, keep going, keep the same FIXED naming: `Patches_FIXED28`, `Patches_FIXED29`, etc.
- When he uploads a file (e.g. `Patches.cs`, `VRWeapon.cs`, `hud_tree.txt`), rebase off *that* file, not your last generated file. He explicitly asked for that on SyncPixel — "rebase off uploaded file, 3x bigger only" — and will correct you if you drift.
- He cares about the helmet fantasy, not just VR: HUD shutting down on damage, visor open/closed, audio muffling — don't suggest removing the helmet/HUD system to "fix" VR.

## Environment — non-negotiable facts
- Unity 4, C# 3, BepInEx. No C# 6+ features: no `$"..."` interpolated strings, no `?.`, no pattern matching, no `var` outside a method, no top-level statements, no tuple returns. You *will* get CS8024 / CS0708 / CS0825 if you forget.
- `Path.Combine` has only 2 args — use `Path.Combine(Path.Combine(dllDir, ".."), "config.txt")`, not 3 args.
- `RenderSettings.fogStartDistance` and `fogMode` don't exist — Unity 4 uses `fogStart`/`fogEnd`. Don't touch fog unless you have verified the API.
- `config.txt` lives next to the DLL. `hud_tree.txt` is written by `HUDTreeDumper.cs` to the DLL directory (not `Z:/tmp/xr`). The dumper runs on F9 via `HUDTreeDumpRunner` (`DontDestroyOnLoad`), ignoring `verboseDebug` for F9.
- All generated files are hidden by default — always link them as `[name](container:///mnt/data/name)` so the user can see/download them. Don't paste full files in chat unless he asks.

## The HUD pipeline — read this before touching it
The HUD is NOT a normal Canvas. It's 3 steps, and breaking any one makes it vanish silently:

1. NGUI UI is rendered by `CAM Camera` (culling 65536, parent `Fake 3D UI Root` at 516,2,0) and by `HUDCamera` (culling 1568/65536, parent `HUD UI Root` at 501,2,0) to `CleanToBlendTarget` RT (1280x720 originally, 1400x1080 in latest dump).
2. `HUDCamera` bends it via `bendMaterial` to `BendToDoubleGlassTarget`, then `doubleGlassMaterial` projects it onto the glass mesh.
3. The glass mesh `CH_Marine1pHelmetGlass_HUD` (layer 0, children layer 14, mats `CH_1pHelmet_HUD_NGUI` / `_Reflection`, `Animator`, `currentVisorState=Closed`) is rendered by `HelmetCamera` (culling 16384, parent `WorldCamera`).

Rules that have cost hours:
- **Never set `glassIntensity=0`, `glassTexture=null`, `clearFlags=SolidColor`, or `backgroundColor=transparent` on `HUDCamera`** — it kills the doubleGlass blend and HUD disappears (dump will show `glassIntensity=0`).
- **Never scale `HUD UI Root`** — scaling it to 0.65 breaks the bend (tree proves: HUDCamera local -10 becomes world -6.5, UI Canvas 5.4 becomes 3.5). Paused restores it to 1.0, which is why HUD only appears when paused. `FIXED29` reverts it to 1.0.
- **Never `EnforceNoPostFX()` on all cameras** — it disables `HUDCamera`'s Bloom/Blur/bend and HUD disappears. Gate to `WorldCamera` only.
- **Never `Screen.SetResolution` + RT resize while RTs are bound** — broke everything (black screen / HUD missing) in FIXED23-25. The current `hud_tree.txt` still shows `currentResolution=(1400,1080)` from that attempt — that's leftover, not a goal.
- **Don't force `localPos=(0,0,0)` / `localRot=identity` on Glass_HUD** — its Animator pose is `(1,359.7,0)` (`helmetClose`). Preserve `originalLocalPos/Rot` from `Awake`, only re-parent when needed, or you get flicker.
- **Layer 8 vs Layer 14:** Hide layer 8 `CH_Marine1pHelmet` / `CH_Marine1pHelmetGlass` (black disc). Do NOT hide layer 14 glass mesh — that's the HUD. The filter that required layer 14 missed Glass_HUD because Glass_HUD itself is layer 0, only its children are 14 — match name, not layer.
- **Menu cameras are separate:** `MainMenuCamera` depth 5, `SettingsMenuCamera` depth 6, `LobbyCamera` depth 8, all at (-1000,22,0), culling 32 — don't scale them, don't parent them to VR.

## The gun — read this before touching it
- Raw controller pose is upside-down on Quest 3. The fix is roll, not yaw: `if (!VRManager.flipHands) r = r * Quaternion.Euler(0,0,180)`. `flipHands=false` = Quest 3 upright, `true` = Quest 2/Pico. User said "no handsfix.txt needed" — config `flipHands` only.
- `RangedWeapon.AttachToCamera()` parents `WeaponModel` to `WorldCamera`, so if you use head-relative pose you double-count head. Detach to scene root, use head position for height + body rotation for orientation.
- **Never detach to `VRRoot`** — `VRRoot` is `Destroy()`ed on level change in `Helmet_InitFirstPersonCamera` and takes the gun with it. Use `SetParent(null, true)`.

## Input blocking — the two leaks
- Winlator cursor is always visible, so `if (Cursor.visible && Cursor.lockState != Locked) return true` always leaks input — remove it.
- The game also reads raw `Input.GetAxis("Mouse X/Y")`, `GetKey(W/A/S/D)`, etc., bypassing `InputManager.IsInput` — you must patch `GetAxis`, `GetAxisRaw`, `GetKey`, `GetKeyDown`, `GetMouseButton`, `GetMouseButtonDown` to 0/false in VR gameplay only. Menus/paused (`LocalMarine==null` or `GameManager.IsPaused`) must still allow input.

## What "HUD only when paused" means
If the user says that again, don't explain hands at floor. It means `VRGlassAnchor.LateUpdate` — paused = `RestoreUniversalScale()` (HUD UI Root back to 1.0, HUD appears via CAM Camera directly), gameplay = scaled to 0.65 (bend broken, glass blank). Check `hud_tree.txt` for `HUDCamera` pos vs localPos: if world pos is localPos * 0.65, that's the scale bug.

## Style / workflow
- Short, direct, technical. No preamble, no "I'll continue", no "great question". The user wants files and code, not summaries.
- When you change one thing, say what you changed and why it broke before, with the actual line numbers / field values from the dump. Cite the dump (e.g. "HUDCamera pos (501,2,-6.5) from local (0,0,-10) = 0.65 scale").
- Keep FIXED numbering sequential and keep the original file's comments about previous FIXEDs — the user reads those comments to track history.
- Don't propose "remove the HUD" or "use a normal Canvas" — the HUD is the game's core identity.
- If you don't know something, say "I don't know" — don't guess Unity 4 APIs or Quest behaviour.
