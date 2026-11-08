# Controller initialization and diagnosis

## Findings

At base commit 4194c3e, the Koikatsu VRGIN submodule is
lotsofbearsKoikatsu/VRGIN at 71fcc214. ControlMode.CreateControllers assigns
Left/Right, pairs Other, and adds tools. Controller.OnStart enables its
selected tool. Tool.OnStart resolves Tracking, Owner, and Neighbor.
GameplayTool.OnStart then unconditionally disabled itself. Its only explicit
reactivation path was OnRenderModelLoaded, broadcast from Controller's
SteamVR RenderModelLoaded listener for a child model.

GameplayTool.OnUpdate is the entry point for HandleNativeInput, which calls
FrameKoiNativeInput.Poll/UpdateActionState. Resolving the action set during
Initialize does not prove UpdateActionState ever executed. The reported log
sequence is consistent with the startup self-disable blocking all polling;
it does not prove why the runtime omitted the model callback.

The tool now stays under VRGIN's selected-tool/focus enablement, resolves its
hand index from Owner == VR.Mode.Left/Right (never from tracked-device numbers),
and initializes menu helpers once after Tool.OnStart. Updates wait for valid
ownership and tracking. Global menu buttons can work before HandHolder
creation; hand-dependent dispatch and the legacy path wait for both hands.
The model notification is informational and never changes enabled state.
The menu laser already supports a controller-transform fallback when no tip
attachment exists. HandHolder tolerates a missing Model component.

Native state is released into the scene that received it, before interpreter
and scene handler teardown. Disable, disconnect, polling failure and destruction
release held native actions and clear the controller's analog axis. Menu
handlers release their own synthetic mouse input and destroy laser resources
with the tool. Native grips still dispatch only to scene hands. The legacy
fallback and its existing opt-in world-grip setting remain; defaults and
physical action bindings are unchanged.

## ABI audit

Reference: [Valve's generated OpenVR C# header](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr_api.cs),
IVRInput, InputDigitalActionData_t, InputAnalogActionData_t, VRActiveActionSet_t.
Slots 0/1/2/4/5/6 are SetActionManifestPath, GetActionSetHandle, GetActionHandle,
UpdateActionState, GetDigitalActionData and GetAnalogActionData. Slot 3 remains
GetInputSourceHandle. The existing Cdecl export and StdCall delegates match
Valve's declarations; no slot/signature defect was found in this prefix.

Explicit Pack=8 preserves the Windows ABI used by the Windows plugin under
Proton. Native bool fields marshal as one-byte I1. Runtime validation checks:

| Structure | Size | Key offsets |
| --- | --- | --- |
| ActiveActionSet | 32 | three uint64 handles, padding, int32 priority |
| DigitalState | 24 | origin 8, state 16, updateTime 20 |
| AnalogState | 48 | origin 8, x 16, updateTime 40 |

Initialization fails back to legacy input if validation fails. These checks
and a simulated delegate table test validate managed layouts/call arguments;
only a runtime headset test can confirm SteamVR action delivery on Steam Frame.

## Bounded logs

Automatic messages begin with `FrameKoi input:`. They include tool activation,
ownership waits, initialization with hand/device indices, optional render
callbacks, ABI validation, action handle errors, polling success, active/inactive
action states, digital transitions, analog moving/idle changes and dispatch
to a named scene.

Polling success logs at most six times, five seconds apart. Action state and
dispatch logs cap at twelve per action; unchanged reads produce no messages.
The existing opt-in axis diagnostic caps at fifteen samples per hand, at most
one per second. All these diagnostics share a 300-message session budget
(plus one exhaustion message), reset on native initialization. Restart to
capture a fresh session. Fatal native initialization/polling warnings remain
outside the budget. No diagnostics change the physical control layout.

## Build and install

The Windows build workflow runs the simulated controller harness and builds
Koikatsu and Sunshine using their actual pinned VRGIN submodules. The harness
links the production GameplayTool and FrameKoiNativeInput; Unity/VRGIN/game
boundaries and native function responses are simulated.

From a full recursive checkout, run:

```powershell
dotnet run --project tests/ControllerInitialization/ControllerInitialization.csproj -c Release
dotnet build KKS_VR/KKS_Game.csproj -c Release
msbuild KK_VR/KK_Game.csproj -restore -p:Configuration=Release
```

Follow the workflow's .NET 3.5 reference-assembly and Sunshine Unity-package
setup steps when needed. Download the FrameKoi-test-binaries artifact from
the successful workflow run for the fix commit.

With the game stopped, back up the installed main-game plugin DLL. For RX22
install `bin/KK/KoikatuVR.dll` in the existing main-game VR plugin directory;
for Sunshine use `bin/KKS/KKS_MainGameVR.dll`. Replace the existing matching
DLL, avoiding a second copy elsewhere in BepInEx/plugins. Preserve the rest
of the working VR installation. Put `SteamVR/actions.json` and
`SteamVR/bindings_frame_controller.json` in a SteamVR directory next to that
DLL, or retain the configured absolute manifest path. Keep native SteamVR
input enabled and restart using the existing Proton launch options.

Keep experimental left-stick movement and world-grip fallback off. Check for
both tool-initialized messages, then native polling and active action messages.
Exercise View/Menu/Y, triggers, grips, sticks, reconnects and scene changes
using [the headset checklist](HEADSET-TEST.md). No Steam Frame hardware test
is implied by a passing build or simulated test.
