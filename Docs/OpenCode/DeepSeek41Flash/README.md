# Docs/OpenCode/DeepSeek41Flash — OpenCode notes on IMVRMod

Written **2026-11-07** by OpenCode / DeepSeek V4.1 Flash, after reading, in this
order:

1. `Docs/MetaAI/` — the session history of how the code got here.
2. `Docs/ClaudeCode/` — the third-party audit of the code as it stood on 30 Oct,
   updated through 3 Nov.
3. `Docs/general_wxr/` — the game-agnostic WinlatorXR rules.
4. The source itself at commit `17a5f92`, plus a Release build.

This is deliberately a **fourth, small set**, not a replacement for any of the
other three. Its job is to reconcile them, record what has changed since the
ClaudeCode audit, and leave a short ranked plan for whoever comes next.

## Read these in this order

| File | What it answers |
|---|---|
| **[CURRENT-STATE.md](CURRENT-STATE.md)** | What is true *today*: commits, build, config defaults, what is device-confirmed, what is built-only, and which older claims are now stale |
| **[HANDSHAKE.md](HANDSHAKE.md)** | The XrApi 0.4 handshake implementation: files, paths, write policy, `system` mapping, precedence, failure modes, device checklist |
| **[CODE-MAP.md](CODE-MAP.md)** | Where everything lives at runtime: boot, frame order, patch inventory, subsystem notes, and the invariants that break the HUD if ignored |
| **[NEXT-WORK.md](NEXT-WORK.md)** | What I would do next, ranked, with the reasoning and the traps |

If you only read one file before touching code, read `CODE-MAP.md`'s
"Invariants" section. If you only read one file before planning, read
`NEXT-WORK.md`.

## Verification marks

These notes distinguish what I actually checked from what I inherited:

- **[src]** — read directly in the source at `17a5f92`.
- **[build]** — produced by `dotnet build IMVRMod.csproj -c Release` on
  2026-11-07.
- **[docs]** — inherited from `Docs/MetaAI/` or `Docs/ClaudeCode/`; not
  re-verified here.
- **[device]** — reported as seen on the owner's Quest 3 through WinlatorXR, by
  the owner or by the ClaudeCode audit.
- **[risk]** — my inference from the source. Implied by the code, not proven on
  hardware.

Nothing in this set was tested on a headset by me. "Works" below means "the code
does this", not "the headset does this".

## The one-paragraph assessment

The port is **further along than `MetaAI/` says and slightly further than
`ClaudeCode/` says**. The hard part — gluing the bent visor HUD to a VR head
without destroying the helmet fantasy — is in the tree and device-confirmed. The
sync pixel was correctly rebuilt as a global overlay camera, the occlusion
blackout was correctly diagnosed as `ToggleOcclusion` switching lights off, the
hand pose was correctly consolidated into one body-rotation frame, and the
in-headset tuning menu is real.

What remained was not the HUD. It was the unglamorous protocol and presentation
work: the borderless window and the XrApi handshake. **Both are now settled.**
The owner runs the game fullscreen in the container, so no chrome-stripping
code is needed. On 7 Nov the handshake was completed: `version` and `vr` are
written unconditionally, a 2 s watchdog repairs them after a runtime wipe, and
`system` now drives `flipHands=auto` with an explicit config override.

The remaining work is device verification and committing. The root `README.md`,
the `general_wxr` notes and this set were updated on 7 Nov; `Docs/ClaudeCode/`
and `Docs/MetaAI/` are **archived and deliberately unchanged**. The handshake is
committed in `f593154`; the low-res world scaler and the UDP-receiver rewrite are
still uncommitted, and all three need a device pass.

## What is genuinely good

Worth saying, because the next agent will otherwise only see the worklist:

- **The HUD pipeline survived.** `VRGlassAnchor` reparents the glass mesh while
  preserving its Animator pose, `VRHudSafeArea` shrinks the uGUI content rather
  than the camera or the bend target, and `HUDCamera.hudScale` was correctly
  reclassified as a **supersample factor**, not a size knob. Those three
  distinctions cost the project days and are now right.
- **The occlusion blackout is understood.** `VRClipGuard` exists because the
  game's culling disables lights, not because geometry disappears. That is a
  non-obvious diagnosis and the code documents it well.
- **The weapon frame is right.** Head position for height, body rotation for
  orientation, one copy in `VRHands` shared by the gun and the interaction ray.
  This is exactly what `general_wxr/TIPS.md` prescribes for the bug it cites
  from this project by name.
- **The input gate is centralised.** `VRInputGate.AllowNative()` is the single
  answer to "should native mouse/keyboard pass through", which is the right
  response to the `Cursor.visible` bug that cost this port so much time.
- **The handshake is now complete.** `version`/`vr` are unconditional, a
  watchdog repairs them after a runtime wipe, and `system` drives
  `flipHands=auto` with an explicit override. That removes the last manual
  guess from a normal session.
- **Performance now has a real lever.** `VRWorldScaler` renders the 3D world at
  half resolution inside the full-resolution window, so the HUD text stays
  crisp while the expensive pass drops to 640x480. The UDP receiver was also
  rebuilt to drain queues and parse without allocating.

## How I would work on this project

The owner's rhythm is unchanged and is correct:

1. One behavioural change per deploy.
2. Logs and a screenshot before theory; `F9` dumps `hud_tree.txt`.
3. Nothing is done until the owner confirms it on the Quest 3.
4. Do not "fix" the helmet by removing it.
5. Rebase on the file the owner actually has, not on a generated copy.
6. Keep the comments that explain dead ends — they are the most valuable part
   of `Patches.cs`.

The only thing I would add: **commit the docs with the code**. The current tree
is clean, but `Docs/general_wxr/PC_INPUT.md` is untracked, and the newest commit
is not reflected in any doc set. That is how a future agent ends up trusting a
stale audit again.
