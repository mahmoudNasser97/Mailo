# Changelog

## [0.5.33]
### Fixed
- **Endless fall → get-up → jump → fall loop.** After standing up, the controller reactivated with a
  stale "not grounded" flag (→ Jump) and the collision it had fallen against immediately re-toppled it,
  looping forever and launching the character. Added a **`standingGrace`** window (1 s): for that long
  after standing, `Knockdown()` is ignored (covers impacts, tilt, fall and ram-into-wall) and the
  character is forced grounded, so it settles instead of looping.
- **Body trailed the mouse when turning.** The camera-follow ran through two smoothing layers (rig +
  controller). Removed the rig lerp so the body faces the camera directly; the controller's `yawLerp`
  is the single smoothing (raise it for a tighter turn).

## [0.5.32]
### Changed
- **Camera steers the body (issue #1).** The body now follows the CAMERA yaw — moving the mouse
  rotates the character — so all locomotion is a forward walk. `PlayerDriver`: W/S move forward/back
  only, A/D turn the view (`keyboardTurnSpeed`), the mouse turns too. No lateral input means no
  strafe path at all. Replaces the 0.5.31 face-to-move experiment.
### Fixed
- **Get-up was cut off at ~1/4 and then jumped (issue #2).** `TickGettingUp` forced `Locomotion`
  after a fixed `getUpDuration` (0.85 s) — a quarter of a real ~3–4 s get-up — and the stale-grounded
  flag then sent it to Jump. `BeginGetUp` now measures the actual get-up clip length and the recovery
  ramps strength/anchor over that whole length, standing only when the clip finishes (capped by
  maxDownTime). With the grounded fix (0.5.29) it no longer detours through Jump.

## [0.5.31]
### Changed
- **Face-to-move locomotion (no strafe).** The body now turns to face the MOVEMENT direction and
  always plays the forward walk, instead of strafing. Turning the camera changes where "forward"
  points, so the camera steers you and the left/right strafe clips are no longer used (removing the
  strafe foot-slide entirely). `PlayerDriver` reports its move direction to `FirstPersonRig.SetMoveIntent`;
  `UpdateBodyYaw` faces it while moving and keeps the free-look neck while standing still.

## [0.5.30]
### Fixed
- **Looking no longer spins the whole body.** `AddLook` snapped the body yaw to the camera yaw on
  every mouse move, so the character pirouetted with the view. The camera (the eyes) now looks
  freely, and `FirstPersonRig.UpdateBodyYaw` turns the BODY only when you move (it faces your look
  direction) or when your head swivels past `freeLookAngle` (70°) while standing still — a neck, so
  head/eyes look around independently of where the feet point. `bodyTurnLerp` tunes how fast the
  body catches up. Look yaw and body yaw both initialize from the character's built facing.

## [0.5.29]
### Fixed
- **Get-up played the jump/fall clip instead of the get-up.** While getting up the controller is
  inactive, so its `IsGrounded` was stale `false` (left from the fall); the animator saw
  `Grounded = false` and took the `Locomotion → airborne` transition mid-recovery. `DriveAnimator`
  now forces grounded during the `GettingUp` state. (The prone/supine selection was already correct —
  face-up → supine, face-down → prone — but the get-up *clips* must be assigned in the clip set or the
  states are empty and it snaps.)
### Added
- **Directional speed** on `PlayerDriver` — `strafeSpeedFactor` (0.7) and `backwardSpeedFactor` (0.6).
  Moving slower sideways/backward matches the slower strafe/back clips (cuts the remaining foot slide
  when the clips depict a lower ground speed than forward) and reads naturally.

## [0.5.28]
### Fixed
- **Strafe / backward slide.** `MoveX`/`MoveY` were normalized by `maxSpeed`, so a walk-speed
  strafe read as ~0.4 on the axis and the 2D directional blend treated it as *half strafe + half
  forward* — the legs stepped forward while the body slid sideways. They now encode pure DIRECTION
  (normalized by current speed), so a strafe plays the strafe clip at full weight; `Speed` still
  selects the gait tier independently. (Forward was unaffected because the tree has a forward clip
  at both the center and the +Y node.)
### Added
- **Ram-into-a-wall knockdown.** `FloatingCapsuleController` now knocks the character down (and it
  gets up) on a HORIZONTAL collision at or above `knockdownSpeed` (default 2.5 m/s) — so running
  into a box or model topples it, while landing a jump (vertical hit) does not. Complements the
  existing impact-bus knockdown from thrown objects/weapons.
### Changed
- Default `jumpVelocity` 5 → 8 (both profile and controller). Existing characters: set it on the
  profile or the FloatingCapsuleController, it's live-tunable.

## [0.5.27]
### Fixed
- **The character stood leaning forward because the hands were permanently reaching.**
  `PhysicsHand.DriveReach` ran every frame unconditionally, spring-dragging both hands to their
  targets — for a player that target is pinned ~0.5 m in front of the head (FirstPersonRig), so
  the soft arms were always pulled forward and dragged the torso into a lean. The reach is now
  gated: the arm only reaches while actively reaching (grab button held) or holding something,
  and otherwise hangs and follows the animation, so the character stands straight. `alwaysReach`
  (default off) restores the old always-visible-first-person-hands behavior. `PlayerDriver` holds
  the reach while the grab button is down and keeps trying to grab so it catches on contact; NPCs
  gate their reach on whether they are carrying. Runtime change — no rebuild needed.

## [0.5.26]
### Added
- **Foot-motion stride measurement — fixes foot sliding for in-place clips.** The blend
  thresholds and `referenceClipSpeed` previously read `clip.averageSpeed` (root motion), which
  is zero for in-place Mixamo clips, so everything fell back to guessed constants and the body
  outran the feet. `AnimatorGraphBuilder.MeasureDepictedSpeed` now samples a clip on a throwaway
  model copy and averages the backward speed of the planted foot — the true depicted ground
  speed, measured from the feet, works with no root motion. The wizard measures walk & run,
  feeds them to the blend thresholds, sets `referenceClipSpeed` from the walk stride, and caps
  the capsule's `maxSpeed` to the run stride (or ~1.25× walk with no run clip) so the torso
  cannot outrun the legs.
### Changed
- **`CharacterBody.ApplyProfile` no longer overwrites `maxSpeed`** — like `rideHeight`, it is now
  a per-character/per-clip-set measurement baked by the builder, not shared profile tuning.
  (Overwriting it from the profile re-created the foot skating.)
- Diagnostic reports a **locomotion sync** line (referenceClipSpeed vs capsule maxSpeed) and flags
  when maxSpeed outruns the walk stride with no run tier to cover it.

## [0.5.25]
### Fixed
- **The real cause of the permanent crouch: the ride spring sagged under the capsule's own
  weight.** `ApplyRideSpring` used a Force-mode spring with no gravity compensation, so it
  settled where its restoring force balanced the capsule's weight — `mg/k ≈ 0.1 m` BELOW
  rideHeight for a 40 kg capsule at spring 4000. The capsule hovered at ~0.74 m instead of
  0.84 m, the pelvis (anchored to it) rode that low, and the legs bent just to keep the feet
  on the floor. Every "leg" symptom chased in 0.5.11→0.5.24 was downstream of this. Added
  gravity compensation (`Body.AddForce(up * mass * -gravity.y)` while grounded) so the spring
  holds rideHeight exactly and the capsule truly hovers. The kinematic-legs test (0.5.24)
  exposed it: with the legs' hidden weight-bearing removed, the pelvis collapsed — proof the
  capsule was never carrying the body as designed.
### Added
- Diagnostic now reports the **capsule's actual height** above ground (and its sag vs
  rideHeight) plus **how far the pelvis hangs below the capsule** — separating "the capsule is
  sagging" (ride-spring weight sag) from "the pelvis isn't held up to the capsule" (weak anchor
  / legs buckling). Replaces the old sag message that wrongly blamed the leg joints even when
  they reached their targets.

