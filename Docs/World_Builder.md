# World Builder – Level, Lighting and Bakes

**Owner:** Mitheja Manoharie (IT24103443) · SE3032 Graphics and Visualization

Scope: the Lab Lockdown level in `Main.unity`, covering layout, modular art, props, ceilings, lighting,
post-processing and the lighting, occlusion and NavMesh bakes. The Facility AI built on top of this level
is documented separately in [FacilityAI.md](FacilityAI.md).

## Level layout

The level is built on a **4 m grid**: one floor tile, wall piece and level-graph node per 4 × 4 m cell,
with a 5 m ceiling. The player starts in the quarantine cell at (−2, −2) and must reach the `ExitPoint`
in front of the control room window at (2, 30.5).

| Area | Tiles (X × Z) | Role |
|---|---|---|
| Quarantine cell | 2 × 2 | start, safe area |
| Quarantine corridor | 1 | safe area |
| Armoury | 2 × 2 | weapon pickup, safe area |
| Service corridor | 1 × 2 | through `Door_ArmouryNorth` |
| Crossroads | 2 × 1 | hub between the service corridor, maintenance bay and lab corridor |
| Maintenance bay | 3 × 3 | large open room, heavy cover |
| Lab corridor | 1 × 2 | east route |
| Research lab | 2 × 2 | specimen tanks, through `Door_LabSouth` |
| Lab link | 1 | research lab → control room |
| Infirmary corridor | 1 | bay → infirmary |
| Infirmary | 2 × 2 | west route, medkit |
| Infirmary north corridor | 1 × 2 | through `Door_InfirmaryNorth` |
| West corridor | 3 × 1 | west route → control room |
| Control room | 3 × 2 | exit; doors `Door_ControlWest` and `Door_ControlEast` |

**Two routes to the exit.** After the armoury the player reaches the crossroads and chooses:

- **Lab route (east):** lab corridor → research lab → lab link → control room. This is the shortest route, at 60 m from the start.
- **Infirmary route (west):** maintenance bay → infirmary → north and west corridors → control room.

**Keycard objective.** The exit is locked until the player takes the keycard (`KeycardPickup` on a `Prop_KeyCard`,
`Assets/Scripts/Level`): it hovers, turns and glows screen blue, and E takes it. Every run (start, respawn, play
again) puts it on one of four `KeycardSpot_*` markers at random, never the same one twice in a row: the infirmary
desk, a crate in the service corridor, a crate in the lab corridor and the large crate in the maintenance bay
(**Tools > World Builder > Place Keycard Spots**). An objective line at the top of the screen says "Find the keycard" (without naming the area), then "Reach the
exit", and
`ExitTrigger` shows "EXIT LOCKED / Find the keycard" if the player arrives without it. Every spot lies before any
lockable door, so the Facility AI can never lock the player away from the keycard, and with the keycard both
routes still lead to the exit.

Each route passes through one lockable door, so the Facility AI can lock one route and force the other
without ever cutting off the exit (see [FacilityAI.md](FacilityAI.md)).

**Pacing by layout.**

- The safe start (cell, corridor, armoury) gives the player time to pick up gear.
- The open maintenance bay is the main fight space.
- The routes narrow into 4 m corridors.
- The brightest room is the control room, which draws the player to the exit.

## Scene structure

```
Environment                 NavMeshSurface (bakes from physics colliders)
├── <Room>                  one per area above (ServiceCorridor counts as a room; Corridors is shared)
│   ├── Art                 floors, WallAstra walls, door frames, corner columns
│   ├── Ceiling             TopCables trim + flipped ceiling plates
│   └── Props               Cover · Throwables · Pickups · Decor
├── Doors                   Door_* markers (the Systems Engineer builds doors on them)
├── Lighting/<Room>         light fixtures, glow lights, one Light Probe Group per room
├── SpawnPoints             SP_* markers for the Facility AI
├── CeilingCollider         one box collider over the whole level
└── ExitPoint
Global Volume               post-processing
```

