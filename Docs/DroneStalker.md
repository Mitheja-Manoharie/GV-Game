# Drone Stalker ("Specimen" agent)

A security drone whose targeting was corrupted during the lockdown. It does not patrol in the open and shoot: it
**lurks in the dark, watches the player without being seen, and strikes when the player is weak or looking away**.
The lighting of the level is part of its decision-making: dark tiles are cheap for it, lit tiles and tiles the player
can see are expensive.

**Architecture:** a utility-based agent that plans with A* over a visibility cost, driven only by what it perceives.

## PEAS

| | |
|---|---|
| **Performance** | Damage the player while staying unseen; avoid being spotted; never get stuck; keep pressure without being unfair (the player can always escape by breaking line of sight) |
| **Environment** | The Lab Lockdown level graph (4 m tiles, doors on edges), baked lighting, open/closed/locked doors, one player |
| **Actuators** | A route (world positions) and a movement speed for the body, a facing target, a strike request |
| **Sensors** | Its own sight (20 m, 120°, night vision, blocked by walls and doors), whether the player can see it, the light level of every tile, which doors are shut, the player's health |

## Architecture

```
StalkerSensor ──► StalkerPercepts ──► StalkerBrain ──────────────────────────► StalkerDecision ──► StalkerAgent ──► body
 (scene)            (what it senses)    │ awareness: sees the player?                (action, route,     (publishes path,   (Agent
                                        │   no  ──► PatrolPlanner (dark-biased)        target, strike)    speed, events)     Controller)
                                        │   yes ──► UtilityScorer (5 tactics)
                                        │           target selection
                                        └──────► AStarPathfinder + VisibilityCostModel
```

