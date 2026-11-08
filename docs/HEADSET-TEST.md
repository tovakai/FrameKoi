# FrameKoi Steam Frame headset smoke test

> **Current status:** Source input mapping complete; hardware compatibility is NOT yet confirmed. Native mode is experimental and off by default. Do not mistake this for a released or validated mod.

## Build/install prerequisites

- Install the correctly matched KK, KK Party, or KKS main-game plugin, including its `SteamVR` folder, beside the plugin DLL. Do **not** install multiple incompatible VR plugins simultaneously.
- Keep a copy of the previous working mod and BepInEx configuration.
- On Proton, use the existing known-working BepInEx loader launch options. The DLL override may be needed: `WINEDLLOVERRIDES="winhttp.dll=n,b" %command% --vr`.
- Start once with native mode **OFF** (the default); verify VR initialization and both hand interactions first.
- Enable `FrameKoi native SteamVR input (experimental)` in the BepInEx config and restart. If the action manifest isn't beside the plugin DLL, set `FrameKoi native input manifest path` to its **absolute** location.
- Confirm `FrameKoi native input action set initialized` in the BepInEx log. A `manifest missing`, `IVRInput_011 unavailable`, or `SetActionManifestPath failed` message means native mode didn't initialize; revert to legacy mode while troubleshooting.

## Physical control checklist

Before pressing buttons, verify two `GameplayTool initialized controller`
messages (0 and 1) and `native action polling executing`. A render-model
callback is optional. Then look for `digital ... active=True`, analog
moving/idle messages and `dispatch ... down/up` to separate binding issues
from tool startup or scene dispatch issues. Diagnostics are bounded; restart
for a fresh capture. See [controller initialization](CONTROLLER-INITIALIZATION.md).

| Control | Expected initial native behavior | Pass / Fail |
| --- | --- | --- |
| Left stick | Independent analog axes; optional experimental roaming movement | |
| Right stick | Analog smooth yaw; optional snap/off | |
| Left stick click | Toggle dash while already walking | |
| Right stick click | Upright/recenter orientation helper | |
| Left grip | Left-hand scene grasp, never world drag | |
| Right grip | Right-hand scene grasp, never world drag | |
| Left trigger | Context action/menu interaction | |
| Right trigger | Context action/menu interaction | |
| Left shoulder | Left-hand secondary scene touchpad action | |
| Right shoulder | Right-hand secondary scene touchpad action | |
| D-pad Up | Up directional scene action/interact | |
| D-pad Down | Down directional scene action/crouch | |
| D-pad Left | Left directional scene action | |
| D-pad Right | Right directional scene action | |
| Left View | Toggle floating menu | |
| Right Menu | Toggle floating menu | |
| A | Confirm / primary click (menu laser if available) | |
| B | Back / Escape | |
| X | Secondary / right mouse click | |
| Y | Toggle floating menu | |

Valve's system buttons are left to SteamVR; FrameKoi must not steal them.

## Regression tests

1. Press and release each grip independently, then both together while touching a character. Neither grip may move the VR world.
2. With a grip held, approach and leave the floating menu. Release the grip while pointing at the menu. The previous interaction must release cleanly.
3. Hold an input while the scene changes or the controllers disconnect. After reconnecting, no mouse, grip, sprint or trigger action should remain stuck.
4. In roaming, move the left stick and turn the right stick simultaneously. Verify neither one triggers accidental D-pad actions.
5. Verify smooth / snap / off, including rearming snap by returning the stick to center. Verify physical rotation still works.
6. In talk/H scenes, check that shoulder secondary actions and D-pad directions don't inadvertently trigger legacy movement.
7. Test floating menu actions A, B, X, Y, View and Menu while hands are near a character.
8. Leave experimental left-stick movement **OFF** initially. If deliberately enabled for testing, use a safe empty area; it may bypass collision and navigation checks.

## Failure reports

Record: game variant (KK/Party/KKS), plugin commit SHA, Proton version, SteamVR version, native mode on/off, controller mapping shown in SteamVR, and the last 50–100 lines of `BepInEx/LogOutput.log` around the error. A brief description of which physical button was pressed and in which scene is more valuable than a generic "controls broken".

If native mode fails, disable it and restart. The legacy polling route is intentionally retained as a fallback.

