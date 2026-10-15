# Systems Engineer – Player Interaction Physics

Module: SE3032 Graphics and Visualization. Role: Student 2 (The Systems Engineer).
Scope: physics and scripts that let the player move barricades, open doors and throw objects.

## Setup (once, in Unity 6000.3.25f1)
1. Pull `dev`, open the project, let Unity import the new scripts (commit the generated `.meta` files).
2. **GV Game > Systems Setup > Setup Prop Prefabs** – adds Rigidbody + `PhysicsProp` to every `Barricade_*` and `Throwable_*` prefab (layer `DynamicProps`, non-static).
3. Open your scene, set door size if needed, then **Build Doors From Markers**. Every `Door_*` marker gets a hinge, leaf, and AI auto-open trigger.
4. Select the player (needs a `CharacterController`) and press **Add Interaction To Selected Player**.
5. Optional: **Build Systems_Test Scene** creates `Assets/Scenes/Dev/Systems_Test.unity` with a player, door, barricades and throwables to test in isolation.

## Controls
E interact (doors) · F pick up / drag / drop · Q throw · Mouse wheel hold distance.

## Design decisions
- **Doors use a kinematic Rigidbody moved with `MoveRotation`** instead of a HingeJoint motor: deterministic, no jitter or sag, still pushes dynamic bodies, and angle limits are explicit (`openAngle`). A closing door that touches a dynamic body or the player swings back open instead of crushing it.
- **Doors on the `Doors` layer** (already excluded from the NavMesh bake), so baked navigation is unchanged; `DoorAutoOpenTrigger` opens doors for any `NavMeshAgent`.
- **Held props are driven by velocity, not parented**, so they still collide with walls; follow speed is scaled by mass so barricades feel heavy. Props are dropped if they get stuck behind geometry (`breakDistance`).
- **Throwing uses `ForceMode.VelocityChange`** so a mug and an ammo box fly at the same speed regardless of mass. Throwables use `ContinuousDynamic` collision detection to avoid tunnelling; barricades use `Discrete`. Anything above `maxThrowMass` is simply dropped.
- **Barricades carry a carving `NavMeshObstacle`** (`carveOnlyStationary`) so enemies path around a barricade after it has been dragged to a new spot, without recarving every frame while it moves.
- **`InteractionNoise`** event: impacts and doors announce themselves so the IS module can make enemies investigate noise.
- **Input actions are created in code**, not added to the shared `InputSystem_Actions` asset, to avoid merge conflicts.

## Optimization notes
- Primitive/box colliders only on props (no MeshColliders on dynamic bodies).
- Props are non-static and excluded from the lightmap bake; Rigidbodies sleep (`sleepThreshold`) when settled.
- Impact noise has a cooldown and speed threshold so resting piles do not spam events.
- NavMesh carving only when stationary.

## Known limits
- Door leaf is a placeholder cube unless a model (e.g. Quaternius `Door_Simple`) is assigned in the setup window.
- Door opening is assumed to run along the marker's local X; rotate the marker 90° for doors in north-south walls.
- `Systems_Test` uses its own minimal player; the real player comes from the Core Developer.

---

# Update: player, combat, health and two-panel doors

## New scripts
| Folder | Script | Purpose |
|---|---|---|
| `Scripts/Player` | `PlayerController` | CharacterController FPS: WASD, mouse look, sprint, jump, crouch (hold Ctrl). Speed capped by `PlayerInteractor.MaxCarrySpeed`. Footsteps raise noise on `InteractionNoise` (sprint loud, crouch silent). |
| `Scripts/Player` | `PlayerHealth` | Implements `IDamageable`, `IHealable` and the Facility AI's `IPlayerStatusSource` (`Health01`). Death disables movement and shooting; R respawns. |
| `Scripts/Player` | `PlayerHud` | IMGUI health bar, ammo, damage flash, hit marker, death screen. |
| `Scripts/Combat` | `IDamageable`, `IHealable`, `DamageInfo` | Shared interfaces. Robots implement `IDamageable` so bullets can hurt them, and call `PlayerHealth.TakeDamage` to hurt the player. |
| `Scripts/Combat` | `WeaponController` | Hitscan weapon: LMB fire (auto), R reload, spread, recoil, tracer, muzzle flash, pushes props, raises a gunshot noise on `InteractionNoise`. |
| `Scripts/Combat` | `WeaponPickup`, `TrainingDummy` | Armoury pickup trigger; shootable test target. |
| `Scripts/Interaction` | `HealthPickup` | Heals anything `IHealable` that needs healing. |

## Controls
WASD move, mouse look, Shift sprint, Space jump, hold Ctrl crouch, LMB fire, R reload (respawn when dead),
E interact (doors), F pick up / drag / drop, hold Q to charge a throw, mouse wheel hold distance.

## Doors (issue #11)
- One `DoorController` per `Door_*` marker (the Facility AI adapter finds it with `GetComponentInChildren`).
- Two hinged leaves per door (`Door_DarkMetal`, 2.1 x 4.05 x 0.2 m, hinges at the outer edges of the 4 m opening).
- Each leaf is independent: a barricade blocking one leaf does not stop the other, so a door can never fully lock the player out.
- Swing direction is computed from each leaf's geometry and the opener's side (always away from the opener).
- Per leaf: light prop (<= 10 kg) is shoved aside slowly; heavy movable prop (<= 120 kg) stops the leaf, and walking into it forces it open slowly up to 60 degrees; the player or anything heavier stops the leaf. Closing never forces a heavy prop.
- NavMesh: closed doors do NOT carve by default (`blockNavMeshWhenClosed`), because the Facility AI blocks LOCKED doors with its own barrier and robots open unlocked doors. Switch it on if the team decides every closed door should block.

## Design decisions (combat and player)
- **Hitscan, not projectiles**: instant, cheap, deterministic, and easy for AI to reason about; bullet drop adds nothing to a short-range facility.
- **Ignore own colliders by root**: the shot ignores the player's hierarchy so the weapon can never hit its owner.
- **`IDamageable` / `IHealable` interfaces**: the player, robots and dummies share one damage path, and health packs work with any healable.
- **Gunshots and footsteps on `InteractionNoise`**: the same event bus that carries door and prop noise, so the Security Hunter hears everything the player does.
- **IMGUI HUD**: no Canvas or font assets to merge, easy to replace later.
- **Respawn instead of scene reload**: reloading a scene that is not in Build Settings fails in the editor, and respawn keeps the level state.
