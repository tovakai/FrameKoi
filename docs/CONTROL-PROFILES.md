# Steam Frame situational controls

This development branch uses native SteamVR action handles, semantic actions,
a context router and game/UI adapters. Physical JSON bindings are unchanged.
The latest profiles intentionally replace the old Vive-style button meanings.
Hardware-confirmed locomotion, hand calibration, body visibility and button
crouch are retained. New profiles/presentation still need headset validation.

## Controls

| Input | Roaming | Special scene | Dialogue / Menu |
| --- | --- | --- | --- |
| Left stick | Existing analog game locomotion | Scene movement hook, only if supported | Navigate choices/UI, repeat every 0.3 s |
| Right stick | Existing smooth/snap/off yaw | Head-pivot yaw | Yaw unless modal UI captures it |
| Left/right grip | Independent hand grab | Independent hand grab | Hand grab where valid; blocked by modal UI |
| Left/right bumper | Corresponding laser selects UI | Corresponding laser selects UI | Corresponding laser selects UI |
| Left/right trigger | Contextual hand action; never walking | Existing contextual hand action | Contextual hand action where valid |
| A | Interact with game-validated target | Primary right-hand scene action | Advance text / confirm focused choice |
| B | Back where available | Back / cancel held interaction | Back only where the UI permits |
| X | Pause / unpause | Pause / unpause | Pause where permitted |
| Y | Crouch / stand | Existing scene secondary action | Reserved |
| Left stick click | Sprint toggle | Reserved | Reserved |
| Right stick click | Upright | Upright / recenter open UI | Upright / recenter panel |
| D-pad Up | Quick travel | Existing scene Up command | Previous choice |
| D-pad Down | Reserved | Existing scene Down command | Next choice |
| D-pad Left/Right | Left reserved / next period | Existing scene commands | Previous/next category |
| View | Fullscreen HUD show/hide | Scene controls | Recenter panel |
| Menu | Original status menu | Scene controls | Recenter panel |

Grips cannot start GripMove. Bumpers cannot reach the old touchpad/tool-cycle
handler. Scene Y retains that secondary behavior, including item cycling when
the corresponding trigger is held. Existing H-scene trigger/grip/D-pad logic
and character handling remain behind the scene adapter.

## Context and release rules

Priority is blocking/modal UI, dialogue/menu, special scene, then roaming.
The adapter reads the game's current interpreter and additive scene stack;
loading and SteamVR input-focus loss suspend routing. It does not use desktop
window focus, which is unreliable for Proton VR.

Every press captures its receiver/release callback. Context changes release
held actions into their original scene/UI, neutralize movement and crouch,
and quarantine every button already held until a physical release. Reconnect
and focus recovery seed current button states without generating presses.
Deflected sticks must return to center after a context transition/reconnect.
Separate per-hand events and shared scene-button hold counts prevent one
button from releasing another's trigger/grab. Remapping also releases and
quarantines old actions. UI callback-driven synchronous scene changes are covered.

## UI presentation

The game still renders its original dialogue/HUD content through VRGIN.
No dialogue scripts, choice logic or HUD data are replaced.

Entering dialogue automatically opens a world-space panel. Defaults: 1.75 m
distance, 25 degrees right, 0.2 m below eye level, 0.65 m panel height. Negative
side angle chooses the left. Placement is sampled on open, once again when the
dialogue camera setup settles, and on explicit recenter. It does not follow
head rotation every frame. On exit, the previous roaming HUD state returns.

View toggles one headset-facing roaming display, 90 degrees wide at 1.25 m.
It has no collider and never enables lasers or captures gameplay input.
Hidden means its GameObject is inactive. It is not split into wrist panels.
Scene controls may use a hand-anchored copy of the original scene interface.
Blocking menus automatically use the world panel.

Lasers are enabled only while an interactive panel is presented. Bumpers select
their own hand's target; a miss does nothing. No finger/tool mode is required.
uGUI uses direct pointer-down/up/click events restricted to UI graphic raycasts,
including modal-root filtering. Scene changes cancel clicks instead of activating
the next panel. A uses direct selected-control/dialogue confirmation, never an
unrestricted world mouse click.

