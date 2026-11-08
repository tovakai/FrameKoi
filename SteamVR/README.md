# Steam Frame native input

Native SteamVR actions are opt-in. Keep the working native-input configuration
when updating. GameplayTool initializes independently of controller render models;
the bridge validates OpenVR layouts and updates the native action set once per
frame. Initialization/polling failure retains the legacy input fallback.

Controller initialization and basic actions were reported working on Steam Frame
under Proton at 81ceb041. The later hand calibration, body visibility, locomotion
and state changes require fresh headset validation. CI compilation and simulated
tests do not establish that hardware validation.

## Physical mapping

The bindings are unchanged: the D-pad is on the left controller; A/B/X/Y are on
the right. System buttons stay with SteamVR.

| Control | Logical action | Behavior |
| --- | --- | --- |
| Left stick | left_stick | Analog game locomotion, with guarded legacy fallback |
| Right stick | right_stick | Smooth/snap/off yaw, unchanged |
| Left stick click | left_stick_click | Sprint toggle while moving |
| Right stick click | right_stick_click | Upright/recenter |
| Left/right grip | left_grip / right_grip | Independent hand interactions only |
| Left/right trigger | left_trigger / right_trigger | Context action/menu; legacy forward walking in fallback mode |
| Left/right shoulder | left_bumper / right_bumper | Existing scene secondary action |
| D-pad | dpad_up/down/left/right | Existing scene directional actions; Up interacts, Down toggles crouch in roaming |
| View/Menu/Y | left_view / right_menu / y | Toggle floating menu |
| A | a | Targeted or selected UI confirmation only |
| B | b | Escape/back |
| X | x | Existing right mouse secondary action |

## Installation and configuration

Install SteamVR/actions.json and SteamVR/bindings_frame_controller.json beside
the matching plugin DLL, for example:
BepInEx/plugins/KK_MainGameVR/SteamVR/actions.json. Enable
1. Roaming / FrameKoi native SteamVR input (experimental), then restart.
FrameKoi native input manifest path can override the location with an absolute
path. Do not install duplicate main-game plugin DLLs.

With native input enabled, FrameKoi game left stick movement defaults to true.
It hooks the game's existing mover and follows navigation-accepted player
displacement; it does not enable the old experimental direct-origin movement.
That old option and world-grip fallback remain false by default.

See [hardware follow-up](../docs/HARDWARE-FIXES.md) for calibration, body modes,
locomotion defaults, fallback controls, installation instructions and runtime
risks. Use [the headset checklist](../docs/HEADSET-TEST.md) to verify buttons,
simultaneous input, scene transitions and reconnects.

## Diagnostics

Check both GameplayTool initialized controller messages, native action polling,
active digital/analog actions and scene dispatch. The game locomotion hook has
its own installed/pattern-mismatch message. Diagnostic output is bounded.
See [controller initialization](../docs/CONTROLLER-INITIALIZATION.md) for ABI and
lifecycle details. On a pattern mismatch, legacy trigger walking stays available.