The work was done in a personal dev scene and then copied to `Main.unity` (#18), which has its own bake
data in `Assets/Scenes/Main/`. The dev scene was removed once `Main` was final.

## Build process

1. **Greybox:** grey cubes on the 4 m grid (`Assets/Materials/Greybox`) to check the routes, scale and door positions.
2. **Art pass, one area at a time:** Quaternius MegaKit modular pieces (CC0) replace the greybox.
3. **Doors:** frames and `Door_*` markers, plus a `Doors` layer kept out of the NavMesh bake.
4. **Props:** cover, throwables, pickups and decor per room.
5. **Ceilings:** top trim and ceiling plates.
6. **Lighting:** fixtures, probes and post-processing, then preview bakes and a readability review of each room.
7. **Final bakes:** lighting, occlusion and NavMesh, rebaked for `Main.unity` on 2026-10-19.

## Modular art

| Element | Choice | Details |
|---|---|---|
| Walls | `WallAstra_*` | Non-convex `MeshCollider` on its own mesh. `WallAstra_Straight_Window` uses `Glass_Dark.mat`, because the kit's glass is opaque light grey. |
| Room floors | `Platform_CenterPlate` / `Platform_DarkPlates` | Visual only, with no collider. |
| Corridor floors | `Platform_Simple` | Visual only, with no collider. |
| Walkable floor | Greybox floor slabs | The renderer is off and the `BoxCollider` stays on. The player walks on it and the NavMesh bakes from it. |
| Door frames | `Door_Frame_Square` | `Door_Frame_A` only at the two control room doors. `Door_Frame_Square_Blocked` on some corridor walls as "sealed during lockdown" set dressing. |
| Ceiling | `Platform_DarkPlates` | Flipped (rot X 180) at Y 5, shadows Two Sided, no collider. One `CeilingCollider` (36 × 0.2 × 36 m) covers the level. |
| Top trim | `TopCables_Straight` / `_Corner_*` | Above each wall. `TopCables_Straight_Hanging` where a room has cables on the walls. |

**Corners.** `WallAstra_Corner_Square_Outer` and `_Inner` are L-shaped pieces that replace two straight walls.
Their pivot sits on a tile corner, not a tile centre. Every corner piece needs two whole 4 m edges, so two
corners cannot share one edge. Three room corners where a room wall meets a corridor corner (maintenance bay
SW and NW, control room SW) showed a sliver of sky. Each one got a `Column_Simple` rotated 45° to chamfer
the corner.

**Light-leak check.** After the first bake, rays were cast every 0.5° from standing points on a 1 m grid.
A ray that hit nothing had escaped the level. Kit meshes are single-sided, so this finds gaps that are easy
to miss. Three gaps were closed with dark `GapFiller` cubes:

- the quarantine corridor jambs;
- the armoury jamb;
- the maintenance bay SW corner.

## Layers and static flags

| Layer | Index | Used for | In NavMesh bake |
|---|---|---|---|
| Default | 0 | walls, floors, static cover | yes |
| Doors | 3 | door leaves | **no**: doors open and close, and locked doors carve their own obstacle |
| Ceiling | 6 | ceiling, trim, ceiling fixtures | **no** |
| DynamicProps | 7 | throwables and barricades | **no**: moved props would leave permanent holes |

The `NavMeshSurface` layer mask is `4294967095`, which is every layer except 3, 6 and 7.

| Object | Static flags |
|---|---|
| Structure (Art, Ceiling, floors, columns, gap fillers) | `2147483647`: everything, including Occluder and Occludee |
| Static props and light fixtures | `2147483645`: Occludee only, so props never hide what is behind them |
| Throwables, barricades, pickups | not static |

Static flags are set on every child renderer of multi-mesh FBX models, not just the root.

## Props

Prefabs live in `Assets/Prefabs/Props` and every copy in the scene comes from one of them.

| Type | Examples (count in `Main`) | Static | Collider | Layer |
|---|---|---|---|---|
| Cover, large decor | crates, desks, shelves, lockers, `Prop_SpecimenTank` (4) | yes | Box (tanks: Capsule) | Default |
| Small decor on surfaces | syringe, tubes, keyboard | yes | none | Default |
| Barricades | `Barricade_Barrel` (4), `Barricade_CrateTarp` (1) | no | Box | DynamicProps |
| Throwables | `Throwable_Mug` (8), `Throwable_AmmoSmall` (4), `Throwable_AmmoClosed` (4) | no | Box | DynamicProps |
| Pickups | `Pickup_HealthPack` (3), weapon and armour markers | no | none (the Systems Engineer adds triggers) | Default |

**Placement rules**

- Props stand at least 0.6 m from the wall line, because WallAstra walls lean about 0.45 m into the room.
- Walkways are at least 2 m wide, and doorways and corridor openings are never blocked.
- Corridors are only 3.12 m wide between wall faces. Cover there is small, crouch-height `Prop_Crate3`
  (1 m cube). Big crates are used only in open rooms (crossroads, maintenance bay).
- Nothing clips into walls or other props. Scale is 1, except the specimen tanks and the aliens inside them.

**Room highlights**

- **Armoury:** the six guns stand upright on a lit shelf. Their materials glow slightly (emission 0.5×) so the shelf reads as a pickup.
- **Research lab:** four `Prop_SpecimenTank` glass tanks with MegaKit aliens inside. The tank is built from
  primitives (glass cylinder, metal base, cap and pipe, green glow strip), so no extra model was needed.
  **Floating specimens:** `SpecimenFloat` (`Assets/Scripts/Level`) on each tank alien gives a slow bob (6 cm, about
  5 s), a gentle turn (8°) and a slight tilt, each with a random phase so they never move in step. The aliens are
  not Batching Static, so Unity can move them.
  **Story:** a chair knocked onto its side in front of the tanks, syringes dropped on the floor and left on the
  desk, as if the scientists left in a hurry. Placed by **Tools > World Builder > Place Lab Story** (`LabStoryPlacer.cs`) under `Props/Story`;
  **Remove Lab Story** deletes them. They are not static and use light probes, so no rebake.
- **Door access panels:** a MegaKit `Prop_AccessPoint` on the wall beside each of the four lockable doors, on the
  side the player approaches from (**Tools > World Builder > Place Door Access Panels**, under
  `Environment/AccessPanels`). `DoorAccessPanel` (`Assets/Scripts/Level`) follows `FacilityEvents.DoorLockChanged`:
  the light strip and screen glow green while the door is open, and blink red with two short beeps when the Facility
  AI locks it, staying red until it reopens. A small no-shadow light spills the colour onto the wall. Not static, so
  no rebake.
- **Spinning fans:** `FanSpin` (`Assets/Scripts/Level`) turns the "Propeller" bone of the rigged `Prop_Fan_Small`, so
  the blades spin while the housing stays still. It runs on the maintenance bay's wall fan and on two more fans in
  the west corridor and at the crossroads (**Tools > World Builder > Place Corridor Fans**, under
  `Environment/Fans`), mounted like the bay fan. The new fans are not static, so no rebake.
- **Control room:** consoles in the south corners, with screens facing the room. Low crates frame the exit.
  **Live screens:** `LiveScreen` (`Assets/Scripts/Level`) on the two `Prop_Computer`s and the `Prop_AccessPoint`
  replaces each screen's texture (only the screen slot: the kit's `M_Decal_Screen`, imported as `T_Decals`, through a `MaterialPropertyBlock`) with a
  generated 64 × 64 data texture: rows of "text" scrolling upwards in screen blue, a faint flicker and a short
  refresh glitch every 3–9 s. Each screen has its own layout and speed; the shared kit material is untouched.

