# IMVRMod — Interstellar Marines via WinlatorXR

This is a WinlatorXR VR port. Read `Docs/general_wxr/CLAUDE.md` before planning
or writing any code — it carries the standing choices not to relitigate, the
non-negotiables in the code, and the working method.

Then read, in this order:

- **`Docs/OpenCode/DeepSeek41Flash/`** — the current reconciliation. Start with
  `CURRENT-STATE.md` for the repo/build/config snapshot, then `HANDSHAKE.md` for
  the XrApi handshake, then `CODE-MAP.md` and `NEXT-WORK.md`.
- **`Docs/ClaudeCode/`** — **archived** 30 Oct audit through the 3 Nov device
  tests. Still excellent on the HUD pipeline and the *why* behind load-bearing
  oddities, but do not treat it as current after 3 Nov.
- **`Docs/MetaAI/`** — **archived** session history, including the HUD dead
  ends. The best record of *why* things are the way they are; not current fact.

## Hard constraints

- **Unity 5.6.7, C# 3 (`LangVersion 3`), .NET 3.5, x86, BepInEx 5 + Harmony.** The
  engine is Unity **5.6.7** - BepInEx logs it on startup - not Unity 4 as the older
  docs say. `LangVersion 3` is a project choice, not an engine limit, so: no
  interpolated strings, no `?.`, no `var` outside a method body, no tuples;
  `Path.Combine` takes two arguments.
- **Roughly half this mod reaches into the game by string-named reflection**,
  which the compiler cannot check. Verify member names against the assemblies
  in `Libs/` before trusting a field name from any document, including these.
- **Do not "fix" the helmet/HUD system by removing it.** The bent-uGUI-onto-
  curved-glass visor is the game's identity, and the HUD glue is the hardest
  working part of this mod. `Docs/OpenCode/DeepSeek41Flash/CODE-MAP.md` has the
  current pipeline; the archived `Docs/ClaudeCode/ARCHITECTURE.md` has more
  history.
- **`HUDTreeDumper` on F9** writes `hud_tree.txt` next to the DLL. Use it
  instead of reasoning about the scene graph.
- Testing happens on a Quest 3 through WinlatorXR, by the owner. Nothing is
  done until he has verified it on hardware. One change per deploy.
