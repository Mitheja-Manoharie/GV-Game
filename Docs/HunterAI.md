# Security Hunter ("Sound Hunter")

**Owner:** Viduna Abeyweera (IT24103428) · SE3062 Intelligent Systems
**Architecture:** model-based agent (probabilistic belief over the level graph) + A* with dynamic edge costs

The Hunter does not know where the player is. It *hears* events, keeps a belief about where
the player is, and walks to the place that best trades "likely" against "cheap to reach".

## PEAS

| | |
|---|---|
| **Performance** | Locate the player through sound and sight; do not be trivially exploitable (noise spam, door tricks); never freeze or loop |
| **Environment** | Level graph (45 tiles, 5 door edges), door states, noise events, the player |
| **Actuators** | A path (waypoint list) handed to the path follower; doors open by walking through them |
| **Sensors** | `NoiseEmitted(position, loudness, source)`, door state (`Current`, `IsLocked`), line of sight (raycast + field of view) |

## Pipeline

```
Percepts -> Belief update -> Decision (state + target) -> Plan (A*) -> Path follower
```

| Layer | Responsibility | Assembly |
|---|---|---|
| Percepts | Convert Unity events to graph facts (noise at node n, door d is closed, player seen at node n) | Runtime |
| Belief | `BeliefMap`: probability per graph node that the player is there | Core |
| Decision | State machine (Patrol, Investigate, Chase, Attack, Dead) plus utility target selection | Core |
| Plan | A* over the level graph with live edge costs | Core |
| Output | `IPathFollower.SetPath(...)` (implemented by the Agent Controller) | Runtime |

Core has no Unity references (`noEngineReferences`), so every algorithm is unit tested.
Core works in graph node ids and (x, z) floats; Runtime converts to `Vector3`.

## Assemblies (match the Facility AI convention)

`LabLockdown.AI.Hunter.Core`, `LabLockdown.AI.Hunter.Runtime`, `LabLockdown.AI.Hunter.Tests`
in `Assets/Scripts/AI/Hunter/`. Core references `LabLockdown.AI.Facility.Core` for `LevelGraph`
(shared level data; read-only use).

## Sound propagation

Loudness `L` is defined as the **open-air audible radius in metres**. An event at the node nearest
its position spreads over the graph, keeping the *largest* remaining loudness per node:

```
remaining(n) = L - sum(loss(e)) along the loudest path
loss(e)      = alpha * length(e) + (door closed or locked ? beta : 0)
audible at n iff remaining(n) >= theta
```

This is a Dijkstra variant (max-first). Because loss is additive per edge, it finds the loudest
path exactly. Locked doors muffle sound like closed ones; they only block *walking*.

Limitation (stated up front): sound travels only along graph edges, not through walls.

## Belief

`b[n]` is a distribution over nodes (sums to 1).

1. **Hear event** at the Hunter's node with `heard = level(hunterNode)` from the sound model.
   Muffling is a proxy for localisation error: `sigma = sigma0 + kappa * (L - heard)`.
   Likelihood for node n: `exp(-d(n, source)^2 / (2 sigma^2))` (d = walking distance), normalised to sum to 1.
   The evidence is **blended in**, not multiplied: `b <- (1 - w) * b + w * likelihood`, with
   `w = heard / (heard + w0)`. Footsteps repeat many times a second; multiplying each repeat would pin the
   belief to one node after a few steps. Blending converges to the likelihood itself (tested), so repeated
   noise can never make the Hunter certain.
2. **See player** at node s: `b` collapses to s. **Being shot** is strong evidence about the shooter: the same
   blend with the shooter's likelihood (`sigma0`) and weight `shotWeight`.
3. **Time**: `b[n] <- (1 - delta*dt) * b[n] + delta*dt / N`, so confidence in an old trail fades.
4. **Search a node** (reach it, no player): `b[n] *= searchedFactor`, renormalise.