## Decals

Red hazard stripes (`Decal_Dashes`, 3 per door) on the approach side of the four doors the Facility AI can lock,
so the player can tell which doors may trap them. They are placed by **Tools > World Builder > Place Decals**
(`Assets/Scripts/Editor/DecalPlacer.cs`) under `Environment/Decals`; running the tool again rebuilds the group.

- **Material** (`Assets/Materials/Decals/M_Decal_Red`, emergency red): the kit's `T_Decals.png` is white on black
  with no alpha, and many kit walls, door frames and consoles use it too. So the decal uses its own copy,
  `T_Decals_Cutout.png`, imported with *Alpha Source: From Gray Scale* at 1024 px, and the kit texture stays
  untouched. The material uses alpha clipping (cutoff 0.5) instead of transparency: no sorting, depth writes
  stay on, and it stays in the opaque pass. Mipmaps preserve coverage so thin stripes do not vanish at a
  distance. The `Decal_Dashes` importer remaps the kit material to it.
- **Cost:** flat quads 1.4 cm above the floor, not static, no shadows and no colliders. They need no lighting,
  occlusion or NavMesh rebake; light probes light them.

## Particle effects

Placed by **Tools > World Builder > Place Particle Effects** (`Assets/Scripts/Editor/ParticlePlacer.cs`) under
`Environment/Effects`; running the tool again rebuilds the group.

