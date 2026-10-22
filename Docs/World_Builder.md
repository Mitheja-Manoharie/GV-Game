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
- **Control room:** consoles in the south corners, with screens facing the room. Low crates frame the exit.

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
    dynamic props, robots and the player.
  - **Reflection probes:** 14 Baked, box-projected probes, one per floor area.
- **Readability:** every room was reviewed after a preview bake.
  - The maintenance bay was too dark and too red to see robots. It got white fill lights and a lighter
    strip of floor tiles, and Indirect Intensity was raised to 1.3 and Albedo Boost to 1.5.
  - Measured average probe brightness puts the control room highest (0.19) and the quarantine cell lowest
    (0.03), as intended.

**Post-processing** (`Global Volume`): Neutral tonemapping, Bloom (threshold 1, intensity 0.4),
contrast +15, Vignette 0.25.

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