## [0.5.24]
### Added
- **`ActiveRagdollMuscles.debugKinematicLegs`** — an editor test toggle that makes the leg
  chain (thigh/shin/foot) kinematic and drives it straight from the animation's local pose,
  with zero joint physics. Isolates the leg drive from the rest of the body: if the character
  stands cleanly with this ON, the remaining problem is purely the leg PHYSICS drive; if it
  still looks wrong, the cause is upstream (pelvis / torso / capsule / the animation itself).
  Bones stay children of the pelvis, so copying the LOCAL rotation keeps the leg attached and
  posed like the clip; the joint target is still updated so the hip joint applies no reaction
  to the dynamic pelvis. Restores dynamic legs the instant the flag is turned off. Play only.
### Fixed
- **Made the strong-leg fix actually reachable on an already-built character.** 0.5.22
  put the leg boost in the leg *group* multiplier (a nested serialized array element),
  which a code default only reaches on a rebuild — so undoing the 6000 baseSpring test
  without hand-editing that array dropped the legs back to ~3000 and the character
  re-folded (knee 25° short, hips sagged). Moved the boost to a **top-level
  `legStrengthMultiplier` field (default 2.5)** on `ActiveRagdollMuscles`, applied in
  `Bind()` to leg bones on top of their 1.2 group. Because it is a NEW serialized field,
  Unity fills it from the initializer on the existing component the moment the script
  recompiles — so it takes effect with **no rebuild and no array editing**, just re-Play.
  Net leg spring = baseSpring(2500) × 1.2 × 2.5 = 7500, the drive that stood. Reverted
  the 0.5.22 leg-group default back to 1.2 so a future rebuild does not double-apply.