| Group | Where | Look | Max particles |
|---|---|---|---|
| Sparks | under a ceiling light in the service corridor and in the infirmary north corridor | showers of stretched streaks that fall and bounce off the floor, timed by `FaultyLight` | 60 each |
| TankBubbles | inside every `Prop_SpecimenTank` | small green-tinted bubbles rising about 2 m | 30 each |

- **Faulty lights:** `FaultyLight` (`Assets/Scripts/Level`) on each spark system makes the fixture above it short out
  every 1.5–5 s: the glowing panel stutters (a `MaterialPropertyBlock` on that fixture only), a shower of sparks
  falls and a warm Realtime point light (range 3 m, no shadows) flashes for a split second, all together. The flash
  light is created at runtime and off between faults, so it never affects the bake. An electrical crackle plays at
  the fixture in 3D (heard within 15 m): four variants synthesized at startup (sharp clicks over a short hiss and a
  120 Hz arc buzz, random pitch), so no audio files are needed; a clip can be assigned instead.
- **Materials** (`Assets/Materials/Particles`): copies of URP's unlit particle material. Sparks are additive with an
  HDR colour, so they bloom. Bubbles render at queue 3050, after the tank glass (3000), so they always draw
  inside it instead of flickering with the glass's sort order.
- **Cost:** about 240 particles in total; unlit, no shadows, no light or reflection probes, low-quality noise and
  collision, and culled automatically when off screen. No rebake is needed.

## Lighting

**Palette:** one cold white base in every room plus one accent colour per room.

| Colour | Hex | Used for |
|---|---|---|
| Cold white | `#DCE6FF` | base ceiling strips everywhere |
| Emergency red | `#FF3B30` | corridor door accents. The maintenance bay uses a softer `#FF5A4A`, so robots stay visible. |
| Lab green | `#6BFF7A` | tanks, medkits |
| Screen blue | `#5AB4FF` | control room consoles |

- **Ambient:** flat colour `#14181E`. The Directional Light is off, the skybox is None and the camera
  clears to `#05070A`. This is a sealed, windowless facility.
- **Fixtures:** seven prefabs in `Assets/Prefabs/Lighting`:
  - `Light_Wide_White`, `Light_Wide_Red`;
  - `Light_Small_White`, `Light_Small_Red`, `Light_Small_Spot`;
  - `Light_Corner_White`, `Light_Floor_White`.

  Each one is a kit model with an emissive material from `Assets/Materials/Lighting` and a child Baked
  light with soft shadows.
- **Light modes:** 44 lights are **Baked**. The only Realtime lights are the four `AlienGlow` lights, which
  have no shadows. The skinned aliens inside the tanks cannot be lightmapped, and no probes fit inside the
  glass, so a Baked light would not reach them.
- **Alarm beacons:** `AlarmBeacon` (`Assets/Scripts/Level`) on the six red corridor door accents and the two
  control room corner lights. While the Facility AI is at its **Peak** (`FacilityEvents.PhaseChanged`), each one
  sweeps a rotating red spot light (Realtime, no shadows) and fades out again afterwards, or when the player dies
  or escapes. The lights are created at runtime and disabled while off, so they cost nothing outside a peak and
  never affect the bake.
- **Emission:** coloured emission stays at 1×, so it does not clip to white. Cold white uses 4×, so the
  fixtures bloom.
- **Probes:**
  - **Light probes:** 346 in one Light Probe Group per room. They sit on a grid about 2–2.5 m apart at
    heights 0.5 m and 2 m, with extra probes in every opening and under the accent lights. They light
    dynamic props, robots and the player, and 24 small static props whose lightmap UVs overlapped (see Quality checks).
  - **Reflection probes:** 14 Baked, box-projected probes, one per floor area.
