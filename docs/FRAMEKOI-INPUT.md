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
- **New:** `1. Roaming → FrameKoi right stick turning = true` (default) polls the **right VRGIN controller's actual analog X axis** on every controller update, independently of the left controller. Smooth turning scales with axis deflection; snap turning activates past 0.75 deflection and rearms after centering.
- `1. Roaming → FrameKoi right stick deadzone = 0.2` (range 0.05–0.5) suppresses small deviations. Legacy left/right directional turn events are suppressed during roaming when this mode is enabled, preventing double-turns.
- **Compatibility fallback:** set `FrameKoi right stick turning = false` to restore the existing legacy directional events. The older `Continuous rotation` setting remains for compatibility but FrameKoi turning mode takes precedence.

### Independent analog stick inputs
- The left and right tracked VRGIN controllers now each pass their own `GetAxis()` to the scene through virtual hooks on `SceneInput`; only roaming acts on them. No SteamVR input-action manifest has been implemented.
- Right stick controls roaming yaw (if mapped by SteamVR), with a configurable deadzone and smooth/snap/off mode.
- `1. Roaming → FrameKoi experimental left stick movement` is **OFF by default**. When explicitly enabled, the left stick moves the VR origin in the headset's horizontal facing direction and synchronizes the protagonist through `PlayerToCamera()`. Its speed and radial deadzone are configurable. This direct translation **may bypass collisions, navigation and event triggers**. Do not use it for normal play until validated.
- In roaming, controllers assigned an analog role no longer send their legacy directional callbacks to scene actions while outside menus; otherwise a stick turn/move could inadvertently crouch, dash or interact.
- Releasing a trigger no longer resets the analog snap-turn latch: it just releases the old walking input.
- **Known limitation:** When the stick's analog axis is redirected to a menu, the menu retains legacy direction callbacks. The SteamVR binding still needs validation on actual hardware.

## Not implemented yet

- Native SteamVR action-manifest bindings. The **right analog stick is now polled independently** through the existing per-controller `GetAxis()` method. Whether SteamVR maps the Frame's intended stick to that axis must be confirmed on the actual headset.
- Production-ready left-stick locomotion. The experimental origin translation above is not a collision-safe alternative to the upstream trigger-held walking. Need a verified game movement API or a collision-respecting movement adapter before making it default.
- Direct A/B/X/Y, shoulder, D-pad, and stick-click bindings. We need to map actual OpenVR / SteamVR input action paths and test with the Steam Frame.
- Context-specific UI fallback for menu repositioning after reserving the grips for hands.
- Runtime, compiler, or physical Steam Frame validation. **These edits are committed but untested in-game.**

## Next implementation slices

1. Isolate controller sampling behind a `FrameKoiInput` layer instead of the current small direct analog-right-stick bridge, preserving the existing scene handlers.
2. Add SteamVR action manifest/binding support, handling disconnected/reconnected controllers and press/release state.
3. Replace experimental direct-origin locomotion with collision-aware game movement; retain the independent analog axes, deadzones, and turning policies.
4. Map menu, click, teleports, and scene-specific actions to the spare buttons, with a configuration panel and diagnostics.
5. Test grabbing independently with both hands, transitions between scenes, and menu proximity.
