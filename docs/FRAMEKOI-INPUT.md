# FrameKoi controller overhaul

FrameKoi is based on [IllusionMods/KKS_VR](https://github.com/IllusionMods/KKS_VR), which already provides one context-aware gameplay tool per controller, IK grasping, scene-specific input routing, and support for KK/KK Party/KKS.

## Implemented in this fork

### Hand-first grips
- **Default:** `6. GripMove → FrameKoi allow world grip = false`.
- Grip down/up now routes to the active scene input handler, **even when a floating menu is nearby**.
- The legacy fallback that drags the VR world is disabled by default. It can be re-enabled with the setting above.
- Individual hands still use KKS_VR's existing `HandHolder`, `GraspController`, and scene handler logic. This does **not** mean that character touching works in every scene: those handlers decide whether a hand interaction is available.
- This change deliberately no longer permits grip to detach/move the floating menu in hand-first mode. That UI function needs a future independent button binding.
- The grip-up route is paired with the mode used on physical grip-down, so changing the setting mid-press cannot divert the release.

### Roaming turning
- `1. Roaming → FrameKoi turning mode`: **Smooth** (default), Snap, Off.
- `1. Roaming → FrameKoi smooth turn speed`: **90 degrees/second** by default (15–360).
- Snap continues to use the existing `Rotation angle` value.
- Smooth rotation runs at frame time, clamped to 50 ms per update to avoid exaggerated spins following a stall.
- Both turning modes are currently activated through the **existing KKS_VR left/right directional events**. The old `Continuous rotation` config remains for legacy config compatibility but is superseded by FrameKoi turning mode.

## Not implemented yet

- Independent Steam Frame right-thumbstick yaw and left-thumbstick locomotion. The underlying KKS_VR/VRGIN OpenVR layer currently reads a single `GetAxis()` per tracked controller and interprets it as a legacy touchpad/thumbstick. We need a dedicated action source instead of pretending this already provides independent sticks.
- Direct A/B/X/Y, shoulder, D-pad, and stick-click bindings. We need to map actual OpenVR / SteamVR input action paths and test with the Steam Frame.
- Context-specific UI fallback for menu repositioning after reserving the grips for hands.
- Runtime, compiler, or physical Steam Frame validation. **These edits are committed but untested in-game.**

## Next implementation slices

1. Isolate raw controller sampling behind a `FrameKoiInput` layer, preserving the existing scene handlers.
2. Add SteamVR action manifest/binding support, handling disconnected/reconnected controllers and press/release state.
3. Drive movement and analog yaw through separate axis actions; apply deadzones and smooth/snap/off turning policies.
4. Map menu, click, teleports, and scene-specific actions to the spare buttons, with a configuration panel and diagnostics.
5. Test grabbing independently with both hands, transitions between scenes, and menu proximity.
