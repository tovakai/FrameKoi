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
- `1. Roaming → FrameKoi axis diagnostics` is **OFF by default**. When enabled, each controller logs analog `x, y` values at most once per second via VRLog, to verify actual Steam Frame axis bindings. Turn it back off after testing.
- Right stick controls roaming yaw (if mapped by SteamVR), with a configurable deadzone and smooth/snap/off mode.
- `1. Roaming → FrameKoi experimental left stick movement` is **OFF by default**. When explicitly enabled, the left stick moves the VR origin in the headset's horizontal facing direction and synchronizes the protagonist through `PlayerToCamera()`. Its speed and radial deadzone are configurable. This direct translation **may bypass collisions, navigation and event triggers**. Do not use it for normal play until validated.
- In roaming, controllers assigned an analog role no longer send their legacy directional callbacks to scene actions while outside menus; otherwise a stick turn/move could inadvertently crouch, dash or interact.
- Releasing a trigger no longer resets the analog snap-turn latch: it just releases the old walking input.
- **Known limitation:** When the stick's analog axis is redirected to a menu, the menu retains legacy direction callbacks. The SteamVR binding still needs validation on actual hardware.

## Native Steam Frame action mode (experimental)

- `SteamVR/actions.json` declares 18 digital actions + 2 analog stick actions. `SteamVR/bindings_frame_controller.json` assigns each to a documented Valve Steam Frame OpenVR path.
- `Shared/Controls/FrameKoiNativeInput.cs` bridges IVRInput_011 separately from VRGIN's outdated managed OpenVR interface.
- KK/KKS plugin initialization can opt into native mode via `1. Roaming / FrameKoi native SteamVR input (experimental)` (default **OFF**) and an optional manifest path. When missing, it defaults to the installed assembly's `SteamVR/actions.json` sibling file.
- `GameplayTool` routes native grip/trigger/button actions into existing scene handlers, with independent analog sticks, dpad scene directions, shoulders as secondary click, stick-click sprint/upright, A/B/X for click/back/alternate, and Y/View/Menu for the menu toggle. Grip world-dragging is never started in native mode.
- `release.ps1` now bundles the SteamVR action manifest and binding file alongside the main-game DLLs for both original Koikatsu and Sunshine.
- **Uncompiled and headset-untested.** The native interop ABI, manifest registration timing, bindings, and physical input state still need validation. SteamVR's default legacy controller-emulation mode may not expose every button.
- Complete physical mapping: [`SteamVR/README.md`](../SteamVR/README.md).

## Not implemented yet

- Validated binary release and Steam Frame hardware input test.
- Safe, collision-respecting left-stick locomotion. Experimental direct origin movement is off by default.
- Scene-specific UX refinements for the new face buttons and shoulders, and a replacement for moving the floating menu with a grip.
- Reliable SteamVR native fallback while changing scenes or reconnecting controllers, verified under Proton.

## Next implementation slices

1. Build and inspect compiler errors against all game projects and runtime dependencies.
2. Validate all 20 Steam Frame actions on real hardware and Proton; audit press/release and disconnection behavior.
3. Replace experimental direct-origin locomotion with collision-aware game movement.
4. Refine contextual menu handling and add in-game help/diagnostics.
5. Test grabbing independently with both hands, transitions between scenes, and menu proximity.
