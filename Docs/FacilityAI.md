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
| **Sensors** | Player node, health, recent damage, kills, calm time, progress to exit, door states, enemy count |

## Architecture

1. **Utility layer** – an intensity score from the player's state chooses how many enemies to
   spawn (k), whether to lock doors, and when to decide next.
2. **CSP layer** – backtracking search (MRV + forward checking, utility value ordering) assigns
   spawn points and door states under hard constraints:
   - not visible to the player
   - path distance within [d_min, d_max]
   - one enemy per spawn point
   - the exit stays reachable with the chosen doors locked
   - every spawn can reach the player
   - never lock the player in, at most L doors locked

## Code structure

| Assembly | Contents | Unity dependency |
|---|---|---|
| `LabLockdown.AI.Facility.Core` | `LevelGraph`, `LevelGraphBuilder`, `LabLockdownLayout`, `Csp/` solver, `Director/` (intensity model, facility CSP, director) | **None** (`noEngineReferences`) |
| `LabLockdown.AI.Facility.Runtime` | `LevelGraphProvider`, `SpawnPointMarker`, `FacilityDirectorRunner`, service interfaces and placeholders | Yes |
| Assembly-CSharp (`AI/FacilityIntegration`) | `DoorControllerDoorService`: adapter to the Systems Engineer's `DoorController` | Yes |
| `LabLockdown.AI.Facility.Tests` | EditMode unit tests for the core | Test runner only |

The decision logic ("brain") cannot reference Unity, which enforces the separation of AI logic
from visuals and lets every algorithm be unit tested.

## Week 1: level graph

- **45 nodes** (one per 4 m floor tile), **55 edges**, **5 door edges**.
- Tiles one grid step apart are connected; on this layout every such pair has an opening or a door.
- **BFS** (`IsReachable`) answers yes/no reachability for the route constraints.
- **Dijkstra** (`ShortestDistances`, O(V²) array version) gives real walking distance for the
  distance-window constraint. With 45 nodes this is a few microseconds and needs no priority queue.

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

## Week 2: generic CSP solver

`Core/Csp/` holds a problem-independent solver, so the Facility constraints (Week 3) are just data.

| Part | Implementation | Lecture 11 term |
|---|---|---|
| `CspProblem` | variables with integer domains; unary, binary and global (n-ary) constraints | X, D, C |
| Node consistency | unary constraints (e.g. "spawn not visible") prune domains once, before search | — |
| `BacktrackingSolver` | recursive backtracking, undo through a trail of pruned values | Algorithm 1 |
| Variable ordering | **MRV** (fewest legal values), ties broken by the **degree heuristic** | fail-first / most constraining variable |
| Value ordering | domain order, **LCV**, or a custom `ValueScore` (Week 3: utility) | succeed-first |
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

## Week 3: the Director

### Decision loop

`FacilityDirectorRunner` (Unity) senses → `FacilityDirector` (pure C#) decides → the runner acts through
`ISpawnService` / `IDoorService`. Decisions happen every 4–8 s (faster when intensity is high), never per frame.

1. **Sense** – player node, health, recent damage (5 s window), calm time, enemies alive, line of sight
   to each spawn point (one `Physics.Linecast` per candidate), current door states.
2. **Utility: how much** – `IntensityModel`:
   `intensity = clamp01(0.15 + 0.35·health + 0.25·progress + 0.30·calm − 0.50·damage − 0.40·enemies/max)`.
   It sets the phase, the spawn budget k, whether doors may lock (≥ 0.55) and how many (1 in build-up,
   2 at a peak).
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
| unary | spawn not in the player's area |
| unary | spawn 12–40 m walking distance from the player |
| unary | door open if the player is at it / it has been locked ≥ 25 s / the player is hurt |
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
  (solid red barrier) let the Director run before robots and doors exist.
- `IPlayerStatusSource` will connect the Core Developer's health; until then a Debug Health slider is used.

### Verification

61 unit tests. The key one plays 25 random games of 20 decisions (random positions, health, damage,
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

## Default spawn points

Eight candidates spread over both routes; none in the quarantine cell, armoury or control room:
MaintenanceBay, Crossroads, ServiceCorridor, LabCorridor, ResearchLab, Infirmary,
InfirmaryNorth, WestCorridor.

## Plan

| Week | Work |
|---|---|
| 1 | Level graph, BFS/Dijkstra, gizmos, spawn markers, tests ✅ |
| 2 | Generic CSP solver (backtracking, MRV + degree, LCV, forward checking) + tests ✅ |
| 3 | Facility constraints, intensity model, branch and bound, director, spawn/door services ✅ |
| 4 | Edge cases, tuning, debug overlay, integration |
| 5 | Performance checks, viva preparation |