## [0.5.22]
### Fixed
- **The character now STANDS on near-straight legs — the leg fold is resolved.**
  0.5.21 proved the legs were buckling under body load (both hip and knee ~25° short
  of the animated pose, hips sagged ~0.24 m), not a joint axis/limit and not the
  pelvis. Root cause: the **leg muscle group was far too weak for a load-bearing
  group** — `springMultiplier = 1.2`, barely above the torso's 1.0, giving a leg
  drive of only ~3000 that lost to the standing load near full leg extension. Raised
  the leg group to **3.0** (leg spring ~7500 at baseSpring 2500). Confirmed live by
  bumping baseSpring to 6000 as a no-rebuild test (leg spring 7200): the knee closed
  from 25°-off to **1°-off (171° vs 172°)**, the hips rose from 0.60 m to 0.70→0.85 m,
  and the pose went from a folded banana to a standing humanoid. The surgical
  leg-only fix keeps the arms/hands soft (their physicality is the interaction layer)
  instead of stiffening the whole body. Residual: the hip carries a mild ~13° forward
  lean and ~0.15 m soft sag — normal for an active ragdoll; push the leg multiplier
  toward 4 for a tighter hip if wanted. **Closes the 0.5.11→0.5.21 leg saga.**
### Note
- The `RagdollProfile.legStrength` / `armStrength` fields are still **dead** —
  `ApplyProfile` never applies them (per-limb tuning lives in `ActiveRagdollMuscles.groups`).
  Left unwired here on purpose: wiring them while an existing profile asset still holds
  the old `legStrength = 1.2` would silently overwrite the new leg group 3.0 and
  re-break the stand. Wire them only together with bumping the asset. Tracked as cleanup.

## [0.5.21]
### Fixed
- **The 0.5.20 diagnostic gave a wrong conclusion — corrected.** With the pelvis
  reading `up off 1°, forward off 100°` it printed "fix the pelvis lock", firing on
  `up > 12 OR forward > 12`. That is a misread: a matching `up` means the pelvis is
  **upright** (no tilt to delete), and `forward off 100°` with matching `up` is a
  pure **yaw** about vertical — which `PelvisAnchor.ApplyYaw` deliberately slaves to
  the controller, not the puppet. A yaw about vertical cannot change a thigh's
  angle-from-vertical, so it cannot fold the leg. The pelvis conclusion now separates
  **TILT** (`up`, a genuine lock/upright fix) from **YAW** (`forward`, expected and
  harmless to leg posture).