- **Readability:** every room was reviewed after a preview bake.
  - The maintenance bay was too dark and too red to see robots. It got white fill lights and a lighter
    strip of floor tiles, and Indirect Intensity was raised to 1.3 and Albedo Boost to 1.5.
  - Measured average probe brightness puts the control room highest (0.19) and the quarantine cell lowest
    (0.03), as intended.

**Post-processing** (`Global Volume`): Neutral tonemapping, Bloom (threshold 1, intensity 0.4),
contrast +15, Vignette 0.25.

**Low-health pulse:** `LowHealthPulse` (`Assets/Scripts/Level`, on `Global Volume`). Below 35 % health a red glow
pulses in from the screen edges like a heartbeat, stronger and faster (70 → 120 bpm) as health falls, and a second
global Volume (priority 100, Color Adjustments saturation −35) drains the colour. The red is a screen overlay, not
a Vignette: URP's Vignette multiplies the image, so in this dark level it only made the edges blacker. Both are off
while the player is healthy and fade out on death; the level's own profile is never changed.

## Bakes

All bake output for `Main.unity` is in `Assets/Scenes/Main/` and tracked with Git LFS.

| Bake | Settings | Output |
|---|---|---|
| Lighting | Progressive GPU, 20 texels/unit, max lightmap 2048, 32 direct / 512 indirect samples, 2 bounces, AO on, Normal compression, baked GI on, realtime GI off | 3 lightmaps, `LightingData.asset`, 14 reflection probe cubemaps (about 90 s) |
| Occlusion | smallest occluder 2 m (the default 5 m is bigger than a 4 m wall piece), smallest hole 0.25 m, backface threshold 100 | `OcclusionCullingData.asset` (86 KB) |
| NavMesh | Humanoid agent (radius 0.5, height 2, step 0.75, slope 45°), built from physics colliders, Doors / Ceiling / DynamicProps excluded | `NavMesh-Environment.asset` |

**NavMesh fix:** `LevelGraphProvider` warned that two corner tiles had no NavMesh within 1.5 m. One was in
the maintenance bay and one in the control room. In both, an L-shaped desk's single 2.2 m box collider
also covered the empty inside of the L. Each `Prop_Desk_L` in `Main` now has two box colliders, one per arm.
After the rebake the check passes.

`Main.unity` uses `Assets/Scenes/Main/Main Lighting Settings.lighting` and the post-processing profile
`Assets/Scenes/Main/Global Volume Profile.asset`.

### Rebaking after a change

After changing level geometry or static props in `Main`:

1. **Window > Rendering > Lighting > Generate Lighting.**
2. **Window > Rendering > Occlusion Culling > Bake.**
3. Select `Environment`, then click **NavMeshSurface > Bake**.
4. Press Play and check the Console for `LevelGraphProvider` warnings.
5. Commit everything in `Assets/Scenes/Main/` together with the scene.

## Performance

Measured on 6 November 2026 in the Editor (Unity 6.3, URP, MacBook with Apple silicon, 1920 × 1080) with
`PerformanceBenchmark` (`Assets/Scripts/Level`, on `FacilityAI`, press **F4** in Play mode). It pauses the game, moves
the camera to four fixed viewpoints and averages 60 frames per setting, so every run is repeatable. Raw data:
`Docs/Performance/benchmark.csv` and `textures.csv`. Frame times include Editor overhead and the GPU is far from its
limit, so the counts (batches, draw calls, triangles) are the meaningful cost; frame time is shown for completeness.

**Occlusion culling** (smallest occluder 2 m, see Bakes). Same viewpoint, culling on → off:

| Viewpoint | Batches | Draw calls | Triangles | SetPass calls | Frame time |
|---|---|---|---|---|---|
| Service corridor, looking north | 204 → 448 (−54 %) | 392 → 1,010 (−61 %) | 174k → 432k (−60 %) | 37 → 54 | 3.00 → 3.16 ms |
| Maintenance bay, looking NW | 290 → 409 (−29 %) | 506 → 894 (−43 %) | 241k → 334k (−28 %) | 41 → 50 | 3.19 → 3.26 ms |
| Crossroads, looking into the bay | 258 → 448 (−42 %) | 466 → 938 (−50 %) | 209k → 365k (−43 %) | 29 → 50 | 3.24 → 3.34 ms |
| Control room, from the lab link | 229 → 365 (−37 %) | 419 → 751 (−44 %) | 121k → 258k (−53 %) | 41 → 51 | 2.96 → 3.06 ms |