## Target choice

```
U(n) = b[n] / (pathCost(hunter, n) + epsilon)       target = argmax U
```

Patrol is not a separate algorithm: with a flat belief, searched nodes stay low and stale nodes
recover, so the Hunter sweeps the level by itself.

## Decision layer (HunterBrain)

Pure C#. Each tick the Unity side reports what the Hunter's sensors saw; the brain returns a command
(state, gait, target node, hold position, attack result). Noise and being shot arrive as events.

| State | When | Behaviour |
|---|---|---|
| Attack | Player visible and within attack range | Stand still, face the player, wind up, shoot |
| Chase | Player visible but out of range, or seen less than `chaseMemory` ago | Walk to the player's node at chase speed |
| Investigate | Belief peak is at least `investigateEnterRatio` times the flat level (falls back below `investigateExitRatio`) | Walk to the likeliest place at investigate speed |
| Patrol | Belief is flat | Walk to the best-utility node at patrol speed |
| Dead | No health | Stop |

The state follows from the belief, not from a script. Investigate and Patrol use hysteresis (enter at a higher
peak than exit) so the Hunter cannot flap at the boundary. The target is re-evaluated at most every
`reselectInterval` unless something new was perceived, and a new target must beat the current one's utility by
`switchMargin` before the Hunter switches.

Patrol is not a separate algorithm. Entering a node without seeing the player lowers that node's probability;
time raises every node back toward flat. With a flat belief the target rule picks the nearest node not
searched recently, so the Hunter sweeps the whole level (tested: all reachable nodes visited, no scripted route).

Being shot is evidence about the shooter. A noise belief is spread over walking distance with every door treated
as open, because sound passes a locked door and the player may be anywhere on its far side.

## Blocked-door memory

The door scripts report no lasting "blocked by a barricade" signal. When the Hunter orders a door open and it
stays shut, the Unity side calls `BlockedDoorMemory.MarkBlocked(doorId)`. For the next 20 s that door reports
Blocked (impassable) to the planner, which routes around it; afterwards the real door is trusted again, because a
barricade can be dragged away. This is the model-based part in the strict sense: a belief about the world that the
sensors do not report directly, which expires.

## Measured: how a noise moves the belief

Real 45-node level, Hunter on a central tile, all doors open. "Peak ratio" = highest node probability divided by the
flat level (1 / 45). Produced by the exploration used to choose the Investigate thresholds.

| Noise | Source distance | Heard level | Peak ratio |
|---|---|---|---|
| Gunshot (30) | 0 m | 30 | 11.9 |
| Gunshot (30) | 8 m | 22 | 4.25 |
| Gunshot (30) | 16 m | 14 | 2.5 |
| Gunshot (30) | 24 m | 6 | 1.9 |
| Gunshot (30) | 32 m | inaudible | 1.0 |
| Thrown prop (12) | 8 m | 4 | 2.8 |
| Thrown prop (8) / door opening (6) | 8 m | inaudible | 1.0 |
| 20 sprint footsteps (8) in a row, 4 m away | 4 m | 4 | 9.4 (never certainty) |

After a gunshot 8 m away the peak decays from 4.25 to 2.6 after 10 s, 1.8 after 20 s, and 1.4 after 30 s.

## Unity adapters (Assets/Scripts/AI/Hunter/Runtime)

Plain scripts, no asmdef, so they can use the GV types (`DoorController`, `InteractionNoise`, `PlayerHealth`).
`HunterAgent` is the only class that talks to both worlds:

| Class | Job |
|---|---|
| `HunterAgent` | Subscribes to `InteractionNoise`, reads line of sight, asks `HunterBrain.Think`, plans with `AStar`, drives the mover and the shooter, opens doors, detects jammed doors |
| `HunterMover` | Walks the body along the waypoints A* produced. It never chooses a route. Same shape as the shared follower the group agreed (SetPath, Stop, FaceTarget, HasArrived, PathEnded) |
| `HunterShooter` | Casts the hitscan ray and applies damage through `PlayerHealth.TakeDamage`. A shot the model rolled as a miss is aimed visibly to the side |
| `HunterHealth` | 125 health, implements `IDamageable`, so the player's weapon hurts it with no extra code |
| `DoorSceneStates` | Reads each door's live state from the `DoorController` called `Door_*`; a graph door with no `DoorController` counts as open |

Line of sight is a field of view plus a ray that must reach the player's collider before any wall or closed door.
The brain only ever sees the player through this: it never reads the player's real position.
Door waypoints sit on the middle of a door edge. The body waits one metre before a door waypoint until the door
is 60% open; a door that stays shut for 2 s (or reports blocked) is written into `BlockedDoorMemory`.

## Ranged hitscan attack

Hitscan = the shot lands instantly along a ray (the same mechanism as the player's weapon), no projectile.
`AttackModel` (Core, pure C#) decides; the Unity side casts the ray and applies the damage.

```
Idle      : line of sight, in range, not on cooldown  ->  start wind-up
WindingUp : sight or range lost -> cancel (no shot).  Wind-up over -> fire, roll to hit
Cooldown  : wait, then Idle
chance(d) = MaxAccuracy + (MinAccuracy - MaxAccuracy) * d / Range        (0 beyond Range)
```

The wind-up is the player's counterplay: breaking line of sight cancels the shot. The Hunter stands still
while winding up. No melee (decided).

## Planning: A* with dynamic costs

```
cost(e) = length(e) + (closed ? C_closed : 0)        infinity if locked or blocked
h(n)    = Euclidean distance(n, goal)
```

`h` is admissible because every edge cost is at least the Euclidean distance between its ends.
A test compares A* with Dijkstra (same optimal cost; nodes expanded are recorded).
Replans happen on events (door changed on the current path, target changed), never every frame.

## Edge cases handled by design

| Case | Handling |
|---|---|
| Hunter hears its own door-opening noise | Ignore noise whose source is the Hunter's own hierarchy (`DoorController.Open` passes the opener as the source) |
| A noise caused by another robot | Ignore noise whose source is an `IDamageable` that is not the player |
| Barricade behind a door (no API for it) | Door commanded open but still Closed after `tBlock`: mark edge blocked for `tForget` |
| Facility AI locks a door on the current path | Event triggers replan; locked = infinite cost |
| No path to best target | Take next-best target by U; if none, fall back to Patrol |
| Noise spam from one spot | Optional stretch: habituation factor (see WEIGHTS) |

## Interface to the Agent Controller (proposal, not yet agreed)

`SetPath(IReadOnlyList<Vector3> waypoints, MoveSpeed speed)`, `Stop()`,
`FaceTarget(Transform t)`, `bool HasArrived`, event `PathEnded`. Door midpoints are included as waypoints.
The Hunter ships a minimal `SimplePathFollower` so it runs without the controller.

## Test plan

Graph adapter, MinHeap, A* vs Dijkstra, dynamic costs, sound reach (open and through doors),
belief update (normalisation, collapse on sighting, decay), target selection, self-noise filter,
blocked-door learning, no oscillation between two equal targets.

## Measured results

Recorded by `AStarTests.AStar_FindsTheSameCostAsDijkstra_WhileExpandingFewerNodes` on the real 45-node level graph,
all doors open, every start/goal pair (2,025 searches):

| Search | Total nodes expanded | Average per search (of 45) |
|---|---|---|
| Dijkstra (heuristic off) | 46,575 | 23.0 |
| A* (Euclidean heuristic) | 28,025 | 13.8 |

Both return the same cost on every pair. A* expands about 40% fewer nodes. At 45 nodes this is a small
absolute saving; the point is that the heuristic is provably safe (every edge cost is at least the straight line,
tested on the real graph) and the saving grows with graph size.