### Added
- **LOCAL drive error** (physical joint local rotation vs the commanded puppet local
  rotation, for hip and knee). This is pelvis-independent — the world-space hip
  flexion can't tell "the joint missed its target" from "the joint reached target but
  the pelvis is mis-posed", this can. Both pelvises upright yet a large local error
  proves the **hip joint is missing its animated target by ~25°** (same category as
  the knee in 0.5.17), not a pelvis problem.
- **Hip-height sag** (physical hip Y above ground vs rideHeight). A large shortfall
  with both leg joints off target is the fingerprint of the whole leg **buckling**
  under body load near a straight-leg stance (rideHeight ≈ full leg reach) — which
  is addressed by rideHeight + leg drive, not the pelvis lock. Together these two
  numbers discriminate "one joint's limit/axis" from "whole-leg buckle" in one run.

## [0.5.20]
### Added
- Diagnostic now reports the **physical pelvis orientation vs the puppet's** (up
  and forward angle difference). 0.5.18 localised the leg fold to the hip
  over-flexing 24°; this splits whether the cause is the **pelvis lock** rotating
  the pelvis off the animation (deleting its pelvic tilt, so the thighs inherit the
  offset) or the **hip joint** alone settling wrong with the pelvis correct — two
  different fixes. Confirm before changing, as with the knee.

## [0.5.19]
### Fixed
- **rideHeight now measured from the PUPPET's animated standing pose**, not the
  bind pose (the straight-leg ceiling, ~0.84, too tall for a bent-knee stance) nor
  the folded physical pose (the symptom). `CalibrateRideHeight` eases rideHeight
  toward the puppet's hip-to-sole (hip world Y − lowest puppet foot bone Y +
  ankle-to-sole offset) while standing. The puppet is driven only by the animation,
  so it's a clean reference the physics can't corrupt — which is why this doesn't
  spiral the way measuring the physical pose did (that fed back and sat the
  character down). Re-enabled `autoCalibrateRideHeight` by default. Note: only
  meaningful with a REAL idle clip assigned; a T-pose take measures the ceiling.

## [0.5.18]
### Added
- Diagnostic now reports **hip flexion** (thigh vs straight-down), puppet vs
  physical, alongside the knee angle. 0.5.17's hinge-unlock did not fix the bent
  legs, so this locates whether the failing joint is the **hip** (thigh swung
  forward into a sit — points at hip drive / pelvis lock) or the **knee**, instead
  of guessing at a third fix.

## [0.5.17]
### Fixed
- **Legs settled ~35° short of the animated pose at full drive (knee 132° vs a
  167° target), systemically, on every character.** Not spring strength — a 35°
  gap is far too large for a load equilibrium against a 3000 drive (that balances
  within ~1°), so a degree of freedom was locked out. `BuildJoints` hardcodes the
  hinge bend axis to local X and **Locked** the other two. X is the real bend axis
  for the arms (which worked) but a few degrees off for the legs on a rig whose leg
  bones aren't oriented like its arm bones — so the knee's true bend was partly
  along a Locked axis and the drive could never reach the pose. Hinge secondary
  axes are now **Limited to ~12° of slop** instead of Locked, letting the joint
  find its real bend direction while still reading as a hinge. Rebuild required.
### Changed
- The rideHeight diagnostic now compares rideHeight to the legs' true **reach**
  (sum of the puppet's bone lengths) instead of the straight-line hip→sole in the
  current pose. When the legs fold, the live span shrinks and the old check blamed
  rideHeight backwards — it would have advised squatting the character to match a
  pose that was itself the bug.

## [0.5.16]
### Added
- Diagnostics now compare the **left knee angle on the puppet (animation) vs the
  physical rig** in Play. This splits the leg-bend cause definitively: puppet
  straight + physical bent → the physics isn't reaching the animated pose (leg
  limits / drive / hip orientation); puppet bent → the retargeted clip is itself
  bent in-engine (avatar/retarget), even if it looks straight in the Mixamo
  preview. Added because the user confirmed the animation is fine on Mixamo and the
  bend reproduces on other characters — i.e. it's systemic, not this clip.

