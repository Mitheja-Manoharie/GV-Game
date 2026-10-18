# Agent Controller – Robot Movement and Animation

**Owner:** Alen Oshan (IT24103473) · SE3032 Graphics and Visualization · Student 4 (The Agent Controller)

Scope: turn the path arrays from the Intelligent Systems agents (Security Hunter, Tactical Flanker) into smooth
3D movement, turning and animation, and give every robot the same body (health, gun, spawning).

## Architecture

```
 IS brains (decide WHERE)                     Robot body (decide HOW it looks and moves)
 ─────────────────────────                    ───────────────────────────────────────────
 Security Hunter (Viduna) ─┐                  RobotPathFollower  waypoints → NavMeshAgent steering, smooth turning
                           ├─ IPathFollower ─► RobotAnimator      speed → idle/walk/run blend, shoot, hit, death
 Tactical Flanker (Alen) ──┘   SetPath()      RobotHealth        125 HP, IDamageable for the player's rifle
                               FaceTarget()   RobotGun           6 dmg / 2.5 s / 50 % hit, line-of-fire check
                                              RobotSpawner       ISpawnService for the Facility AI Director
```

| Assembly | Contents | Why there |
|---|---|---|
| `LabLockdown.AI.Agents` | `IPathFollower`, `MoveSpeed`, `PathEndReason`, `IRobotHealth`, `IRobotWeapon` | Shared contract, UnityEngine only, so both brains can use it without depending on the body |
| Assembly-CSharp (`Scripts/Agents`) | `RobotPathFollower`, `RobotAnimator`, `RobotHealth`, `RobotGun`, `RobotSpawner` | The body needs the teammates' `DoorController` and `IDamageable` |
| Assembly-CSharp-Editor | `AgentSetupTools` (menu **GV Game > Agent Setup**) | Builds the rig, clips, animator and prefabs |

## Movement: from path array to motion

`IPathFollower.SetPath(waypoints, speed)` receives the IS module's path: tile centres (Flanker) or tile centres and
door midpoints (Hunter).

| Design decision | Why |
|---|---|
| **Waypoints are authoritative; the NavMeshAgent only steers to the current one** | The IS module chooses the route (e.g. the Flanker's hidden route). Calling `SetDestination(player)` would let the NavMesh pick its own route and undo the AI. The agent still gives local avoidance (other robots, props, carved locked doors). |
| **Corner cutting** (`cornerRadius` 1.2 m) | The next waypoint is taken before the corner is reached, so the robot curves round tile centres instead of stopping at each one. Braking (`autoBraking`) only for the last waypoint. |
| **Joining a new path without snapping** | When a brain re-plans mid-walk, the follower projects the robot onto the new path and starts from the next waypoint ahead, so it never walks back to a tile it has already passed. |
| **Copy of the waypoint list** | Brains reuse their buffers (agreed with the Hunter). |
| **No CharacterController** | `DoorController` treats one as solid and would jam doors on robots. A kinematic Rigidbody + capsule collider instead: moved by the agent, still triggers `DoorAutoOpenTrigger`, and the player's rifle can hit it. |
| **Stuck handling** | No progress for 2.5 s → try to open a closed, unlocked door ahead → if still stuck, `PathEnded(Stuck)` so the brain re-plans. |
| **Speeds** `MoveSpeed` × 3 m/s: Patrol 0.5, Investigate 0.75, Chase 1 | 3 m/s is what the Facility AI's pacing simulation was tuned with (3.5 m/s drops the average player's lowest health from 58 % to 46 %). |

## Rotation

The agent's own rotation is off (`updateRotation = false`); the follower turns the body itself:

- towards where it is moving, or towards `FaceTarget(target)` (e.g. the player while shooting), even while walking;
- with `Quaternion.RotateTowards` at most **360°/s**, so the robot never snaps round on a sharp corner or a re-plan.

## Animation

| Part | Choice | Why |
|---|---|---|
| Rig | Android set to **Humanoid** with an explicit bone map (`DEF-spine` = Hips … from the Core Developer) | Humanoid lets any humanoid clip (Mixamo) play on the custom model. Automatic mapping can pick the Rigify twist bones (`DEF-upper_arm.L.001`), so every bone is mapped by name. |
| Clips | Mixamo: idle, walk, run, firing, hit reaction, death, imported **in place** (root position and rotation baked into the pose) | The NavMeshAgent moves the robot; a clip that also moved it would make it drift off the path. |
| Locomotion | 1D blend tree on `Speed` (m/s): idle 0, walk 1.5, run 3 | Thresholds equal the real speeds (Patrol 1.5, Chase 3), so the legs match the ground speed and the feet do not slide. `Speed` is damped (0.1 s) so the blend does not flicker. |
| Root motion | Off | Movement follows the calculated path exactly. |
| Events | `Shoot` and `Hit` triggers, `Dead` bool from the gun and health events | Animation reacts to gameplay, never the other way round. |

