# Steam Frame native input: physical mapping and integration gate

This directory contains an **experimental native SteamVR Input mapping** and its accompanying runtime bridge (`Shared/Controls/FrameKoiNativeInput.cs`). The native mode is **OFF by default**. The bundled older VRGIN API lacks `IVRInput`, so FrameKoi now uses a separate interop bridge for the modern SteamVR action API. No game build or Frame headset test has been completed.

## Physical controls and assigned logical actions

| Steam Frame control | SteamVR input path | Action | Proposed gameplay behavior |
| --- | --- | --- | --- |
| Left stick axis | `/user/hand/left/input/thumbstick` | `left_stick` | Roaming locomotion (experimental implementation exists) |
| Right stick axis | `/user/hand/right/input/thumbstick` | `right_stick` | Smooth/snap/off yaw |
| Left stick click | left thumbstick click | `left_stick_click` | Sprint toggle while roaming |
| Right stick click | right thumbstick click | `right_stick_click` | Recenter/upright |
| Left grip | left grip click | `left_grip` | **Left-hand grasp only** |
| Right grip | right grip click | `right_grip` | **Right-hand grasp only** |
| Left trigger | left trigger click | `left_trigger` | Context-sensitive action/interaction |
| Right trigger | right trigger click | `right_trigger` | Context-sensitive action/interaction |
| Left shoulder | left bumper click | `left_bumper` | Secondary action |
| Right shoulder | right bumper click | `right_bumper` | Secondary action |
| D-pad Up | left dpad_up click | `dpad_up` | Interact or previous option |
| D-pad Down | left dpad_down click | `dpad_down` | Crouch / next option |
| D-pad Left | left dpad_left click | `dpad_left` | Previous category |
| D-pad Right | left dpad_right click | `dpad_right` | Next category |
| View | left view click | `left_view` | Toggle menu |
| Menu | right menu click | `right_menu` | Toggle menu |
| A | right A click | `a` | Confirm/primary UI |
| B | right B click | `b` | Back/cancel |
| X | right X click | `x` | Alternate action |
| Y | right Y click | `y` | Toggle menu / context UI |

Steam Frame has D-pad on the LEFT and A/B/X/Y on the RIGHT. Do not assume the same face buttons exist on both hands. Preserve system buttons for SteamVR.

## Current implementation and how to enable it

- The 20 physical inputs are declared in `actions.json` and `bindings_frame_controller.json`.
- `GameplayTool` has a native dispatcher for 18 digital actions, with paired press/release and per-hand grip delivery. The independent left and right stick vector actions feed existing scene hooks.
- To enable, set `1. Roaming / FrameKoi native SteamVR input (experimental) = true` in BepInEx ConfigurationManager (then restart).
- The loader defaults to `SteamVR/actions.json` **next to the installed main-game plugin DLL** (e.g. `BepInEx/plugins/KK_MainGameVR/SteamVR/actions.json`). The release packaging script has been updated to include it. The alternate `FrameKoi native input manifest path` accepts an absolute path.
- If native initialization fails, the legacy polling path remains available. **Do not combine both input systems at once.**
- Native-mode shortcuts currently are: shoulders = scene's previous touchpad click, View/Menu/Y = toggle floating menu, A = mouse left, B = Escape, X = mouse right, left stick click = toggle roaming sprint, right stick click = upright, D-pad = directional scene actions. Trigger actions retain menu/scene context. Both grips are hand-only.
- Roaming left-stick movement remains experimental and **disabled by default** because direct VR-origin translation has not been validated against collision geometry.

## First hardware test gate

1. Compile both main-game projects with game-specific package dependencies and submodules present.
2. Start with legacy mode (native disabled); confirm ordinary movement and hand interactions still initialize.
3. Enable native mode and verify the SteamVR binding UI recognizes `frame_controller` and all 20 named actions.
4. Confirm each digital button down/up and left/right analog vector, then test simultaneous grips, menu proximity, scene transitions, and disconnect/reconnect.
5. Disable native mode and revert to legacy if the action manifest API fails on Proton.

**Not yet tested:** managed/native ABI compatibility, the startup timing requirement (manifest must be registered before SteamVR event polling), SteamVR's mapping of the Frame inputs, and full game behavior. These are not verified just because the source is committed.

## Remaining validation risks



1. Replace or upgrade VRGIN's old OpenVR managed API, which currently has **no CVRInput / IVRInput bindings**. The modern SteamVR Input interface needs to exist. Do not use guessed EVRButtonId values for the extra buttons.
2. Set the manifest path to an **absolute** `SteamVR/actions.json` before SteamVR input event polling. Resolve the on-disk manifest relative to the plugin installation rather than assuming a working directory.
3. Resolve action-set and action handles once, update the action set **once per frame**, and read analog/digital state. On disconnect or invalid action state, release held logical inputs.
4. **Replace** the legacy input polling (or explicitly separate compatibility paths), not both simultaneously, otherwise grip/trigger and thumbstick events will double-fire.
5. Dispatch new logical actions to scene handlers with context: roaming, talk, H, and menus. Keep left and right grips reserved for existing HandHolder/GraspController handlers.
6. Package manifest + binding file in release installation, and verify SteamVR's bindings UI sees the `frame_controller` profile.
7. Test on actual Steam Frame under Proton. Validate press/hold/release for all 18 digital actions, independent X/Y axes, scene transitions, lost tracking, simultaneous two-hand grips, and menu proximity.

**Current status:** All 20 native physical inputs now have binding declarations and dispatcher paths, but native mode is still opt-in and **uncompiled/untested**.

References: https://partner.steamgames.com/doc/steamhardware/steamframe/input and https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input.