## [0.5.15]
### Changed
- **Reverted 0.5.14's rideHeight auto-calibration to a safe, non-spiralling form
  and turned it off by default.** Chasing the live hip height drove a rig whose
  idle bends the legs into a deep sit (rideHeight fell 0.84 → 0.42): lowering the
  hips makes already-bent legs bend more, so the target keeps dropping. The
  measurement proved the real cause — the character's straight-leg height is
  0.84 m but its idle settles at 0.42–0.59 m, i.e. the retargeted idle animation
  bends the legs deeply. No rideHeight can straighten a bent reference pose; the
  physics is faithfully copying it. Calibration now only closes a genuine
  foot-to-ground gap (feet dangling or sinking), which is stable, and is off by
  default. The remaining leg-pose issue is animation-fit, tracked separately.

## [0.5.14]
### Fixed
- **Banana persisted after 0.5.13 because the builder can only measure the BIND
  pose** (straight legs → 0.84 m), while the character actually stands in the idle
  pose with bent legs (~0.59 m). `CharacterBody` now **auto-calibrates `rideHeight`
  at runtime**: while standing grounded and nearly still, it eases `rideHeight`
  toward the live hip-above-ground height, converging to the point where the anchor
  does no vertical work and the feet just reach the floor. This dissolves the
  anchor-vs-legs fight for any rig and any idle pose without hand-tuning, and is
  what makes the floating-capsule design work on non-human proportions. Gated to
  grounded + low speed so a shove, step or walk frame never bakes a wrong height
  (`autoCalibrateRideHeight`, on by default).

