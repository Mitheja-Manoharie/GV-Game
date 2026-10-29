# Facility AI ("Lockdown Director")

**Owner:** Mitheja Manoharie (IT24103443) · SE3062 Intelligent Systems

The facility itself is the opponent. It decides **how much** pressure to put on the player
(utility) and **where** to apply it fairly (constraint satisfaction): which spawn points to
use and which doors to lock.

## PEAS

| | |
|---|---|
| **Performance** | Keep player tension in a target range; the game must always stay fair and winnable |
| **Environment** | Level graph (rooms, corridors, doors), player state, enemies alive |
| **Actuators** | Spawn enemies at spawn points; lock and unlock doors |
| **Sensors** | Player node, health, recent damage, calm time, progress to exit, door states, enemy count, line of sight, occupied spawn points, blocked doorways |

## Architecture

1. **Utility layer** – an intensity score from the player's state chooses how many enemies to
   spawn (k), whether to lock doors, and when to decide next.
2. **CSP layer** – backtracking search (MRV + forward checking, utility value ordering) assigns
   spawn points and door states under hard constraints:
   - not visible to the player
   - path distance within [d_min, d_max]
   - one enemy per spawn point, and only on free spawn points
   - never lock a door with something in the doorway
   - the exit stays reachable with the chosen doors locked
   - every spawn can reach the player
   - never lock the player in, at most L doors locked

## Code structure