## Configuration

All new options are in 0. General:

| Setting | Default |
| --- | --- |
| FrameKoi dialogue distance | 1.75 m |
| FrameKoi dialogue side angle | +25 degrees (right) |
| FrameKoi dialogue vertical offset | -0.2 m |
| FrameKoi dialogue panel height | 0.65 m |
| FrameKoi fullscreen HUD width | 90 degrees |
| FrameKoi roaming/scene/menu binding overrides | Empty, use defaults |
| FrameKoi scoped compatibility input | true |

Override format: semicolon-separated physical=semantic pairs, for example
y=Crouch;dpad_left=None. Action names are in FrameKoiSemanticActions.cs.
Invalid names are ignored. The profile overrides are separate and apply live.
Placement changes apply at the next open/recenter. Keep the working hand
calibration (this installation: left 45/0/90, right 45/0/-90) and locomotion
settings. The SteamVR physical binding JSON does not need to be redesigned.

## Direct APIs and explicit remaining compatibility

- Quick travel/status/next period call the game's OpenMapMoveAllMenu,
  OpenStatus and NextCycle methods. Their original shortcut/event guards remain.
  An unavailable version-specific method logs a bounded warning and does nothing.
- Action-point interaction calls the existing scene-event coroutine where
  exposed. Older gate/NPC activation still uses the previous target-validated
  secondary-click path, only with scoped compatibility enabled.
- Text advances through ADV RequestNextLine. Existing scroll-to-advance remains
  a compatibility fallback on versions without that method.
- uGUI selection/confirmation/navigation use Unity event APIs. Original IMGUI
  panels may still require desktop cursor/click compatibility. Disabling scoped
  compatibility disables those fallback clicks; it does not invent world clicks.
- X invokes an exposed original configuration/pause button when available.
  Otherwise it owns a reversible Unity timescale pause. It never unpauses a
  pause owned by an unrelated blocking modal. X/B can close known configuration/
  exit menus through their permitted cancel controls. Mandatory menus remain open.
- No general safe analog reposition API has been identified for scripted H scenes.
  Their scene-movement hook defaults to neutral, retaining existing scene
  commands instead of translating the VR origin or characters directly.
- The display-only HUD preserves all original information. Status is on Menu,
  travel on D-pad Up, next period on D-pad Right and pause/configuration on X.
  Save/load, phone, help, individual minimap controls and mod-added HUD functions
  have no dedicated semantic shortcut yet. Use the game's desktop UI or existing
  relevant game menu; they are not exposed as laser controls on the roaming HUD.

## Install and hardware checks

Use FrameKoi-test-binaries from the successful CI run for the new commit.
Stop Koikatsu RX22, back up BepInEx/plugins/KK_MainGameVR/KoikatuVR.dll and replace
it with bin/KK/KoikatuVR.dll. Keep VRGIN, SteamVR manifests and the calibrated
configuration. Sunshine uses bin/KKS/KKS_MainGameVR.dll in its matching directory.
Avoid duplicate plugin DLLs. New config entries are added on startup.

1. Check native tools/actions still initialize, then movement, smooth turning,
   hand alignment, body visibility and Y crouch/stand.
2. Toggle View while moving: one readable HUD, no wrist panels or laser capture.
3. Enter dialogue without pressing Menu. Check side/below placement, readability,
   face clearance and world stability while looking around.
4. Use A to advance/confirm, D-pad/stick navigation, and each bumper independently.
   Hold A/grip/trigger across dialogue entry/exit: no new-context press may fire.
5. Open travel/status/configuration and mandatory confirmation/tutorial menus.
   Check cancellation limits, pause ownership and restoration of roaming controls.
6. Test H-scene grips/triggers/D-pad and Y secondary/tool selection. Bumpers must
   never cycle items, even with a trigger held.
7. Test controller reconnects, SteamVR dashboard focus and interrupted loading.
   Release held buttons and center sticks before actions can resume.
8. Check scene/head-attached POV turning, original mod menus and IMGUI compatibility
   on actual hardware. Simulations and compilation do not establish these results.
