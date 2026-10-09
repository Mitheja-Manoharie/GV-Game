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
| `LabLockdown.AI.Facility.Core` | `LevelGraph`, `LevelGraphBuilder`, `LabLockdownLayout` (later: CSP solver, utility model, director) | **None** (`noEngineReferences`) |
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

## Default spawn points

Eight candidates spread over both routes; none in the quarantine cell, armoury or control room:
MaintenanceBay, Crossroads, ServiceCorridor, LabCorridor, ResearchLab, Infirmary,
InfirmaryNorth, WestCorridor.

## Plan

| Week | Work |
|---|---|
| 1 | Level graph, BFS/Dijkstra, gizmos, spawn markers, tests ✅ |
| 2 | Generic CSP solver (backtracking, MRV, forward checking) + tests |
| 3 | Facility constraints, intensity model, spawn/door services |
| 4 | Edge cases, tuning, debug overlay, integration |
| 5 | Performance checks, viva preparation |
