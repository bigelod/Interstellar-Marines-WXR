# Docs/ClaudeCode — Claude Code's notes on IMVRMod

Written for whoever works on this port next (an agent or a contributor), and
for the owner as a status record.

This set was produced on **2026-10-30** by reading every source file in the
repository against `Docs/general_wxr/` (the protocol and porting rules) and
`Docs/MetaAI/` (the history of how the code got here). It is deliberately
separate from both: `MetaAI/` is the record of the sessions that wrote the
code, `general_wxr/` is the game-agnostic framework, and this folder is a
**third-party audit of the code as it actually stands**.

## Read in this order

| File | What it is |
|---|---|
| **[CURRENT_FEATURES.md](CURRENT_FEATURES.md)** | What the mod does today — controls, config, haptics, HUD — and the things that look wrong but are not |
| **[STATUS.md](STATUS.md)** | Subsystem-by-subsystem state, and where the README and `MetaAI/` docs disagree with the code |
| **[ARCHITECTURE.md](ARCHITECTURE.md)** | What the code actually does at runtime — patch inventory, frame order, the HUD pipeline |
| **[FINDINGS.md](FINDINGS.md)** | Concrete defects, ranked, each with a file and line |
| **[NEXT_STEPS.md](NEXT_STEPS.md)** | Ordered plan, one change per deploy |

## How claims here are marked

Reading code is not the same as knowing it works. Every non-obvious claim in
this set carries one of these:

- **[code]** — read directly from the source in this repository.
- **[reflected]** — verified against the shipped game assemblies in `Libs/`
  using reflection, so the member genuinely exists with that name and type.
  This matters because roughly half of this mod reaches into the game by
  string-named reflection, which the compiler cannot check.
- **[spec]** — from `Docs/general_wxr/`, which is itself sourced from the
  WinlatorXR runtime's Java source, the official spec and device captures.
- **[unverified]** — believed but not confirmed. Needs a device, a log line or
  a reflection check. Treat as work, not as fact.

Nothing in this set has been tested on hardware. The owner tests on a Quest 3
through WinlatorXR; until he has, "fixed" means "compiles and reads correctly".

## The one-paragraph summary

The mod is a working skeleton with real depth in the hard part — the helmet
HUD pipeline — and several subsystems that are wired up but not actually
connected to anything. Head tracking, input mapping and the HUD glue are
substantially done. **Haptics are dead code, the flat-screen mode switch sends
a value that the spec says silences the runtime entirely, the sync pixel is
very likely being drawn into an offscreen render target, and the window is
never forced borderless.** Three of those four are in the small set of things
`general_wxr/CLAUDE.md` calls non-negotiable. See [FINDINGS.md](FINDINGS.md).

## Keeping this set useful

If something recorded here turns out to be general — a protocol fact, a trap
any WinlatorXR port would hit — fold it back into `Docs/general_wxr/` as that
folder's own `CLAUDE.md` asks, and note where it came from. If something here
is wrong, correct the file rather than working around it.
