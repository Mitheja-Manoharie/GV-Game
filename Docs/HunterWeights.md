# Security Hunter: weights and heuristics register

Every constant in the Hunter appears here with a reason and a way to check it.
Status: **Initial** (reasoned, not yet tuned) or **Tuned** (changed after testing; reason recorded in the change log).

## Units

Loudness is the open-air audible radius in metres. Tiles are 4 m, so one open edge costs 4 loudness units at `alpha = 1`.

## Sound

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| L_door | Door-open noise | 6 | Existing `DoorController` value | n/a | Fixed |
| L_throw | Throw impact | 8 x [0.4, 1.5] | Existing `PhysicsProp` values | n/a | Fixed |
| L_barricade | Barricade impact | 12 x [0.4, 1.5] | Existing `PhysicsProp` values | n/a | Fixed |
| L_gun | Gunshot | 30 (from the player's WeaponController) | Carries about 7 open tiles; far above props (8-12) so shots dominate | Unit test: audible radius | Fixed |
| L_walk | Player footstep, walking | 3 (from the player's footstep script) | Below one 4 m tile edge: heard only when the Hunter is in the player's own tile | Unit test | Fixed |
| L_sprint | Player footstep, sprinting | 8 (from the player's footstep script) | Reaches two open tiles, about the same as a thrown mug | Unit test | Fixed |
| L_crouch | Player footstep, crouching | 0 (silent) | Gives the player a stealth option | n/a | Fixed |
| alpha | Loss per metre | 1 | Makes loudness equal audible radius in metres | Unit test: audible exactly L m in open space | Initial |
| beta | Closed or locked door loss | 3 | A closed door should muffle a normal throw, not erase it: 8 - (4 + 3) = 1 = theta, just audible next door; a hard throw (12) carries 5 | Unit test: door muffling | Initial |
| theta | Hearing threshold | 1 | Stops propagation; below this the Hunter hears nothing | Unit test | Initial |

## Belief

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| sigma0 | Base localisation error | 4 m | One tile: the graph cannot resolve finer than a tile | Plot belief for a close open-air noise | Initial |
| kappa | Extra error per loudness unit lost | 0.5 m/unit | More muffled means vaguer; 20 units lost gives sigma = 14 m | Plot belief spread vs loss | Initial |
| delta | Belief decay rate | 0.05 /s | About a 20 s time constant for a trail going cold | Observe search duration in playtest | Initial |
| searchedFactor | Multiplier on a searched node | 0.1 | Searched means unlikely, not impossible (player may have moved) | Test: no oscillation between rooms | Initial |
| epsilon | Cost offset in U | 1 m | Avoids division by zero at the Hunter's own node | n/a | Initial |

## Planning

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| C_closed | A* closed-door penalty | 3 | Door-open time (about 1 s) x walk speed (about 3 m/s) | Test: prefers a short open route over a closed door only when the detour is under 3 m | Initial |
| locked, blocked | Edge cost | infinity | Cannot be walked through | Test | Fixed |
| h | Heuristic | Euclidean | Admissible: every edge cost >= Euclidean distance | A* vs Dijkstra test | Fixed |

## Edge cases

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| dSelf | Radius for ignoring own door noise | 4 m | One tile: the Hunter opens doors it is standing at | Test: no self-trigger | Initial |
| tBlock | Door still Closed after open command | 2 s | Open animation should finish well within this | Test with barricaded door | Initial |
| tForget | How long a learned blocked edge is remembered | 20 s | A barricade can be dragged away; matches delta's time scale | Playtest | Initial |
| habituation (stretch) | Credibility factor for repeat noise near an earlier one | 0.5 per repeat within r and T | Stops throw-spam from pinning the Hunter | Only if time allows | Deferred |

## Not yet set (to be added when behaviours are written)

State-transition thresholds with hysteresis (Investigate, Search, Chase, back to Patrol),
field of view and sight range, search timeout, speeds per state.

## Change log

| Date | Change | Reason |
|---|---|---|
| 2026-10-10 | beta set to 3 (an earlier draft had 5) | With 4 m edges, beta = 5 made a normal throw inaudible through a closed door (8 - 9 < 0), contradicting the intended "muffled, not erased" behaviour |
| 2026-10-12 | L_gun changed from a proposed 40 to the real 30; footstep loudness values added | The Systems Engineer update defines these values; the register must match the game |
