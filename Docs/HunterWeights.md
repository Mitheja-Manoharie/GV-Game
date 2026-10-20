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
| w0 | Noise weight half-point: w = heard / (heard + w0) | 5 | A noise heard at level 5 gets weight 0.5; a gunshot heard at 30 gets 0.86, a faint footstep at 1 gets 0.17 | Unit test: louder noise moves the belief more | Initial |
| shotWeight | Weight of being shot | 0.9 | The shooter's direction is known well; not 1, because the shooter may move | Unit test | Initial |

## Planning

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| C_closed | A* closed-door penalty | 3 | Door-open time (about 1 s) x walk speed (about 3 m/s) | Test: prefers a short open route over a closed door only when the detour is under 3 m | Initial |
| locked, blocked | Edge cost | infinity | Cannot be walked through | Test | Fixed |
| h | Heuristic | Euclidean | Admissible: every edge cost >= Euclidean distance | A* vs Dijkstra test | Fixed |

## Decision layer

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| chaseMemory | Keeps chasing after losing sight | 4 s | About 12 m at 3 m/s, three tiles: enough to follow the player round a corner | Unit test | Initial |
| investigateEnterRatio | Peak / flat level needed to start investigating | 2.0 | A gunshot heard 16 m away gives 2.5 and must trigger it; one 24 m away gives 1.9 and only biases the patrol (measured, see HunterAI.md) | Unit test | Initial |
| investigateExitRatio | Peak / flat level below which investigating ends | 1.5 | Below the enter ratio so the state cannot flap. A gunshot 8 m away decays to 1.5 in about 30 s | Unit test: hysteresis | Initial |
| reselectInterval | Minimum time between target re-evaluations | 0.5 s | A 45-node cost field is cheap; reselecting every frame would cause jitter | Unit test: no flip-flop | Initial |
| switchMargin | A new target must beat the current utility by this fraction | 0.25 | Stops the Hunter dithering between two similar targets | Unit test: no flip-flop | Initial |
| forgetSeconds | How long a jammed door is remembered (tForget) | 20 s | A barricade can be dragged away; roughly the belief's own time constant (1 / 0.05 per s) | Unit test | Initial |

## Attack and movement (calibrated to the group balance agreed with the Facility AI and Flanker owners)

| Symbol | Meaning | Value | Justification | Check | Status |
|---|---|---|---|---|---|
| attackDamage | Health taken per hit | 6 | Group balance | n/a | Fixed (group) |
| shotInterval | Time between shots = windUp + cooldown | 2.5 s | Group balance | Unit test: shots 2.5 s apart | Fixed (group) |
| windUp | Aim time before the shot | 0.6 s | Long enough to react and duck behind cover, short enough to stay threatening. Part of the 2.5 s interval | Playtest | Initial |
| attackCooldown | Pause after a shot | 1.9 s | 2.5 s interval minus the 0.6 s wind-up | Unit test | Derived |
| maxAccuracy | Hit chance at 0 m | 0.8 | Close range is dangerous but not certain | Statistical unit test | Initial |
| minAccuracy | Hit chance at attackRange | 0.2 | Long shots mostly miss, so retreating helps | Statistical unit test | Initial |
| (mid-range accuracy) | Hit chance at 6 m | 0.5 | Equals the group's 50% roll, so average damage per second at mid-range is 6 x 0.5 / 2.5 = 1.2, the same as the group's pacing simulation | Unit test | Derived |
| attackRange | Furthest shooting distance | 12 m | About three tiles. The group has not fixed a range; to be confirmed | Playtest | Initial |
| robotHealth | Hunter health | 125 | Group balance: 5 rifle hits of 25 | n/a | Fixed (group) |
| baseSpeed | Chase speed | 3 m/s | Group balance | n/a | Fixed (group) |
| patrolSpeedFactor | Patrol speed / base | 0.5 (1.5 m/s) | A patrolling robot listening for noise moves slowly | Playtest | Initial |
| investigateSpeedFactor | Investigate speed / base | 0.75 (2.25 m/s) | Between patrol and chase | Playtest | Initial |

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
| 2026-10-17 | Noise update changed from multiplying evidence into the belief to blending it | Footsteps repeat many times a second; multiplication collapses the belief to one node (and shrinks sigma) within a few steps, blending converges to the likelihood |
| 2026-10-17 | Attack numbers changed: damage 10 to 6, cooldown 1.2 s to 1.9 s (2.5 s between shots), accuracy 0.9 to 0.8 at close range | My first numbers gave about 3.1 damage per second at mid-range, 2.5 times the group's balance of 1.2, which would break the pacing simulation |
