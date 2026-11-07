# HANDSHAKE.md — XrApi 0.4 handshake, as implemented 2026-11-07

This is the implementation record for the handshake fix. Protocol background is
in `Docs/general_wxr/XRAPI_V0_4_DESC.md` section 2.

## Files and paths

| File | Written by | Contents | Lifecycle |
|---|---|---|---|
| `version` | the mod | `0.4` | Written unconditionally at init; rewritten if missing or wrong |
| `vr` | the mod | `VR` | Written at init; rewritten if missing; deleted on clean shutdown |
| `system` | the runtime | 7 lines, see below | Read by the mod; never written |

On device the directory is `Z:/tmp/xr`.

On a desktop tester with no `Z:`, the mod uses `xrtemp` **next to the DLL**
instead. The same binary therefore runs in both places without a compile-time
switch.

## Write policy

`XRAPI.EnsureHandshakeFiles` writes each file only if its trimmed contents
differ from the expected value, so the 2 s watchdog does not rewrite an
unchanged file every tick. Writes use **ASCII**, not default UTF-8, because the
runtime matches `version` with `startsWith` and a stray BOM would make `0.4`
unrecognised.

`XRAPI.TickHandshake` is called from `VRInputRunner.Update` and is rate-limited
to once every 2 seconds. It:

1. recreates the handshake directory if the runtime wiped it;
2. rewrites `version` and `vr` if they are missing or wrong;
3. retries `system` while no detection has been latched;
4. re-applies the detected handedness unless the config is explicit.

At init, `XRAPI.XR_Init` writes the files and reads `system` immediately, then
opens the sockets.

## `system` format used

The mod reads line 1 (manufacturer) and line 2 (product codename), and ignores
the rest. A partial read is **not** latched: if the file has fewer than two
lines or an empty product, the next tick retries.

| Make | Product | Mapped model | `flipHands` |
|---|---|---|---|
| `META` / `OCULUS` | `EUREKA` | Quest 3 | `false` (upright) |
| `META` | `PANTHER` | Quest 3 | `false` |
| `META` | `HOLLYWOOD` | Quest 2 | `true` (upside-down) |
| `META` | `SEACLIFF` | Quest Pro | `true` (untested; docs assume upside-down) |
| `META` | `MONTEREY` | Quest 1 | `false` (unsupported, but no crash) |
| missing / unknown | — | Quest 3 default | `false` |

`flipHands` is a **controller orientation** quirk, not a handedness swap. The
correction is a 180° roll in `VRHands`, not yaw.

## Precedence

1. `flipHands=true` or `false` in `config.txt` — explicit, always wins.
2. `flipHands=auto` or no key — detection decides.

`VRManager.ParseConfig` tracks whether the key was present. A missing key is
treated as `auto`, so a config that predates the feature still benefits from
detection. `XRAPI.ApplyDetectedHandedness` refuses to override an explicit
value.

## Shutdown

`XRAPI.Shutdown` closes both sockets, aborts the receive thread, and deletes
`vr`. It leaves `version` in place: that describes the protocol the binary
speaks, not a live claim that the process still wants VR.

## Failure modes and what the log says

| Symptom | Likely cause | Log line |
|---|---|---|
| No packets at all | `version` missing or unrecognised when the runtime started | `Handshake <dir> version=0.4 vr=VR` — check the directory and the written value |
| Packets stop after a runtime restart | runtime wiped the directory, then read `version` before the 2 s watchdog rewrote it | `... (rewritten)`; a game restart may still be required, because the runtime reads `version` once |
| Hands upside-down on Quest 3 | detection mapped the wrong codename, or an explicit `flipHands=true` is set | `Headset: make=... product=... -> ...` and `flipHands auto -> ...` |
| Hands not matching the setting | an explicit config value is overriding detection | the detection line appears, but no `flipHands auto -> ...` line |
| No detection at all | `system` absent or unreadable | no `Headset:` line; the mod keeps the default Quest 3 mapping and retries |

## Device test, 7 Nov build

1. Log shows `Handshake Z:/tmp/xr version=0.4 vr=VR`.
2. Log shows `Headset: make=META product=EUREKA -> Quest 3, hands upright`.
3. Delete `vr` externally; it reappears within ~2 s.
4. Set `flipHands=true`; the hands roll. Set `flipHands=auto`; they return to
   the detected orientation.
5. Restart the runtime under the game; look for the `(rewritten)` line. If the
   stream stays silent, note whether a game restart fixes it — that is the
   known one-shot read limitation, not a write failure.

## What this does not do

- It does not upgrade past XrApi 0.4. 0.4 is the project's standing target;
  0.6/0.7 are documented in `XRAPI_UPGRADE.md` but are not a roadmap.
- It does not write `input_version`; this port does not use the InputAPI path.
- It does not detect the display or the container; only headset make/product.