## [0.5.13]
### Fixed
- **"Banana" pose — torso upright but the lower body curling backward.** `rideHeight`
  was a fixed `0.9` (a human hip height), but the test character's legs are only
  `0.63 m`. The capsule held the hips at `0.9`, the planted feet capped them at
  `0.63`, and the anchor spring fought that `0.27 m` gap permanently, curling the
  body. The builder now **measures `rideHeight` from the character's real leg length**
  (hip to the built foot colliders' sole) instead of the profile default, so the
  capsule holds the hips exactly where the legs put the feet — correct for stylized
  and non-human proportions, not just ~1.8 m humans. `ApplyProfile` no longer
  overwrites `rideHeight` (it's per-character geometry, not shared tuning). The
  diagnostic's foot check now flags rideHeight-vs-leg mismatch in either form
  (dangling feet or sagging hips), not only dangling.

## [0.5.12]
### Added
- Diagnostics now measure, in Play, how far the lowest foot sits above the ground
  and the character's actual leg length (hip→sole) versus `rideHeight`, flagging a
  PROBLEM when the feet dangle. This objectively identifies the "banana" pose —
  torso upright but the lower body curling backward — as `rideHeight` being taller
  than the character's legs (common on stylized/non-human proportions), so the fix
  is measured rather than guessed.

## [0.5.11]
### Fixed
- **Character knocked ITSELF down whenever its legs moved (walk → collapse →
  endless jump/get-up loop).** `logKnockdownCause` showed every knockdown was a
  bone-on-bone impact within the character — thighs slamming the spine
  (`Spine02` ← `LeftUpLeg`/`RightUpLeg`, up to ~54 Ns vs a 12 Ns threshold), the
  two feet colliding, thighs crossing. The joints only disable collision between
  DIRECTLY connected bones, so non-adjacent pairs (thigh↔spine, thigh↔thigh,
  foot↔foot) were free to collide; on a humanoid they overlap constantly once the
  legs move, and the solver's depenetration ejections read as blows. `CharacterBody`
  now disables collision between every pair of its own bones (per-collider-pair, so
  bones still collide with other characters and props — grab/hit/impact intact).
  This was a latent, rig-independent bug masked until the character stood and moved.

## [0.5.10]
### Added
- Diagnostics now report the puppet Animator's runtime state to explain a standing
  character stuck in a T-pose: Animator enabled, Avatar valid/Humanoid, current base
  state, `Speed` param, and — in Play — exactly which clip(s) are being sampled and
  their weights. This distinguishes "no clip sampled" (empty state / missing Idle at
  threshold 0) from "a clip is sampled but not retargeting" (Avatar not a valid
  Humanoid) — the two causes that both look identical (a T-pose) from the outside.

## [0.5.9]
### Fixed
- **Standing character floored itself in a knockdown/get-up loop from its own feet
  touching the ground.** With balance solved, `logKnockdownCause` showed every
  knockdown was an IMPACT on `LeftFoot`/`RightFoot`/`RightLeg` from the static
  environment — the mis-posed legs clip the floor and the solver ejects them with
  large depenetration impulses (seen up to ~47 Ns vs a 12 Ns threshold), which
  accumulate to a knockdown. A character on the floating capsule cannot fall on its
  own, so `ReceiveImpact` now ignores static-ground contacts (source with no
  rigidbody) while Standing. Real blows (rigidbody sources — thrown props, weapons,
  other characters) and the separate fall-speed test are unchanged, so every
  intended knockdown path still fires.

### Note
- This makes the character **stand and stay standing**, but the legs still look
  wrong (splayed / clipping) on the digitigrade test rig. That is the leg-mapping
  work (open issue #1), now cleanly separated from "can it stand".

## [0.5.8]
### Added
- Diagnostics now flag `tiltFailDot >= 0.9` as a PROBLEM (edit mode too). The tilt
  test is `chestUp·up < tiltFailDot` and `chestUp·up` maxes at 1, so a threshold of
  1 knocks down a perfectly vertical character every frame. `logKnockdownCause`
  proved this was happening — the balance work was already complete (`chestUp·up =
  1.00`), and the only thing flooring the character was `tiltFailDot` left at 1
  (a `1`-for-`-1` typo during earlier testing). This guard catches that instantly.

## [0.5.7]
### Fixed
- **Pelvis froze at whatever tilt it had when Standing began**, so a character that
  entered Standing mid-recline (or finished a get-up at an angle) locked in that
  lean. `SetState` now snaps the pelvis to true upright (minimal rotation, keeps
  facing) *before* applying the pitch/roll freeze.

### Added
- `CharacterBody.logKnockdownCause` — logs what triggered each knockdown (impact
  with the source object name, tilt, or fall). Added to diagnose a standing
  character that repeatedly knocks itself down and loops through get-up
  ("jumping"): the suspected cause is the mis-mapped legs slapping the floor and
  feeding the impact bus, but this proves it rather than assuming.

## [0.5.6]
### Fixed
- **Character draped to the floor even when fully driven with the pelvis anchored
  correctly and the upright spring cranked.** The anchored body is an inverted
  pendulum — its mass sits above the pelvis pivot, so any lean self-accelerates,
  and a spring torque (while also dragging the legs) can't stabilise it; it
  settles into a horizontal "superman" pose. The puppet was standing correctly
  the whole time — the physical rig simply couldn't hold the pose it was handed.
  `CharacterBody` now hard-freezes the pelvis's world pitch and roll (yaw stays
  free to turn) while in the Standing state and releases the constraint the moment
  it goes down. Same guarantee the floating capsule already gives itself: standing
  is locked, knockdown is a deliberate release. The upright springs still shape
  posture within the lock and take over the instant it is released.

## [0.5.5]
### Added
- `CharacterBody.debugHoldStanding` — a checkbox that pins the character in a
  fully-driven Standing pose and disables knockdown, so balance can be tuned in
  isolation. Needed because the knockdown/get-up loop (and `ApplyProfile`
  restoring `knockdownImpulse` on Awake) made it impossible to freeze the
  character for testing from the inspector alone. Editor testing only.

## [0.5.4]
### Fixed
- **Character sagged to the floor and could never stand, even with knockdown
  disabled.** Two bugs in `PelvisAnchor`, both about holding the body up:
  - **The pelvis had no upright control.** The upright torque only righted the
    chest/spine. The pelvis — the root of the rig and the one bone the capsule
    holds by position — had nothing controlling its pitch or roll, so once it
    tipped there was no way back. The spine could not compensate either: the
    spine→hips joint is limited to ~20°, so a face-down pelvis pins the whole
    body down no matter how hard the chest is righted. The anchor now rights the
    **pelvis** (the stabiliser) and then the chest (posture).
  - **The pelvis position spring used `ForceMode.Force`.** The pelvis carries the
    whole hanging body (~totalMass), not just its own ~10 kg, so a mass-dependent
    force sagged ~6× too far and dropped the hips to the floor. Now applied as
    `ForceMode.Acceleration`, which holds regardless of how much rig hangs off it.
    The existing defaults (spring 900, damper 60) are already critically damped in
    these units — 2·√900 = 60 — confirming this was the intended mode.

## [0.5.3]
### Fixed
- **Character still collapsed on Play after 0.5.2 (capsule floating above a
  limp body).** The build-time ground raycast in `RagdollBuilder` used a `~0`
  mask, so it started just above the hips and hit the character's *own* hip
  collider instead of the floor. `groundY` came out at hip height, the capsule
  was placed ~`rideHeight` too high, and `PelvisAnchor.localOffset.y` was baked
  to about `-rideHeight`. At runtime the ride spring (which correctly excludes
  the character layer) settled the capsule on the real floor, so the pelvis
  anchor targeted the hips ~`rideHeight` below where they belong — below the
  floor — dragging the character down until the tilt test knocked it limp. This
  is the same hip-height failure 0.5.2 believed it had removed; the raycast was
  silently reintroducing it. The builder now casts with the *same* mask the ride
  spring uses at runtime, so build-time and runtime agree on the ground surface.

### Added
- Diagnostics now compute **standing hip height above the capsule's ground**
  (`rideHeight + localOffset.y`) and report a PROBLEM when it is at or below the
  floor. This is deterministic and works in edit mode, so the failure above is
  caught right after a rebuild without entering Play.
- Diagnostics flag a character reported as `Standing` that is already past the
  knockdown angle — previously that state printed only as `info`, so a floored
  character could return "No blocking problems found."

## [0.5.2]
### Fixed
- **Character collapsed on Play, limbs splayed.** Two causes, both in setup:
  - The controller capsule sits inside the torso and shared the Character layer
    with every ragdoll bone. The solver saw deep interpenetration on frame one
    and blasted the limbs apart. Now excluded per collider pair, at build time
    and again in `CharacterBody.Start()`. Layers cannot solve this — bones must
    still collide with other characters.
  - The upright test assumed the chest bone's local +Y is world up. That holds
    for Mixamo rigs and not for many others, so a custom rig read as permanently
    face-down and was knocked down every frame. Both `CharacterBody` and
    `PelvisAnchor` now sample the real axis from the authored pose at Awake.
- Capsule is now placed `rideHeight` above the ground under the character
  (raycast) instead of guessed from hip height, and `PelvisAnchor.localOffset`
  is measured from the actual rig. The old guess left the pelvis spring fighting
  the character's real proportions.

### Added
- `Tools > Nasser Active Ragdoll System > Diagnose Selected Character` — reports
  missing components, missing scripts, unbound muscles, bone-axis mismatch,
  capsule isolation and animator culling. Run it in Play mode for live state.

## [0.5.1]
### Fixed
- **Missing-script bug.** Unity requires one MonoBehaviour per file with the
  filename matching the class name. Eight classes were sharing files and could
  not be resolved as script assets, so components attached by the wizard came
  through as "The referenced script (Unknown) on this Behaviour is missing!"
  and the character collapsed on Play.
  Split into their own files: `CharacterDriver`, `PlayerDriver`, `NpcDriver`
  (were in `CharacterDrivers.cs`), `NpcHandTargets` (was in `FirstPersonRig.cs`),
  `GripPoint` (was in `Grabbable.cs`), `ImpactRelay` and `Projectile`
  (were in `ImpactSystem.cs`).
  Non-MonoBehaviour types are unaffected and still share files: `Impact` and
  `IImpactReceiver` in `ImpactSystem.cs`, `RbCompat` and
  `ConfigurableJointExtensions` in `JointMath.cs`.

## [0.5.0]
### Changed
- **Renamed to Nasser Active Ragdoll System.** Package id is now
  `com.nasser.active-ragdoll-system`.
- Namespace `ActiveRagdoll` → `NasserActiveRagdoll`
  (editor: `NasserActiveRagdoll.EditorTools`).
- Assemblies renamed to `NasserActiveRagdoll.Runtime` / `.Editor`.
- Menus moved to `Tools ▸ Nasser Active Ragdoll System` and
  `Assets ▸ Create ▸ Nasser Active Ragdoll System`.
- Console prefix is now `[Nasser ARS]`; the wizard window is titled `Nasser ARS`.
- Removed stale references to the pre-wizard manual menu items from the
  architecture doc.

### Upgrading from 0.4.x
Delete the old package before installing this one — the two define the same
class names in different namespaces and will collide. Any of your own scripts
that `using ActiveRagdoll;` need updating to `using NasserActiveRagdoll;`.

## [0.4.1]
### Fixed
- `AnimatorUpdateMode.AnimatePhysics` is deprecated in 2023.1+/Unity 6; now
  compiles to `AnimatorUpdateMode.Fixed` there via conditional compilation.

### Changed
- Documented against Unity 6.2 (6000.2), including the Active Input Handling
  requirement for `PlayerDriver` and the full list of shimmed Unity 6 API renames.

## [0.4.0]
### Added
- `LocomotionClipSet` — reusable clip asset with a built-in completeness audit.
- `AnimatorGraphBuilder` — nested 1D-of-2D blend trees, airborne chain,
  get-up states, and a masked upper-body carry layer.
- Auto blend thresholds measured from each clip's `averageSpeed`.
- `MoveX` / `MoveY` facing-relative direction parameters.
- Damped parameter writes and playback-rate sync (`syncPlaybackToSpeed`).
- One-click clip import fixer.
- `Documentation~/animation.md`.

### Changed
- Puppet Animator now uses `AnimatorUpdateMode.AnimatePhysics`.
- Wizard animation section rebuilt around the clip set asset.

## [0.3.0]
### Added
- `CharacterSetupWizard` — one-window setup for any humanoid model.
- `RagdollBuilder` — colliders, rigidbodies, limit-configured ConfigurableJoints,
  puppet clone, floating capsule, hands and grab handles, from `Animator.GetBoneTransform`.
- `CharacterRole` (Player / NPC) on `CharacterBody`.
- `FirstPersonRig` — head-mounted camera and aim-linked hand targets for players.
- `NpcHandTargets` — chest-parented hand targets for characters with no camera.
- `CharacterDriver` / `PlayerDriver` / `NpcDriver` — the only layer that differs by role.
- `RagdollProfile` ScriptableObject; `CharacterBody.ApplyProfile()`.
- Animator controller generation: Speed blend tree plus both get-up states.
- Animator parameter driving: `Speed`, `Grounded`, `Carrying`.
- Automatic layer creation via TagManager.
- UPM layout with assembly definitions.

### Changed
- `CharacterBody` replaces `RagdollStateMachine`.
- `PhysicsHand` replaces `GrabController`.
- `TurbulenceField` split out of `CabinPhysics`; `FirstPersonRagdollCamera` replaced by `FirstPersonRig`.
- Characters now drop what they are holding when knocked down.

## [0.2.0]
### Added
- Interaction layer: `Impact` bus, `ImpactRelay`, `Projectile`, `Grabbable`,
  `GripPoint`, `PhysicsHand`, `MeleeWeapon`.

## [0.1.0]
### Added
- Two-skeleton active ragdoll: `ActiveRagdollMuscles`, `JointMath`,
  `FloatingCapsuleController`, `PelvisAnchor`.