## Combat numbers (agreed with the Facility AI and Security Hunter owners)

| Value | Setting | Reason |
|---|---|---|
| Robot damage per second | 6 damage × 50 % hit chance / 2.5 s = **1.2 HP/s** | The average-player rate in the Facility AI's pacing simulation; the Director's tested pacing stays valid |
| Hit chance | fixed 50 % dice roll | Predictable damage per second (aim physics would vary with distance and frame rate) |
| Range | 12 m, line of fire required | Same as the Flanker's attack range |
| Robot HP | **125** | 5 hits of the player's rifle (25); about 4 s to kill for an average player, as the simulation assumes |
| Speed | 3 m/s | See above |

## Optimisation

- **No per-frame path finding.** Brains re-plan on events (door locked, player changed tile, path ended); the follower
  only calls `SetDestination` once per waypoint.
- **Shared line-of-sight sensor.** The Flanker's 45 rays to the tile centres are computed once per 0.2 s for the scene,
  not per robot.
- **Animator culling** `CullUpdateTransforms`: robots off-screen skip bone updates.
- **Medium-quality obstacle avoidance** on the NavMeshAgent: enough for a few robots in corridors, cheaper than high.
- **Primitive colliders only** (one capsule per robot); the rifle model's colliders are removed.
- **Dead robots** disable their agent, follower and colliders at once (no physics, no avoidance), then are destroyed.

## Setup in Unity (once)

1. **Get the clips** from [mixamo.com](https://www.mixamo.com) (free Adobe account): *Rifle Idle*, *Walking* (or
   *Rifle Walk*), *Rifle Run*, *Firing Rifle*, *Hit Reaction*, *Dying*. Download each as **FBX for Unity, Without Skin,
   30 fps**, with **In Place** ticked for walk and run. Put them in `Assets/Art/Animations/Robot/`.
2. **GV Game > Agent Setup > 1. Configure Android Rig**: Humanoid avatar. Then select `Android.fbx` > Rig > Configure and
   check every bone is green (Pose > Enforce T-Pose if needed). Commit only `Android.fbx.meta`.
3. **2. Configure Robot Clips**: Humanoid, in place, idle/walk/run looping.
4. **3. Build Robot Animator**: `Assets/Art/Animations/Robot/Robot.controller`.
5. **4. Build Robot Prefabs**: `Assets/Prefabs/Robots/Robot_Base` and `Robot_Flanker` (a variant with the Flanker brain).
   Open `Robot_Base` and adjust the rifle under the right hand (`DEF-hand.R`) so it sits in the palm.
   The Security Hunter is another variant of `Robot_Base` named `Robot_Hunter` (Hunter owner).
   **Every robot prefab needs** a script implementing `IDamageable` on its root (`RobotHealth`) and a solid
   (non-trigger) collider. Without them it cannot be shot or counted as dead, and the Facility AI cannot see it standing
   on a spawn point or in a doorway (both its overlaps ignore triggers). A variant of `Robot_Base` has both;
   `RobotSpawner` warns once per prefab if either is missing.
6. Open your dev scene (or `Main.unity` in your turn) > **5. Add Robot Spawner To Scene**: creates `RobotSpawner` under
   `FacilityAI` and assigns it as the Director's Spawn Service. Enable `FacilityAI` to run the Director.

## Play-mode checklist

- [ ] A robot spawned by the Director stands on the NavMesh and runs at about 3 m/s with the run animation (no sliding).
- [ ] It curves round corners and never turns faster than it looks natural; no snapping when its plan changes.
- [ ] Unlocked doors open for it; a door the Director locks makes it re-route.
- [ ] It stops, turns to the player and shoots (tracer, Shoot animation); the player loses about 1.2 HP/s per robot.
- [ ] Five rifle hits kill it; the Hit animation plays on each, then Death; the Director's overlay count drops at once.
- [ ] The Flanker overlay (top right) shows its plan; with the robot selected, red and yellow tiles show what the
      player holds and can see.

## Known limits

- Shooting is a full-body state, so a robot stops to shoot (the Flanker always does). An upper-body layer with an
  avatar mask would allow shooting while walking.
- The rifle's position in the hand is set by eye once in the prefab.
- Robots do not open locked doors; they wait for the Director (by design: locked doors are the Director's tool).
