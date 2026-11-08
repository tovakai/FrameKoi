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