Culling removes 28–60 % of the triangles and 29–54 % of the batches. The gain is largest in the corridor, where walls
hide most of the level, and smallest in the open maintenance bay, where most of what is in front of the camera is
genuinely visible.

**Static batching.** With culling on, Unity draws 392–506 draw calls as 204–290 batches: 188–216 draw calls (43–48 %)
are merged, and 276–320 draw calls go through static batching (6 static batches). That is the Stats panel's "Saved by
batching" (189 in the corridor). It works because the structure and static props share a few trim-sheet materials.

**Baked vs Realtime lighting.** 44 lights are Baked and cost nothing per frame. 13 Realtime lights are active in a
normal moment, all small and without shadows: the 4 `AlienGlow` tank lights, the 4 door access panels, the keycard
glow and the 4 drones' eye lights. A Peak adds the 8 alarm beacons (21 in total), and a faulty-light flash adds 1 for a
split second. Shadow casters: 0. Forward+ rendering handles this many lights without a per-object limit.

**Texture budget** (`Tools > World Builder > Apply Prop Texture Budget`). Texture memory goes where the player looks.
A first pass capped every small Essentials prop texture at 1024 px. The texel density check (see Quality checks) then
showed two problems. The cap had halved the sharpness of the ammo boxes and health packs, to 153–178 px/m against
490 px/m on the walls. The cover crates, which the player crouches beside, imported their 4096 px source at only
2048 px (207–250 px/m). The budget now sizes each map by what it shows at the distance the player sees it:

| Texture | Kit default | Budget | Reason |
|---|---|---|---|
| T_Props_Batch2_BaseColor | 2048 px, 2,730 KB | unchanged | colour is what the player reads on a pickup |
| T_Props_Batch2_Emissive | 2048 px, 2,730 KB | 1024 px, 682 KB | small glow areas, no fine detail |
| T_Props_Batch2_ORM | 2048 px, 2,730 KB | 1024 px, 682 KB | roughness and metal detail do not show on props this small |
| T_Props_Batch2_Normal | 1024 px (source), 682 KB | unchanged | |
| T_Props_Crates_BaseColor | 2048 px, 2,730 KB | 4096 px (source), 10,922 KB | cover crates are seen up close |
| T_Props_Crates_Normal | 2048 px, 2,730 KB | unchanged | |
| T_Props_Crates_ORM | 2048 px, 2,730 KB | unchanged | |
| **Total** | **17,062 KB** | **21,162 KB (+4,100 KB, +24 %)** | |

The budget saves 4 MB on maps that do not show at this size and spends 8 MB on the crate colour. Pickups are now at
306–356 px/m (twice as sharp as under the cap) and crates at 414–500 px/m, in line with the walls. The wall and trim
sheets stay at 2048 px. The decal copy (`T_Decals_Cutout`) is imported at 1024 px from its 2000 px source. Raw data:
`Docs/Performance/textures.csv`. `Restore Prop Textures To 2048` puts the kit defaults back.

**LOD culling of small props** (`Tools > World Builder > Add LOD Culling To Small Props`). 27 props under 0.8 m got a
LOD Group that hides them beyond about 25 m (cull height = size / (2 · 25 m · tan(FOV/2))). Measured gain: none worth
reporting (at most 2 triangles in these views). The level's longest sightlines are about 20–25 m, so almost no small
prop is ever that far away while visible, and occlusion culling already removes the ones behind walls. The groups
are kept because they cost almost nothing and only help on the longest views; in a larger level this technique would
matter more.

## Quality checks

`Tools > World Builder > Quality Report` (`Assets/Scripts/Editor/QualityReport.cs`) checks the lighting and texturing
with numbers. It writes CSV files to `Docs/Quality` and a summary to the Console. Last run on 6 November 2026, after
the final bake. The Scene view screenshots in the same folder show the same checks visually.

