# Tactical Flanker

**Owner:** Alen Oshan (IT24103473) · SE3062 Intelligent Systems

A robot that does not walk straight at the player. It plans a sequence of actions such as
*take cover → flank → attack*, using goal-oriented action planning (STRIPS-style, A* over actions).
If the player holds one route, the flanker re-plans via the other one, which the level's two routes
to the control room make possible.

## PEAS

| | |
|---|---|
| **Performance** | Attack the player from outside their view whenever a hidden route exists; avoid head-on fights; stay alive (retreat to cover at low health) |
| **Environment** | Level graph (tiles, two routes, doors), player position and facing, locked doors |
| **Actuators** | Move along a tile route (take cover, flank, assault); attack |
| **Sensors** | Own tile and health; player tile, position and facing; which tiles the player has line of sight to; locked doors |

## Architecture

Three layers, each built on the one below:

1. **Generic STRIPS planner** (`Core/Goap/`): world states, actions with preconditions and effects, and A*
   search over actions. It knows nothing about the game and is tested on the lecture's Blocks World.
2. **Tactical layer**: `ThreatMap` (which tiles the player sees, and which ones it is *looking at*) and
   `RouteField` (route costs over the level graph that account for the player's view).
3. **Flanker domain** (`FlankerPlanner`): turns percepts into facts and grounded actions, picks a goal, and
   calls the planner.

| Assembly | Contents | Unity dependency |
|---|---|---|
| `LabLockdown.AI.Flanker.Core` | `Goap/` planner, `ThreatMap`, `RouteField`, `FlankerPlanner` | **None** (`noEngineReferences`) |
| `LabLockdown.AI.Flanker.Tests` | EditMode unit tests | Test runner only |

The core reuses the Facility AI's `LevelGraph` (`LabLockdown.AI.Facility.Core`), so the flanker, the Director
and the robots all agree on the level, the routes and which doors are locked. It does not depend on any
teammate's Unity scripts.

## Layer 1: the STRIPS planner

| Part | Implementation | Lecture term |
|---|---|---|
| `WorldState` | the set of true facts; one bit per fact in a 64-bit mask | state (closed-world assumption) |
| `PlanAction` | `RequireTrue`, `RequireFalse`, `Add`, `Delete`, `Cost` | operator: preconditions, add list, delete list |
| `Goal` | facts that must be true and facts that must be false | goal (a set of states) |
| `GoapPlanner.Plan` | forward A* through states | state-space search |

**Heuristic:** h(s) = ⌈u / k⌉ · c_min, where *u* is the number of goal facts not yet satisfied, *k* the most
goal facts one action can satisfy, and *c_min* the cheapest action cost.
- **Admissible:** one action fixes at most *k* of the *u* facts, so at least ⌈u / k⌉ more actions are needed,
  each costing at least *c_min*.
- **Consistent:** one action changes ⌈u / k⌉ by at most 1, so h falls by at most *c_min* ≤ the action's cost.
  A* therefore returns the cheapest plan without re-opening states, which is what the closed set relies on.

**Design decisions**

- *Bitmask states.* Comparing, hashing and copying a state are single integer operations, and applying an
  action is `(state & ~Delete) | Add`. 64 facts is far more than the flanker needs (6).
- *Grounded actions.* Each planning call creates concrete actions ("flank to tile 31") from the current percepts,
  so the planner never handles variables. The move costs come from real routes (layer 2), not fixed numbers.
- *Negative preconditions.* "Flank only while not exposed" is `RequireFalse = Exposed`, more natural than
  inventing a `NotExposed` fact.
- *Deterministic tie-breaking.* Equal f prefers the deeper node, then first-in, so the same situation always
  produces the same plan (reproducible tests and demo).
- *Node budget.* `MaxNodes` stops the search with `NodeLimitReached` so planning can never stall a frame.
- *Own binary heap.* Unity's .NET Standard 2.1 profile has no `PriorityQueue`.

**Measured effect (Blocks World, unit costs; states expanded)**

| Problem | Actions | Optimal plan | Uniform-cost | **A\*** |
|---|---|---|---|---|
| Sussman anomaly (3 blocks) | 18 | 6 steps | 21 | **13** |
| Reverse a tower of 4 | 32 | 8 steps | 47 | **9** |
| Reverse a tower of 5 | 50 | 10 steps | 184 | **15** |

The heuristic matters more as problems grow: 12× fewer states on the 5-block tower, about 0.09 ms in .NET.
The Sussman anomaly is the lecture's example of why sub-goals cannot be solved one at a time; the planner finds
the optimal 6-step plan *Unstack(C,A) → PutDown(C) → PickUp(B) → Stack(B,C) → PickUp(A) → Stack(A,B)*.

## Layer 2: what the player is holding

### Threat map

For every tile:

- **visible:** the player has line of sight to it (one raycast from the player's eyes per tile in Unity);
- **watched:** visible *and* inside the player's view cone. The player is "holding" that tile.

| Tile is… | Meaning for the flanker |
|---|---|
| not visible | **cover** |
| visible, watched, within 12 m | head-on attack position |
| visible, *not* watched, within 12 m | **flank position**: it can attack while the player looks elsewhere |

The cone's half-angle is **55°**, taken from the player camera: a 75° vertical field of view is about 108°
horizontally at 16:9. Facing unknown → every visible tile counts as watched (the cautious assumption).

### Route field

Dijkstra over the level graph from the flanker's tile, where entering a watched tile costs
(1 + exposure penalty) × its length. One search prices every destination, so all move actions are costed
with one pass. Locked doors are impassable, using the same `IsPassable` as the Director and the robots.

- **Take cover / assault:** exposure penalty 2 (a metre in the player's view costs 3 m).
- **Flank:** penalty ∞, so watched tiles are walls. A flank is a *hidden* route by definition. If the player
  holds one route, that route does not exist for flanking, and the flank goes round by the other one.

**Why the tile graph rather than the NavMesh:** the decision is about rooms, routes and what the player can see,
not exact footsteps. The 45-tile graph keeps a full decision to microseconds, is testable outside Unity, and is the
same graph the Director uses. The Agent Controller's movement turns the tile route into smooth NavMesh motion.

## Layer 3: the flanker domain

### Facts

| Fact | True when |
|---|---|
| `InCover` | the flanker's tile is not visible to the player |
| `Exposed` | the flanker's tile is watched |
| `AtAttackPosition` | visible to the player and within 12 m |
| `OnFlank` | at an attack position that is not watched |
| `PlayerEngaged` | goal: attacking the player |
| `LowHealth` | health ≤ 30 % (sensed; no action changes it) |

### Actions

| Action | Requires | Adds | Deletes | Cost |
|---|---|---|---|---|
| `TakeCover` (nearest cover tile) | ¬InCover | InCover | Exposed, AtAttackPosition, OnFlank | route cost |
| `Flank` (nearest flank position, hidden route) | ¬Exposed, ¬AtAttackPosition, ¬LowHealth | AtAttackPosition, OnFlank | InCover | route length |
| `Assault` (nearest watched attack position) | ¬AtAttackPosition, ¬LowHealth | AtAttackPosition, Exposed | InCover, OnFlank | route cost |
| `AttackFromFlank` | OnFlank, ¬LowHealth | PlayerEngaged | – | 1 |
| `AttackHeadOn` | AtAttackPosition, ¬OnFlank, ¬LowHealth | PlayerEngaged | – | 1 + 30 |

*Why Flank requires ¬Exposed:* a flank only works if the player loses track of the flanker. An exposed flanker
therefore has to break line of sight first, which is where **TakeCover → Flank → AttackFromFlank** comes from.
The Flank route is priced from the cover tile in that case, because that is where it will start.

### Goals

| Goal | When | Goal facts |
|---|---|---|
| EngagePlayer | normally | PlayerEngaged |
| StayAlive | health ≤ 30 % | InCover |
| StayAlive (fallback) | no plan reaches the player (e.g. behind locked doors) | InCover, then hold until something changes |

### Example decisions (unit tests)

| Situation | Plan |
|---|---|
| Flanker at the crossroads; player in the control room **facing east** (holding the lab route) | Flank via maintenance bay → infirmary → west corridor → AttackFromFlank |
| Same, but the player **turns west** | old plan invalid → Flank via lab corridor → research lab → AttackFromFlank |
| Player in the maintenance bay facing east spots the flanker in the middle of the room | **TakeCover** (infirmary) → **Flank** (infirmary corridor, beside the player) → **AttackFromFlank** |
| Flanker already behind the player | AttackFromFlank |
| Infirmary door locked, player holding the lab route | Assault → AttackHeadOn (no hidden route left) |
| Health 20 % | TakeCover only |
| Player behind both locked control-room doors | hold in cover (fallback) |

### Re-planning

GOAP plans go stale. `IsStillValid` returns false, and the flanker plans again, when:

- the player moves to another tile;
- a door is locked or unlocked (`FacilityEvents.DoorLockChanged`);
- health crosses the low-health threshold;
- **the player now watches a tile on the planned take-cover or flank route** (the player turned to hold it).

The last rule is the "replans via the other route" behaviour.

## Tuning: head-on penalty

How much detour is worth avoiding a head-on fight? Sweep over **every flanker tile × player tile × 8 facings**
(15,840 situations, doors open):

| Head-on penalty | Flank (any) | Head-on | of which TakeCover → Flank |
|---|---|---|---|
| 10 | 71.6 % | 28.3 % | 0.1 % |
| 20 | 74.0 % | 26.0 % | 0.5 % |
| **30** | **78.0 %** | **22.0 %** | **1.8 %** |
| 40 | 80.2 % | 19.8 % | 2.5 % |

**Chosen: 30** (about 30 m, 12 s of walking). The flanker flanks in most situations and fights head-on only when no
hidden route is affordable. Higher values make it walk round half the level to avoid a short fight, which looks
indecisive. TakeCover → Flank is rare by design: it happens when the player spots the flanker mid-approach.

**Decision cost:** about 13 µs per decision in .NET (Release), at most 4 states expanded. The search is small
because a flanker has few actions. The value of A* shows on larger problems (Blocks World above).

## Verification

**31 tests**, all passing (run outside Unity against .NET Standard 2.1 / C# 9, the same profile Unity uses, together
with the Facility AI's 82 tests).

- **Planner (9):**
  - Sussman anomaly: optimal 6-step plan, A* and uniform-cost;
  - 4-block reverse tower: A* optimal and expands fewer states;
  - **400 random problems checked against brute force** (Bellman-Ford over all 256 states) for both searches;
    goals come from random walks, so most need several actions, and a quarter are random to cover "no plan";
  - edge cases: goal already true, no plan, negative preconditions and goals, node budget, invalid input.
  - Sanity check: making the heuristic overestimate makes the brute-force test fail, so the tests can catch it.
- **Routes (5):** with no threat, `RouteField` matches the Facility AI's Dijkstra for every pair of tiles (doors open
  and locked); the watched route is avoided; locked doors are never crossed.
- **Threat map (5):** view cone, turning around, player tile, unknown facing, attack range.
- **Flanker (12):**
  - the scenarios in the table above;
  - plan validity and re-planning triggers;
  - **every situation (15,840 decisions, random locked doors and health)** checking that low health never attacks,
    every engage plan ends in exactly one attack, moves chain up and never use a locked door, take-cover targets
    are cover, flank targets are flank positions reached without crossing a held tile, and a flank never starts
    exposed;
  - A* and uniform-cost always choose equally good plans.

In the tests, line of sight is a grid approximation (a straight line must stay on floor tiles; locked doors block it).
In the game it comes from raycasts, so the planner's behaviour in the scene still needs a check in Play mode.

## Runtime (Unity)

`FlankerAgent` (assembly `LabLockdown.AI.Flanker.Runtime`) is the Unity body around the planner:

| | Every 0.25 s (sense and decide) | Every frame (act) |
|---|---|---|
| | Player tile, position and facing; tiles the player can see (`PlayerVisibilitySensor`); locked doors (`FacilityEvents`); own health | Move step: hand the step's tile route to the robot's `IPathFollower` once |
| | Plan still valid? (`IsStillValid`) If not, `Decide` again | Attack step: stop, face the player, fire through `IRobotWeapon` when in range with line of fire |

- **Line of sight:** one ray per tile from the player's eyes (1.6 m) to 1.2 m above the tile centre, blocked by the
  Default and Doors layers, as in the Facility AI. Robots and the player never block it, so a flanker does not hide
  its own tile. One shared sensor for all flankers, refreshed every 0.2 s (45 rays).
- **Extra re-plan triggers:** a door locked or unlocked (`FacilityEvents.DoorLockChanged`, immediately), a move step
  finished, the follower stuck, or no line of fire for 2 s during an attack.
- **Off the level:** if the player is more than 3 m from every tile centre or below y = −2, the flanker waits.
- **Debug:** the overlay (top right) lists each flanker's plan, decision count and decision time; gizmos draw the
  planned routes (green flank, cyan cover, red assault) and, for the selected flanker, the tiles the player holds
  (red) and can see without holding (yellow).

The robot's body (movement, animation, health, gun, spawning) is shared with the Security Hunter and documented in
`Docs/AgentController.md`.

## Limitations

- Cover is tile-level (a tile the player cannot see), not individual cover props.
- Move costs are estimated for where the flanker will start each move; the plan is re-checked every sense tick
  and rebuilt when the situation changes, which keeps this accurate in practice.
- One flanker plans on its own; several flankers do not coordinate (e.g. one pins while another flanks).
- A player sweeping the view back and forth can make a flanker switch routes repeatedly. If Play mode shows this,
  the fix is hysteresis: only treat a route as newly held after it has been watched for about half a second.

## Plan

| Step | Work |
|---|---|
| 1 | STRIPS planner + A* + tests ✅ |
| 2 | Threat map, route field, flanker domain, tuning + tests ✅ |
| 3 | Unity runtime: sensing (raycasts, player facing), decision loop, debug overlay ✅ (compiles; Play-mode check pending) |
| 4 | Movement and animation along the planned routes (Agent Controller, SE3032) ✅ (compiles; Play-mode check pending) |
| 5 | Play-mode tuning in `Main.unity`, viva preparation |
