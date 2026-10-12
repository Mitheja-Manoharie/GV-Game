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
| `LabLockdown.AI.Facility.Core` | `LevelGraph`, `LevelGraphBuilder`, `LabLockdownLayout`, `Csp/` solver (later: utility model, director) | **None** (`noEngineReferences`) |
| `LabLockdown.AI.Facility.Runtime` | `LevelGraphProvider`, `SpawnPointMarker` (later: director runner, door/spawn services) | Yes |
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

## Default spawn points

Eight candidates spread over both routes; none in the quarantine cell, armoury or control room:
MaintenanceBay, Crossroads, ServiceCorridor, LabCorridor, ResearchLab, Infirmary,
InfirmaryNorth, WestCorridor.

## Plan

| Week | Work |
|---|---|
| 1 | Level graph, BFS/Dijkstra, gizmos, spawn markers, tests ✅ |
| 2 | Generic CSP solver (backtracking, MRV + degree, LCV, forward checking) + tests ✅ |
| 3 | Facility constraints, intensity model, spawn/door services |
| 4 | Edge cases, tuning, debug overlay, integration |
| 5 | Performance checks, viva preparation |
