# IMVRMod — Interstellar Marines via WinlatorXR

This is a WinlatorXR VR port. Read `Docs/general_wxr/CLAUDE.md` before planning
or writing any code — it carries the standing choices not to relitigate, the
non-negotiables in the code, and the working method.

Then read, in this order:

- **`Docs/ClaudeCode/CURRENT_FEATURES.md`** — what the mod does today, and a
  list of things that look like bugs but are load-bearing. Read this before
  "tidying" anything.
- **`Docs/ClaudeCode/STATUS.md`** — where the port actually stands, and which
  claims in the root `README.md` and `Docs/MetaAI/` are no longer true.
- **`Docs/ClaudeCode/ARCHITECTURE.md`** — what the code does at runtime.
- **`Docs/ClaudeCode/FINDINGS.md`** — known defects, with file and line.
- **`Docs/ClaudeCode/NEXT_STEPS.md`** — the ordered plan.
- **`Docs/MetaAI/`** — the history of how the code got here, including the
  dead ends on the HUD pipeline. Still the best record of *why* things are the
  way they are; see `STATUS.md` for the handful of places it is wrong.

## Hard constraints

- **Unity 4, C# 3 (`LangVersion 3`), .NET 3.5, x86, BepInEx 5 + Harmony.** No
  interpolated strings, no `?.`, no `var` outside a method body, no tuples;
  `Path.Combine` takes two arguments.
- **Roughly half this mod reaches into the game by string-named reflection**,
  which the compiler cannot check. Verify member names against the assemblies
  in `Libs/` before trusting a field name from any document, including these.
- **Do not "fix" the helmet/HUD system by removing it.** The bent-NGUI-onto-
  curved-glass visor is the game's identity, and the HUD glue is the hardest
  working part of this mod. `Docs/ClaudeCode/ARCHITECTURE.md` has the pipeline.
- **`HUDTreeDumper` on F9** writes `hud_tree.txt` next to the DLL. Use it
  instead of reasoning about the scene graph.
- Testing happens on a Quest 3 through WinlatorXR, by the owner. Nothing is
  done until he has verified it on hardware. One change per deploy.
