# Steam Frame native input: physical mapping and integration gate

This directory contains a **proposed native SteamVR Input mapping**. These JSON files are **NOT loaded by the current VRGIN runtime**. Installing them by themselves does not make the extra buttons work.

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

## Prerequisites before testing in-game

1. Replace or upgrade VRGIN's old OpenVR managed API, which currently has **no CVRInput / IVRInput bindings**. The modern SteamVR Input interface needs to exist. Do not use guessed EVRButtonId values for the extra buttons.
2. Set the manifest path to an **absolute** `SteamVR/actions.json` before SteamVR input event polling. Resolve the on-disk manifest relative to the plugin installation rather than assuming a working directory.
3. Resolve action-set and action handles once, update the action set **once per frame**, and read analog/digital state. On disconnect or invalid action state, release held logical inputs.
4. **Replace** the legacy input polling (or explicitly separate compatibility paths), not both simultaneously, otherwise grip/trigger and thumbstick events will double-fire.
5. Dispatch new logical actions to scene handlers with context: roaming, talk, H, and menus. Keep left and right grips reserved for existing HandHolder/GraspController handlers.
6. Package manifest + binding file in release installation, and verify SteamVR's bindings UI sees the `frame_controller` profile.
7. Test on actual Steam Frame under Proton. Validate press/hold/release for all 18 digital actions, independent X/Y axes, scene transitions, lost tracking, simultaneous two-hand grips, and menu proximity.

**Current status:** All 20 native physical inputs have JSON binding *declarations*, not a live runtime mapping. The existing analog and grip changes use the legacy API. **Do not call this test-ready yet.**

References: https://partner.steamgames.com/doc/steamhardware/steamframe/input and https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input.
