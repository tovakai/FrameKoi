# Steam Frame hardware follow-up

Based on controller initialization commit 81ceb041. Settings apply live unless
otherwise stated. A successful simulation/build is not a headset test.

## Hand calibration

In 0. General, FrameKoi hand calibration defaults to AutoFrame. It probes
OpenVR's controller type (property 7000) for each tracked hand, independently
of render-model loading. Only frame_controller gets the Frame profile.
AllControllers explicitly forces it; Disabled restores the original pose.
Unknown controller types keep their original defaults. The detected type and
profile selection appear in bounded startup diagnostics.

FrameKoi left hand rotation and FrameKoi right hand rotation default to
(-90, 0, 0) degrees. This is an initial correction for the reported down-facing
hands, not a hardware-measured universal pose. The rotations are independent,
controller-local XYZ vectors; they are not mirrored. The correction rotates
the shared physical hand anchor, so model animation, colliders and grasp
attachments use the same calibrated frame. Existing Adjust model position
and Adjust model rotation still apply as before.

For calibration, use an empty area. With the other axes zero, try X, Y and Z
individually at +/-90 degrees on each hand; verify pointing forward, palm
normal and wrist roll rather than only finger direction. Then tune smaller
increments, both hands extended forward and sideways. Check finger, normal
hand and toy modes against a target and verify the contact location. If the
runtime reports another type, select AllControllers deliberately for this test.

## First-person body

FrameKoi first person body (0. General) defaults to HideBody, the conservative
fallback for stock combined skinned meshes. FullBody disables this feature.
HideArms hides separate arm renderers when recognizable; if a skin binds both
arm and torso bones, or no separate arms can be identified, it logs once and
hides the original body instead. It never edits shared meshes or bone scales.

This feature applies to the roaming protagonist only, with native input
enabled. Existing face/head concealment and event-scene POV remain unchanged.
Renderer enabled states are captured at headset PreCull and restored at
PostRender, with recovery on LateUpdate/disable. Separate spectator cameras see the
original states, including nested camera rendering. Initially disabled
renderers remain disabled. Character/scene/config changes refresh selection.
The tracked hand models are outside the protagonist hierarchy and are never
included. No rig, collider, animator or character GameObject is disabled.

## Left-stick locomotion

FrameKoi game left stick movement (1. Roaming) defaults to true for native
input. The guarded Harmony hook supplies a movement request to the game's
ActionGame.Chara.Mover.Main.Update and replaces its direction-normalization
step. The original movement regulation, cursor/pause and event gates, animator
speed path and NavMeshAgent.Move remain in place. The game's gated movement
request is preserved; looking straight up/down falls back to the origin's
heading rather than disabling movement. Disabled/off-navmesh agents cannot
produce displacement through the game's direct-position fallback under native
stick control.

Movement is head-relative with radial deadzone (default 0.2), continuous
diagonals and analog speed. FrameKoi game movement scale defaults to 1.0
(range 0.25–1.5), multiplying the game's walk/run speed. Left stick click toggles
sprint while moving; right-stick smooth turning settings are unchanged.
The VR origin follows only player displacement already accepted by the game,
retaining the physical headset offset. Stick movement and smooth yaw do not
teleport the protagonist to the headset position.

Triggers request contextual gate/character/action-point interaction instead
of forward walking. Grips keep their hand-only route. If another game build
or mod changes the expected mover pattern, the hook declines to replace it
and logs a warning; legacy trigger walking remains the fallback. Set game
left stick movement false to select that fallback explicitly. The old direct
origin movement option remains separate, experimental and false by default;
it is never selected while the game locomotion hook is active.

The mover path was inspected in the installed KK Assembly-CSharp.dll.
Compatibility with each compiled target does not prove navmesh behavior:
stairs, corners, slopes, gates, off-mesh links and room-scale/yaw combinations
must still be tested in the headset. Large scene warps use the existing
ResetCamera/reload path rather than being treated as stick displacement.

## Input state fixes

A clicks only a targeted floating menu or submits an active/interactable
selected Unity UI control. With neither target, A does nothing. It no longer
injects an unrestricted left mouse press/release into the world. B, X, menu
buttons and the physical binding layout remain as before.

Native game locomotion supplies crouch as an explicit game input state, so
walking does not require holding a synthetic crouch key. D-pad Down still
toggles it. StandUp always reconciles both Ctrl and Z, even if the crouch-key
configuration changed or internal standing state was already reset.
Controller release/disable clears crouch and sprint requests.