The **brain is pure C#** (no `UnityEngine`), so every behaviour is unit tested. Scene code only senses and publishes;
it never moves, rotates or animates anything (that is the Agent Controller's part).

| Assembly | Folder | Contents |
|---|---|---|
| `LabLockdown.AI.DroneStalker.Core` | `Assets/Scripts/AI/DroneStalker/Core` | `StalkerAction`, `StalkerPercepts`, `StalkerSettings`, `VisibilityCostModel`, `AStarPathfinder`, `UtilityScorer`, `PatrolPlanner`, `StalkerBrain` |
| `LabLockdown.AI.DroneStalker.Runtime` | `.../DroneStalker/Runtime` | `StalkerLightMap`, `StalkerSensor`, `StalkerAgent`, `StalkerDebugView` |
| `LabLockdown.AI.DroneStalker.Tests` | `.../DroneStalker/Tests/EditMode` | `AStarTests` (10), `StalkerBrainTests` (17) |

It reuses the shared `LevelGraph` and `LevelGraphProvider` from the Facility AI and nothing else.

## Perception

**Drone sight.** The player is in sight when they are within `SightRangeMetres` (20 m), inside the
`FieldOfViewDegrees` (120°) of the drone's eye, and no wall (`Default`) or door leaf (`Doors`) lies in between.
**Night vision:** darkness does not affect it.

**Player sight (the asymmetry).** The player notices the drone when it is inside the player camera's view, within
30 m, with a clear line of sight, **and** either its tile is lit (above `DarkLightLevel`) or it is within 5 m.
In the dark the player only notices it up close. The same rule, applied to every tile four times a second, gives the
set of tiles the player can see. *The drone sees in the dark, the player does not*: that is what makes stalking work.

**Doors, sensed physically.** A door counts as closed while one of its own leaves blocks the doorway centre (a player or
prop standing in the doorway does not count). Locked doors are shut, so they are covered too.

**Light map.** Built once at the start of play (`StalkerLightMap`): for every tile, the baked light probes are sampled
in six directions at hover height (they hold the baked lights and their bounced light); realtime fill lights are added
with a range falloff when no wall is in between. Values are rescaled across the level (5th percentile → 0,
90th → 1), because the drone cares about *darker than elsewhere*, and this keeps the cost weights meaningful whatever
the light intensities are.

## Awareness and patrol

The drone is **not omniscient**. It knows where the player is only while it sees them, plus a
`LostSightGraceSeconds` (1.5 s) grace period after losing sight, during which it acts on the last tile it saw them on.
The grace period stops it giving up every time a pillar or door frame blocks its view for a moment; a door shut in its
face still makes it give up within 1.5 s.

When it is not aware of the player it **patrols** (also from the start: it never hovers at its spawn point).
Each patrol leg goes to a random tile 8–24 m away (walking, through open doors only), chosen in proportion to

    weight = 1 + PatrolDarkWeight × (1 − light)          (PatrolDarkWeight = 3)

so a pitch-dark tile is four times as likely as a fully lit one. The last 8 visited tiles are skipped. On the **first
leg after losing the player**, tiles that do not lead away from where the player vanished keep only 15 % of their
weight, so the drone moves away from the closed door instead of waiting at it. The random generator is seeded (and
each drone gets its own seed), so behaviour is reproducible in tests.

Unlike the Security Hunter's patrol, which searches the most likely unsearched places from a belief built from sound,
this patrol does not search: it lurks, and it serves only until the drone spots the player.

## Path cost: A* with a visibility cost

Cost of moving along an edge into tile *n*:

    cost = edge length + LightCostWeight × light(n) + SeenCostWeight × seen(n)
         = edge length + 8 × light(n) + 16 × (1 if the player can see n, else 0)

On 4 m tiles a fully lit tile costs 3× a dark one, and a tile the player can see 5×. Closed and locked doors are
impassable. The heuristic is the straight-line distance: both penalties are never negative, so it never overestimates
the remaining cost (admissible) and A* returns the optimal route under this cost. In other words the "shortest" route
is the **sneakiest** one. `AStarMatchesDijkstraCostEverywhere` proves this on a test level.

A node budget (2000) means a search can never stall a frame. `CostsFrom` (Dijkstra, same costs) rates every candidate
spot in one pass when choosing where to hide or wait.

While the player's position is unknown, the "seen" penalty is ignored: the drone cannot know what an unseen player sees.

## Utility decisions

While aware of the player, every 0.25 s the brain scores five tactics from normalised factors (all 0 to 1):

| Factor | Meaning |
|---|---|
| proximity | 1 within 8 m of walking, 0 beyond 28 m, linear in between (far = 1 − proximity) |
| weak | 1 − player health |
| seen | 1 while the player sees the drone |
| lit | light level of the drone's tile |
| spotted | fades from 1 to 0 over 5 s after being seen |
| struck | fades from 1 to 0 over 6 s after a strike |
| spot | quality of the best ambush spot: (1 − exposure) / (1 + walking distance / 40 m) |

| Action | Score |
|---|---|
| **Stalk** | 0.35 + 0.25 × far − 0.40 × seen |
| **Hide** | 0.55 × seen + 0.25 × lit + 0.20 × spotted |
| **Ambush** | (0.40 × weak + 0.35 × spot + 0.15 × proximity) × (1 − struck), only if an ambush spot exists |
| **Strike** | 0.45 + 0.50 × weak, only if the player is in sight, within 2.5 m, the drone is unseen, and 12 s have passed since the last strike |
| **Retreat** | 0.90 × struck + 0.60 × spotted × proximity |

Scores are clamped at 0 and the highest wins. Every term is kept (`ActionScores.Terms`), so the debug panel shows
exactly why an action won.

**Hysteresis.** The current tactic is kept for at least 1.5 s unless a new one beats it by 0.1. Exceptions that switch
at once: being seen (Hide or Retreat), a chance to strike, or the current tactic becoming impossible.

**The ambush fade after a strike** is a rule with a reason: right after striking, the drone has given its position
away, so lingering for another ambush makes no sense. Without it, Ambush tied with Retreat one second after a strike.

### Checked scenarios

| Situation | Winner |
|---|---|
| Player healthy, 30 m away, does not see it | Stalk 0.60 (Ambush 0.18) |
| Spotted at 12 m | Hide 0.88 (Retreat 0.48) |
| Player at 30 % health, good dark spot near their route | Ambush 0.66 (Stalk 0.44) |
| Player passes 2 m away without seeing it | Strike 0.80 (Ambush 0.71) |
| One second after striking | Retreat 0.75 (Ambush 0.13) |

## Target selection

| Action | Target tile |
|---|---|
| Patrol | dark-biased random tile 8–24 m away (see above) |
| Stalk | a dark, unseen tile about 13 m (a quarter of the way from "near" to "far") from the player, cheap to reach |
| Hide | the cheapest dark tile the player cannot see, within 16 m; otherwise the least exposed reachable tile |
| Ambush | a dark, unseen tile on or beside the player's next 4 steps. The player's route is **predicted**: to the nearest heal point when below 50 % health, otherwise to the exit |
| Strike | the player's tile |
| Retreat | a reachable tile at least 20 m from the player, as dark and unseen as possible; otherwise the farthest one |

If the target cannot be reached (for example behind a closed door), the drone hides nearby instead. Between decisions
the route is refreshed every 0.5 s, and **at once** if a door on it is closed or locked.

## Why this architecture

| Alternative | Why not |
|---|---|
| Finite state machine | Five tactics with many interacting factors would need a transition for every pair, with hand-written conditions. Utility scores every option from the same factors, so a new factor changes all decisions consistently, and the reason for each decision can be shown as numbers. |
| Behaviour tree | Good for fixed priorities, but stalking is about **trade-offs** (how weak the player is versus how exposed the drone is), which a priority order expresses badly. |
| Goal-oriented planning | The Flanker already uses it. The drone's tactics are single moves, not chains of actions, so a planner would add cost without benefit. |
| NavMesh / plain A* by distance | Would take the shortest route, through lit rooms and the player's view. The visibility cost is what makes the drone sneaky. |
| Omniscient tracking | Unfair: the player could never escape. The drone acts only on what it perceives. |

## Weights register

| Setting | Value | Effect and reason |
|---|---|---|
| `LightCostWeight` | 8 | A fully lit 4 m tile costs 12 (3× dark): strong enough to choose a longer dark route, weak enough to cross a light when needed |
| `SeenCostWeight` | 16 | A tile in the player's view costs 20 (5× dark): being seen is worse than being lit |
| `HeuristicWeight` | 1 | Keeps A* optimal |
| `SearchNodeBudget` | 2000 | Far above the level's size; only a safety limit |
| `NearDistanceMetres` / `FarDistanceMetres` | 8 / 28 | Two to seven tiles: the range where the player is a threat or an opportunity |
| `StrikeRangeMetres` | 2.5 | Melee reach |
| `StrikeCooldownSeconds` | 12 | With about 15 damage per strike this is about 1.25 HP/s, in line with the group's agreed 1.2 HP/s |
| `SpottedMemorySeconds` / `StrikeRetreatSeconds` | 5 / 6 | Long enough to get away, short enough to return to stalking |
| `DarkLightLevel` | 0.35 | Bottom third of the level's light range counts as dark |
| `HideSearchMetres` | 16 | Four tiles: a hiding place it can reach before the player closes in |
| `AmbushLookaheadNodes` | 4 | About 16 m of the player's route: far enough to get there first |
| `RetreatMinDistanceMetres` | 20 | Five tiles: out of the player's immediate reach |
| `SightRangeMetres` / `FieldOfViewDegrees` | 20 / 120 | A forward sensor with a wide view, but it can still be approached from behind |
| `LostSightGraceSeconds` | 1.5 | Covers pillars and door frames, not deliberate escapes |
| `PatrolDarkWeight` | 3 | Dark tiles 4× as likely: keeps to the shadows without ignoring lit areas |
| `PatrolMinLegMetres` / `PatrolMaxLegMetres` | 8 / 24 | Real movement, no dithering between neighbouring tiles |
| `PatrolRecentTiles` | 8 | Spreads the patrol over the level |
| `PatrolTowardLastSeenFactor` | 0.15 | Moves away from where it lost the player |
| `DecisionIntervalSeconds` / `ReplanIntervalSeconds` | 0.25 / 0.5 | Responsive without re-planning every frame |
| `ActionCommitSeconds` / `SwitchMargin` | 1.5 / 0.1 | Stops flip-flopping |
| Utility weights | see table above | Checked against the scenarios above |

## Edge cases

- No light data: tiles count as half-lit. No line-of-sight data: nothing counts as seen. Missing heal points or exit: no ambush, other tactics still work.
- Unknown positions (drone or player off the graph): the drone holds instead of throwing.
- Target behind a closed door: hides nearby. Sealed in completely: stays put.
- Door closed mid-route: re-plans at once; if no route remains, patrols instead.
- Player behind a wall or door, even 1 m away: never strikes (a strike needs sight).
- Being seen mid-tactic: reacts at once, ignoring the commit time.
- Several drones: each has its own patrol seed, so they spread out.
- Player respawn: `StalkerAgent.ResetAgent()` forgets the player and patrols again.
- No level graph in the scene: the agent logs a warning and stays idle.

## Tests

`Window > General > Test Runner > EditMode > Run All` (27 tests, about 50 ms).

- **A\*** (10): shortest route when dark; longer dark route over a short lit one; avoids a tile the player sees; reroutes round a locked door; no path when sealed off; start equals goal; invalid nodes never throw; missing light data; A* equals Dijkstra everywhere (admissible heuristic); stops at the node budget.
- **Brain** (17): stalks from the dark; hides the moment it is seen; ambushes a weak player on the way to the infirmary; strikes then retreats with no second strike; no flip-flop inside the commit time; replans at once at a locked door; sealed in never throws; unknown positions hold; missing sensor data still decides; never strikes while seen; patrols from the start; switches from patrol the moment it spots the player; keeps hunting during the grace period, then patrols; loses the player behind a closed door and patrols away from it; never strikes without seeing the player; patrol prefers dark tiles; patrol avoids recent tiles.

## Integration for the Agent Controller

`StalkerAgent` is the hand-over point. It publishes; the body carries it out.

| Output | Type | Use |
|---|---|---|
| `CurrentPath` | `IReadOnlyList<Vector3>` | The route as tile centres on the floor (the current tile not included). Pass to `IPathFollower.SetPath` |
| `CurrentSpeed` | `StalkerMoveSpeed` | `Stop` (waiting in ambush or no route), `Patrol`, `Investigate` (stalk, hide, move to ambush), `Chase` (strike, retreat): maps directly onto `MoveSpeed` |
| `CurrentAction` | `StalkerAction` | For animation or sound (e.g. eye colour per action) |
| `LookTarget` | `Vector3?` | Face the player while known; null means face along the route |
| `PathChanged` | event | Raised only when the route or speed changes |
| `ActionChanged` | event | (previous, new) |
| `StrikeRequested` | event (`Transform` target) | Raised once per strike: play the attack and deal the damage (about 15, see the weights register) |

**Setup on the drone prefab** (body: `Assets/ThirdParty/Quaternius/Essentials/FBX (Unity)/Enemy_EyeDrone.fbx`):

1. Add `StalkerSensor` and `StalkerAgent` to the drone root (`StalkerDebugView` optional, for testing).
2. **Eye:** assign the drone's eye transform (its forward is where it looks); defaults to the root.
3. **Heal points:** assign the infirmary and medkit positions, so a hurt player's route can be predicted.
4. Leave the level graph and the player empty: they are found automatically (`LevelGraphProvider`, `Player` tag).
5. **Sight blockers** default to `Default` and `Doors`.
6. The body hovers at about 1.4 m (the light map and tile visibility are sampled at that height; change `hoverHeight` on the sensor if it differs).
7. On player respawn call `ResetAgent()`.

The drone never routes through a closed door, so a follower's "open the door when stuck" logic is not needed for it.

## Debug view

`StalkerDebugView` (display only):

- **Scene view:** the route coloured by action (grey patrol, blue stalk, green hide, purple ambush, red strike, orange retreat), the target tile, the vision cone, the last place the player was seen (magenta), the tiles the player can see (red) and, optionally, the light map (blue dark to yellow lit).
- **Play mode panel:** the action and its reason, awareness, in sight, seen by player, light here, speed, and the five utility scores as bars.

## Limitations and future work

- The light map is built once; lights switched on or off during play are not seen until it is rebuilt.
- Player sight uses a simple "lit or close" rule rather than contrast against the background.
- Patrol could move between dark **lookout tiles** that overlook much of the level (needs tile-to-tile visibility data).
- Utility weights were tuned against scenarios and unit tests; they still need tuning in play with the final body speed.