| Assembly | Contents | Unity dependency |
|---|---|---|
| `LabLockdown.AI.Facility.Core` | `LevelGraph`, `LevelGraphBuilder`, `LabLockdownLayout`, `Csp/` solver, `Director/` (intensity model, facility CSP, director, `Simulation/` pacing simulator) | **None** (`noEngineReferences`) |
| `LabLockdown.AI.Facility.Runtime` | `LevelGraphProvider`, `SpawnPointMarker`, `FacilityDirectorRunner`, `FacilityEvents`, service interfaces and placeholders | Yes |
| Assembly-CSharp (`AI/FacilityIntegration`) | `DoorControllerDoorService` (adapter to the Systems Engineer's `DoorController`), `RobotSpawnService`, `TestRobot` | Yes |
| `LabLockdown.AI.Facility.Tests` | EditMode unit and pacing tests for the core | Test runner only |

The decision logic ("brain") cannot reference Unity, which enforces the separation of AI logic
from visuals and lets every algorithm be unit tested.

## Level graph

- **45 nodes** (one per 4 m floor tile), **55 edges**, **5 door edges**.
- Tiles one grid step apart are connected; on this layout every such pair has an opening or a door.
- **BFS** (`IsReachable`) answers yes/no reachability for the route constraints.
- **Dijkstra** (`ShortestDistances`, O(V²) array version) gives real walking distance for the
  distance-window constraint. With 45 nodes this is a few microseconds and needs no priority queue.
- **`ShortestPath`** returns the route itself (Dijkstra with predecessors), and
  `LevelGraphProvider.FindPath(from, to, lockedDoors)` turns it into world-space waypoints for robot
  movement. Both use the same `IsPassable` rule as the CSP, so robots and the Director always agree on
  which routes are open.

**Why a tile graph rather than Unity's NavMesh:** the Facility AI reasons about rooms and doors,
not exact footsteps. Constraint checks run graph searches many times per decision, so a small graph
keeps each decision cheap, and pure C# makes it testable outside Unity. The `LevelGraphProvider`
validates the graph against the scene (door markers, spawn markers, NavMesh coverage) on Start.

**Verified facts (unit tests):**

| Scenario | Result |
|---|---|
| Shortest route, player start → exit | 60 m (via the research lab) |
| Lock `Door_InfirmaryNorth` | still 60 m |
| Lock `Door_LabSouth` | 92 m (forced through the infirmary) |
| Lock both route doors, or both control room doors | exit unreachable → must be rejected by the CSP |
| Lock `Door_ArmouryNorth` | player confined to 9 tiles (the safe area) |

## Generic CSP solver

`Core/Csp/` holds a problem-independent solver, so the Facility constraints (see *The Director*) are just data.

| Part | Implementation | Lecture 11 term |
|---|---|---|
| `CspProblem` | variables with integer domains; unary, binary and global (n-ary) constraints | X, D, C |
| Node consistency | unary constraints (e.g. "spawn not visible") prune domains once, before search | — |
| `BacktrackingSolver` | recursive backtracking, undo through a trail of pruned values | Algorithm 1 |
| Variable ordering | **MRV** (fewest legal values), ties broken by the **degree heuristic** | fail-first / most constraining variable |
| Value ordering | domain order, **LCV**, or a custom `ValueScore` (the Director's utility) | succeed-first |
| Inference | **forward checking** on every constraint left with one unassigned variable | forward checking |
| Safety | `MaxNodes` budget → `NodeLimitReached`, so a decision can never stall a frame | — |

**Design decisions**

- *Integer values for every variable* – one problem can mix spawn slots (value = graph node) and
  doors (0 = open, 1 = locked).
- *All-different as pairwise ≠* – lets forward checking prune it like any binary constraint.
- *Forward checking rather than AC-3/MAC* – the Facility problem is small (≤ 4 spawn slots + 5 doors)
  and its most expensive constraint (exit reachability) is global, which arc consistency cannot use
  directly. Forward checking catches dead ends one step ahead at a fraction of the cost.
- *Domains as "alive" flags, not lists* – pruning and restoring never reorders values, so value
  ordering stays deterministic and undo is O(1) per value.
- *MRV counts legal values* – with forward checking the domain size is already the legal count;
  without it the solver checks each value against the assignment (otherwise MRV would never change
  anything, because domains only shrink through inference).

**Measured effect (N-Queens, first solution, nodes = values tried)**

| Variant | 8-Queens | 12-Queens | 16-Queens |
|---|---|---|---|
| Plain backtracking | 876 | 3,066 | 160,712 |
| MRV + degree only | 572 | 1,770 | 584 |
| Forward checking only | 88 | 193 | 7,560 |
| **MRV + FC** | **75** | **153** | **44** |
| MRV + FC + LCV | 39 | 32 | 332 |

- MRV + FC reduces 16-Queens from ~161k nodes to 44 (≈ 3,600× fewer); about 0.1 ms vs 26 ms in .NET 8.
- LCV is a heuristic, not a guarantee: best on 8 and 12, worse on 16. The Facility AI replaces it with
  a utility score, which ranks values by how good they are for gameplay rather than how few options
  they remove.
- Australia map colouring (lecture example): MRV + FC solves it with 7 nodes, 0 backtracks, and the
  degree heuristic picks SA first.

**Tests (17):** lecture examples (Australia, MRV slide, LCV slide), unsolvable problems (K4 with 3
colours, impossible global sum), node consistency, forward checking detecting a dead end early, the node
budget, and **200 random problems checked against brute force for every combination of options**.

## The Director

### Decision loop

`FacilityDirectorRunner` (Unity) senses → `FacilityDirector` (pure C#) decides → the runner acts through
`ISpawnService` / `IDoorService`. Decisions happen every 4–8 s (faster when intensity is high), never per frame.

1. **Sense** – player node, health, recent damage (5 s window), calm time, enemies alive, line of sight
   to each spawn point (one `Physics.Linecast` per candidate), current door states.
2. **Utility: how much** – `IntensityModel`:
   `intensity = clamp01(0.15 + 0.35·health + 0.25·progress + 0.30·calm − 0.50·damage − 0.40·enemies/max)`.
   It sets the phase, the spawn budget k, whether doors may lock (≥ 0.55) and how many (1 in build-up,
   2 at a peak). Only four doors are lockable (`LockableDoors`): `Door_InfirmaryNorth`, `Door_LabSouth`,
   `Door_ControlWest` and `Door_ControlEast`. `Door_ArmouryNorth` is never locked.
3. **CSP: where** – `FacilityProblem` builds the constraints below; `BacktrackingSolver.SolveBest`
   (branch and bound) returns the highest-utility valid configuration.
4. **Degrade** – if k spawns cannot be placed fairly, try k−1 … 0; if nothing is valid, open every door.
5. **Remember** – door change and lock times, the last 4 spawn points, a relief window after a peak.

### Phases

| Phase | When | Effect |
|---|---|---|
| Relief | health < 30 %, player in a safe area, or 15 s after a peak | no spawns; doors reopen (immediately if the player is hurt) |
| Build-up | intensity < 0.8 | small waves (wave size grows with intensity), at most 1 locked door |
| Peak | intensity ≥ 0.8 | full wave, up to 2 locked doors, then a relief window |

### Constraints

| Kind | Constraint |
|---|---|
| unary | spawn hidden from the player (line of sight) |
| unary | spawn point free: nothing already standing on it |
| unary | spawn not in the player's area |
| unary | spawn 12–40 m walking distance from the player |
| unary | door open if the player is at it / something is in the doorway / it has been locked ≥ 25 s / the player is hurt |
| unary | door keeps its state for 10 s after changing (cooldown) |
| unary | door open when locking is not allowed |
| binary | Spawn_i < Spawn_i+1 (all-different plus symmetry breaking) |
| global | exit reachable from the player with the chosen doors locked |
| global | at most L doors locked |
| global | every spawn can reach the player with the chosen doors locked |

**Symmetry breaking:** spawn slots are interchangeable, so {A, B} and {B, A} are the same decision.
Ordering the slots removes k! duplicate branches.

### Utility (value ordering + branch-and-bound objective)

- **Spawn:** walking distance near the middle of the window; **flank**: at high intensity prefer spawns
  *behind* the player (further from the exit), at low intensity *ahead* (fairer, head-on); **variety**:
  avoid recent spawn points; **spread**: distance to the other chosen spawns.
- **Door:** Locked scores `intensity × detour`, where detour is how much longer the player's route to the
  exit becomes. A door that does not change the route is never worth locking.
- **Bound:** assigned variables score their current value; unassigned ones their best possible value.
  Spread can only fall as more spawns are placed, so the bound never underestimates.

### Integration

- `DoorControllerDoorService` drives the Systems Engineer's `DoorController` (`Close()`, `SetLocked()`)
  and adds a carving `NavMeshObstacle` while locked, because doors are excluded from the NavMesh bake.
  It sits in Assembly-CSharp because the Facility assemblies must not depend on teammates' scripts.
- `PlaceholderSpawnService` (red capsules that disappear after 20 s) and `PlaceholderDoorService`
  (solid red barrier) are fallbacks: the runner adds them itself when no spawn or door service is assigned.
- `PlayerHealth` implements `IPlayerStatusSource` and is assigned to the runner in `Main.unity`. The
  Debug Health slider is only used when no status source is assigned.

### Testing the Director

The key test plays 25 random games of 20 decisions (random positions, health, damage,
visibility, enemy counts) and checks after every decision that the exit is reachable, no locked door is
next to the player, at most 2 doors are locked, and every spawn is hidden, outside the safe areas and
the player's area, within 12–40 m and able to reach the player – while still spawning and locking regularly.

**Scripted playthrough** (start → armoury → service corridor → lab → control room):

| t (s) | Player | Phase | Intensity | Spawned | Locked |
|---|---|---|---|---|---|
| 8 | quarantine cell | Relief (safe area) | 0.62 | 0 | – |
| 22 | service corridor | Build-up | 0.77 | 3 | LabSouth |
| 34 | crossroads, hurt | Build-up | 0.21 | 0 | – |
| 48 | lab corridor | Build-up | 0.66 | 2 | ControlEast |
| 60 | research lab, 25 % health | Relief (health) | 0.14 | 0 | – |
| 84 | control room | Peak | 0.86 | 3 | – |

The simulation caught two pacing problems that every test passed: robots spawning while the player was
still choosing gear, and a 40 % health player receiving a full wave. Hence the safe-area rule and wave
sizes that grow with intensity. Decisions take about 1 ms (.NET 8): 4–120 search nodes, always proven optimal.

## Edge cases, tuning, integration

### Edge cases

| Situation | Risk | Handling |
|---|---|---|
| Something already on a spawn point (a robot that has not moved, a dragged canister) | robots spawn inside each other or inside props | The runner checks a 0.75 m sphere at each marker for enemies (`NavMeshAgent`), the player (`CharacterController`) and physics props (`Rigidbody`). New unary constraint: *spawn on a free spawn point*. |
| Something in an open doorway | locking shuts the door on a robot or prop; a robot gets stuck in the carving obstacle | The runner checks a box covering the doorway (4 × 2 × 1 m, in the door's frame). New door rule: *must be open: something is in the doorway*. Only open doors are checked, so a robot waiting at a locked door does not unlock it. |
| Player off the level graph (falling, out of bounds, more than 3 m from any tile centre or below y = −2) | decisions made for the wrong room | The runner skips the decision, shows *Waiting: …* in the overlay and retries every second. The Director itself rejects invalid nodes. |
| Time spent choosing gear counted as calm | the first fight after the armoury was always a peak | Calm time only counts from when the player leaves a safe area. |
| Every spawn point blocked | – | Zero spawns: still a valid decision, not the fallback. |
| Decision cost | frame hitches | Timed with a stopwatch and shown in the overlay. .NET 8, 2,000 random states: median 0.08 ms, 95th percentile 0.37 ms, max 5.3 ms. |

### Pacing simulator

`PacingSimulator.Run(level, settings, player, seed)` plays a whole run against the **real Director** on the
real level graph, in 0.5 s steps:

- **The simulated player:**
  - walks the shortest open route at 2.5 m/s and stops to fight while robots are engaging;
  - loses health for each engaging robot and defeats robots at a skill-dependent rate;
  - regenerates after 6 s without damage, and uses the three medkits.
- **Robots** reach the player at 3 m/s along the graph.
- **Three player profiles:**

  | Profile | Robots defeated per second | Health lost per robot per second |
  |---|---|---|
  | Weak | 0.15 | 1.8 % |
  | Average | 0.25 | 1.2 % |
  | Strong | 0.40 | 0.8 % |

  Each run varies these values by ±20 %.
- Results are deterministic for a given seed.

It models pacing, not combat. Its numbers are for comparing settings with each other, not for predicting how
real players will do.

### Experiment: adaptive vs fixed difficulty

50 runs per cell. *Fixed pressure* uses a constant intensity with no relief.

| Director | Weak: finished / lowest health / robots per min | Average | Strong |
|---|---|---|---|
| **Adaptive (default)** | **98 % / 0.14 / 4.9** | **100 % / 0.57 / 8.7** | **100 % / 0.84 / 9.5** |
| Adaptive, no relief | 34 % / 0.03 / 6.1 | 100 % / 0.54 / 9.0 | 100 % / 0.80 / 9.9 |
| Fixed 0.70 | 0 % / 0.00 / 8.9 | 0 % / 0.00 / 11.4 | 0 % / 0.00 / 13.8 |
| Fixed 0.50 | 0 % / 0.00 / 7.6 | 100 % / 0.65 / 9.6 | 100 % / 0.90 / 9.2 |
| Fixed 0.35 | 100 % / 0.49 / 5.7 | 100 % / 0.88 / 6.2 | 100 % / 0.96 / 7.1 |

- **No single fixed level suits every player.**
  - Fixed 0.50 kills every weak player.
  - Fixed 0.35 barely touches a strong player: their lowest health is 96 %.
- **The adaptive Director:**
  - gives the weak player about half the robots it gives the strong player (4.9 vs 9.5 per minute);
  - still pushes the strong player (lowest health 84 %).
- **Relief is the mechanism that matters most.** Without it, weak-player survival drops from 98 % to 34 %.
- **Fairness held in every run:**
  - the exit stayed reachable in all 750 adaptive runs;
  - the fallback was never needed in 1,445 decisions;
  - every decision was proven optimal (median 9 search nodes, max 88).

![Pacing for a weak, average and strong player](FacilityAI_pacing.png)

### Tuning

Fixing calm time removed every peak: intensity no longer reached 0.8, so runs had no climax.

Candidate settings, 50 runs each:

| Change | Weak survival | Peaks per run | Locks per average run |
|---|---|---|---|
| none | 100 % | 0 | 1.1 |
| peak threshold 0.80 → 0.75 | 98 % | 1.0 | 2.0 |
| **calm saturation 20 s → 12 s** | **98 %** | **1.0** | **2.0** |
| progress weight 0.25 → 0.35 | 94 % | 1.0 | 2.8 |
| progress 0.35 + calm 12 s | 88 % | 1.0 | 2.6 |

**Chosen: calm saturation 12 s.** It gives the same result as lowering the peak threshold, but it changes
*what triggers* a peak (a healthy player who has been calm for a while) instead of making peaks easier to
reach for everyone. Raising the progress weight cost weak players too much.

### Debug overlay

- **Live intensity graph** covering the last 60 s:
  - intensity in blue and health in orange;
  - build-up and peak bands;
  - lock and peak threshold lines.

  It is sampled every 0.5 s with `FacilityDirector.PreviewIntensity`, the same calculation a decision uses,
  but without changing the Director's memory.
- **Text panel additions:**
  - why the Director is waiting;
  - blocked spawn points and doorways;
  - decision time (last and max).
- **Gizmos:** magenta marks occupied spawn points and blocked doorways.

### Integration for teammates

`FacilityEvents` (namespace `LabLockdown.AI.Facility.Runtime`) announces what the Facility AI does.
Teammates' scripts in Assembly-CSharp can subscribe without referencing the runner. A robot that moves
on the level graph can route around locked doors with
`LevelGraphProvider.FindPath(from, to, FacilityEvents.LockedDoors)`.

| Event / property | Typical use |
|---|---|
| `DoorLockChanged(doorId, locked)` | robots re-plan their path (`NavMeshAgent.ResetPath` / `SetDestination`); the `DoorAccessPanel` beside each lockable door turns red |
| `WaveSpawned(positions, phase)` | alarm sound, warning UI |
| `PhaseChanged(previous, current)` | music (`FacilityAudio`) and the rotating red `AlarmBeacon` lights at Peak |
| `DecisionMade(decision)` | logging, analytics |
| `Phase`, `Intensity`, `IsDoorLocked(id)`, `LockedDoors` | polling instead of events |

```csharp
using LabLockdown.AI.Facility.Runtime;

void OnEnable()  => FacilityEvents.DoorLockChanged += OnDoorLockChanged;
void OnDisable() => FacilityEvents.DoorLockChanged -= OnDoorLockChanged;

void OnDoorLockChanged(string doorId, bool locked)
{
    if (locked) agent.SetDestination(target.position); // re-plan around the locked door
}
```

**Behaviour:**

- Events are raised after the decision has been applied to the scene.
- A subscriber that throws does not stop the others.
- Subscribers are cleared when Play mode starts.

**What teammates need to provide:**

- Robots need a non-trigger collider, so spawn points and doorways can detect them.
- Props that can block a doorway need a `Rigidbody`.
- When the player respawns, call `FacilityDirectorRunner.ResetDirector()`. `RobotSpawnService` does this
  automatically through `PlayerHealth.Respawned`.

### Robot spawner

`RobotSpawnService` (in `AI/FacilityIntegration`, next to the door adapter) is the real `ISpawnService`.
The Director decides when, where and how many; the spawner decides which prefab and keeps count.

| Responsibility | How |
|---|---|
| Which robot | `Build Up Robots` list during build-up, `Peak Robots` list at a peak (e.g. Hunter, then Flanker). Each list is used in turn. |
| Placement | Snaps the spawn marker to the NavMesh and warps any `NavMeshAgent` there. |
| Alive count | A robot stops counting as soon as its `IDamageable.IsAlive` is false, or it is destroyed or deactivated. |
| Player respawn | Removes all robots and resets the Director (`PlayerHealth.Respawned`). |
| Robot set-up (optional) | Robot scripts implementing `IFacilitySpawned` are told their spawn point and phase. |

With no prefabs assigned it spawns `TestRobot` capsules (test only): they walk to the player at 3 m/s,
deal 1.2 health per second within 2 m and have 100 health, matching the pacing simulator, so the full
loop can be checked in Play mode. Both robot lists are currently empty in `Main.unity`, so the scene spawns
test robots until the robot prefabs are assigned.

### Verification

**82 EditMode tests** in `LabLockdown.AI.Facility.Tests`:

| File | Tests | Covers |
|---|---|---|
| `LevelGraphTests` | 18 | graph building, BFS/Dijkstra, the layout's routes and door scenarios |
| `ShortestPathTests` | 5 | `ShortestPath` routes, locked doors, and agreement with Dijkstra for every pair of nodes |
| `BacktrackingSolverTests` | 17 | the generic CSP solver (see above) |
| `BranchAndBoundTests` | 3 | `SolveBest` optimality, pruning and the node budget |
| `IntensityModelTests` | 9 | phases, wave size and decision interval |
| `FacilityDirectorTests` | 14 | constraints, door rules, relief, variety, flanking, random games |
| `FacilityEdgeCaseTests` | 8 | see below |
| `PacingTests` | 8 | see below |

**Edge-case tests (8):**

- occupied spawn points;
- obstructed doors;
- calm time after leaving the safe area (two tests);
- `PreviewIntensity`;
- an off-graph player;
- null percept collections;
- every spawn point blocked.

**Random-games test:** it now also randomises occupied spawn points and obstructed doors.

**Pacing tests (8):** 20 full simulated runs per player type check that:

- the exit is always reachable and the enemy cap holds;
- average and strong players always finish, and weak players finish at least 90 % of runs;
- adaptive pacing beats fixed 0.5 for the weak player;
- the weak player meets fewer than 75 % of the strong player's robots per minute;
- every average-player run has a climax, followed by a relief window with no spawns;
- adaptive pacing challenges a strong player more than fixed 0.35;
- the simulation is deterministic.

The runtime code compiles against Unity API stubs. Behaviour in the scene still needs a check in Play mode.

## Keycard objective and fairness

The exit only opens once the player has the keycard, which appears at one of four random spots each run (see
World_Builder.md). The Director's "exit reachable" constraint stays sufficient because every spot was chosen before
any lockable door: the service corridor, the maintenance bay, the lab corridor (before `Door_LabSouth`) and the
infirmary (before `Door_InfirmaryNorth`), all reached from the start through `Door_ArmouryNorth`, which is never
locked. So locking can never cut the player off from the keycard, and from any spot the exit is reachable whenever
the constraint holds. Allowing spots behind lockable doors would need a second global constraint, "the keycard is
reachable while the player does not have it".

## Default spawn points

Eight candidates spread over both routes; none in the quarantine cell, armoury or control room:
MaintenanceBay, Crossroads, ServiceCorridor, LabCorridor, ResearchLab, Infirmary,
InfirmaryNorth, WestCorridor.