Trigger+bumper keeps the existing scene-specific item cycle. Item changes
release the previous handler's temporary interaction, restore visible/collidable
item state, and start with a clean set of animation weights. A layer transition
is canceled before switching items or starting another transition; its coroutine
captures its owning item and cannot write a layer index into the next item.
Cycling during an active grip/grasp is ignored; release the grip and press the
bumper again to cycle.
H-scene handler disable now ends manual item movement and restores hidden
hand rendering, in addition to releasing temporary trigger clicks.

## Install and headset checklist

Download FrameKoi-test-binaries from the successful Windows workflow for the
new branch head. With the game stopped, back up the installed DLL, then replace
BepInEx/plugins/KK_MainGameVR/KoikatuVR.dll with bin/KK/KoikatuVR.dll from the
artifact. For Sunshine use bin/KKS/KKS_MainGameVR.dll in the matching plugin
directory. Retain the existing working VRGIN/runtime files, SteamVR/actions.json,
binding file and native-input configuration. Avoid duplicate plugin DLL copies.
The new settings are added on startup; no configuration reset is required.

1. Confirm both input tools and native actions still initialize; View/Menu
   opens the menu and right stick still turns smoothly.
2. Calibrate X, Y and Z independently for both hands, then verify finger,
   normal hand and toy contacts match the rendered hands.
3. Check HideBody and FullBody in roaming. Try HideArms and inspect its fallback
   message. Verify NPCs and tracked hands stay visible; inspect the desktop view.
4. Walk forward/back/sideways/diagonally, then test walls, stairs, slopes, gates
   and scene reloads. Move physically within the play space before/after turning.
5. Test partial stick displacement, sprint click, stopping, and both controllers
   reconnecting. Check the game-locomotion-installed message; on pattern mismatch
   use the documented legacy fallback.
6. Toggle crouch idle, while starting/stopping movement, then reconnect and
   change scenes. Verify uncrouch always works.
7. Press A over UI and empty world space. Only UI should react. Verify B and
   the working native interaction buttons remain functional.
8. Repeatedly cycle trigger+bumper during/after layer transitions and return
   to finger mode; verify contacts and renderer visibility recover. Release any
   grip/grasp before cycling. Repeat in talk and H scenes.

The simulation links production dispatcher, math, roaming input, render-scope
and guarded Harmony locomotion code. Game input, Unity objects/camera events and
NavMeshAgent responses are simulated. Its boundary test proves routing through
that adapter, not real collision/navigation safety. The animated hand assets,
NPC reactions, nonstandard body meshes, Proton controller-type reporting and
actual Steam Frame poses still require the headset checklist.

## Dialogue pointer and crouch-height follow-up

Hardware feedback confirmed left/right stick locomotion and the calibrated
hands. Preserve the installed calibration: left (45, 0, 90), right (45, 0, -90).
These are saved settings; replacing the DLL does not require resetting them.

The native UI ray now uses tracked-controller forward, independent of render
models and hand-mesh rotation. FrameKoi UI pointer rotation (0. General) defaults
to (0, 0, 0) and can be tuned live without changing the hands. FrameKoi UI pointer
range defaults to 5 meters and is independent of panel scale.

Both controllers can point at UI, including the hand that owns the wrist panel.
Character proximity/busy state no longer blocks visible UI. The nearest active,
rendered, collidable panel is selected on every update. The cursor is positioned
on the first hit before any click dispatch. Rays remain visible on a miss.
Held UI clicks retain desktop cursor ownership through release so the other hand
cannot drag them off target. Losing a panel releases the click and reacquires
other active UI; native cleanup preserves wrist-panel ownership.

A still never injects an unrestricted world click. It clicks a targeted panel,
submits an active selected Unity control, or uses a dialogue-only fallback.
In text-only ADV the fallback uses the existing scroll-to-advance gesture.
For dialogue choices, first confirm highlights a visible choice and the next
confirms it. Roaming has no such fallback.

FrameKoi button crouch camera (1. Roaming) defaults to true. With native game
locomotion, D-pad crouch smoothly lowers the origin by the difference between
Camera height and Crouching camera height (defaults 1.5 and 0.7 meters).
Stand-up reverses only that virtual offset. Real head movement is preserved and
physical crouch detection subtracts the offset to avoid feedback. Controller
release, scene teardown and reset remove any remaining virtual crouch offset.
Disable this option to keep the previous fixed virtual height.

The follow-up tests include production KoikMenuHandler with simulated Unity
raycasts/desktop input, plus virtual/physical crouch and cleanup regressions.
Retest a dialogue from entry through choices and exit, aim from both controllers
while near a character, confirm a newly acquired panel immediately, and try
crouch/uncrouch while stationary and walking. These fixes need a new headset
runtime test; successful CI alone does not establish that result.