| Check | Method | Result |
|---|---|---|
| Lightmap UVs | every lightmapped renderer has a second UV set (lightmap UVs); its Scale In Lightmap | 277 of 277 have lightmap UVs. All use scale 1, so lightmap resolution is the same 20 texels/unit everywhere. |
| Lightmap UV overlap | each renderer's lightmap UVs laid onto its real lightmap texels; a texel touched by two UV islands is shared | 24 props found and fixed (below). Now 0. |
| Seams and blotches | Scene view, Baked Lightmap mode (`baked_lightmap.png`) | none. Light falls off smoothly across wall-piece joins. |
| Texel density | texture pixels per metre on each surface: sqrt(UV area / world area) × texture width | walls and floors 484–514 px/m (room medians); props fixed (below) |
| Light probes | every probe compared with its neighbours within 3 m; 2,744 walkable points sampled from the NavMesh at 1 m height | 346 probes, 0 inside geometry. No walkable point is more than 1.8 m from a probe. |

The 55 greybox Cube walls and floors under the kit art are disabled and not counted.

**Texel density of the structure, per room** (median px/m; the structure median is 490):

| Armoury | Control room | Corridors | Infirmary | Maintenance bay | Quarantine cell | Research lab | Service corridor |
|---|---|---|---|---|---|---|---|
| 494 | 514 | 490 | 494 | 484 | 514 | 494 | 490 |

Every room is within about 6 % of the others, so no room looks blurrier than the next. The only outliers are the three
`GapFiller` cubes (1,070–1,529 px/m), which sit inside wall gaps and are never seen.

### Found and fixed

1. **Overlapping lightmap UVs on small props.** The Scene view's UV Overlap mode showed red on the barrels, the guns
   on the armoury wall and the vents (`uv_overlap_before.png`). The report found 24 renderers whose UV islands share
   lightmap texels:

   | Props | Shared texels |
   |---|---|
   | wall guns (rifle, sniper, pistol, revolver) | 28–84 % |
   | health pack tube, syringe | 20–28 % |
   | barrels | 12 % |
   | vents | 1–4 % |
   | specimen tank glow strips and pipes | under 1 % |

   At 20 texels/unit a small prop gets only a few texels per face, so the gaps between its UV islands become
   narrower than one texel, and baked light bleeds from one face onto another.
   **Fix:** `Tools > World Builder > Light Overlapping Props With Probes`. These props keep Contribute GI, so they
   still block and bounce light in the bake, but receive their own light from the light probes, which cover the whole
   level (check above). After the rebake: 0 overlaps (`uv_overlap_after.png`).
2. **Blurry pickups and crates.** The texel density check showed ammo boxes and health packs at 153–178 px/m and
   cover crates at 207–250 px/m, against 490 on the walls. The first was caused by my own 1024 px texture cap.
   **Fix:** the texture budget in Performance. Pickups are now at 306–356 px/m and crates at 414–500 px/m.

### Checked and accepted

| Item | px/m | Why it stays |
|---|---|---|
| Barrels | 260–278 | the kit ships this texture at 1024 px, so there is nothing to raise |
| Door panels (`Door_DarkMetal`) | 276 | plain dark metal with little detail; reads cleanly in play |
| Hazard-stripe decals | 190 | plain stripes with no detail to lose; the 1024 px copy keeps memory low |
| Vents and access panels | 2,000–2,478 | small parts of a trim sheet. Extra density costs nothing, because mipmaps pick the right size. |
| Ceiling fixtures and wall tops in Baked Lightmap mode | | shown dark because they face up, where no light reaches; the player never sees them |

## Design decisions

- **4 m modular grid:** it matches the MegaKit wall length, keeps every piece snapped, and gives the Facility
  AI a level graph with one node per tile.
- **Walk on the greybox colliders, not the art:** the kit floors have no clean colliders. Keeping one box per
  floor slab gives the player and the NavMesh a flat, reliable surface.
- **Almost everything Baked:** the target includes Macs, so lighting is precomputed. Only the four
  shadowless `AlienGlow` lights run in real time, plus the alarm beacons during a peak.
- **One ceiling collider:** a single box instead of one collider per tile is cheaper and leaves no seams.
- **Occludee-only props:** small props would make poor occluders and only add bake time.
- **Moving props out of the NavMesh:** barricades carve their own obstacle when they stop, so robots path
  around them wherever the player leaves them.

## Known limits

None known.
