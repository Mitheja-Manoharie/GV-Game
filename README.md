# GV-Game

An interactive 3D shooting game built in Unity for the **SE3032 – Graphics and Visualization** group assignment at SLIIT. The AI agents also serve as the team's **SE3062 – Intelligent Systems** project.

> **Theme:** Lab Lockdown
> **Status:** In development. The `Main` scene is integrated with the final lighting, occlusion and NavMesh bake, the player systems, physics doors and props, and the Facility AI director.

You are trapped in a research facility in lockdown. Armed with a rifle, move barricades and throw objects to get past the facility's robots, and reach the exit while the facility decides where to send enemies and which doors to lock.

---

## Team

| Name | Student ID | Role | Responsibilities |
|------|-----------|------|------------------|
| Manoharie J A M | IT24103443 | World Builder | Level design, textures, lighting bake, NavMesh bake · Facility AI (IS) |
| Abeyweera A M P V T C | IT24103428 | Systems Engineer | Physics interactions: doors, barricades, throwables · player, weapon and health · Security Hunter AI (IS) |
| B G R V Senanayake | IT24103456 | Core Developer | Custom 3D character models (Blender), import pipeline · Drone Stalker AI (IS) |
| Oshan D L D A | IT24103473 | Agent Controller | Robot agent body, prefabs and spawner · Tactical Flanker AI (IS) |

---

## Tech Stack

| Tool | Version / Notes |
|------|-----------------|
| Unity | **6000.3.25f1 (Unity 6.3 LTS)** – everyone must use this exact version |
| Render pipeline | Universal Render Pipeline (URP) 17.3 |
| Navigation | AI Navigation 2.0 (NavMesh) |
| Input | Input System 1.20 (actions created in code) |
| Modelling | Blender |
| Version control | Git + Git LFS, hosted on GitHub |
| Code editor | Visual Studio Code or JetBrains Rider |

---

## Getting Started

### Prerequisites

- Unity Hub with **Unity 6000.3.25f1** and **Windows Build Support (Mono)** installed
- Git and Git LFS

On macOS:

```bash
brew install git-lfs
git lfs install
git config --global core.autocrlf input
```

On Windows, install [Git for Windows](https://git-scm.com/) (includes Git LFS), then run `git lfs install`.

Make sure your Git email matches your GitHub account so commits are credited to you:

```bash
git config --global user.name "Your Name"
git config --global user.email "your-github-email@example.com"
```

### Clone and open

```bash
git lfs install
git clone https://github.com/Mitheja-Manoharie/GV-Game.git
cd GV-Game
git switch dev
```

In Unity Hub, click **Add > Add project from disk** and select the cloned `GV-Game` folder. The first open takes a few minutes while Unity rebuilds the `Library` folder.

Open `Assets/Scenes/Main.unity` and press **Play**.

---

## Controls

| Action | Key |
|--------|-----|
| Move | WASD |
| Look | Mouse |
| Sprint | Left Shift |
| Crouch (silent footsteps) | Left Ctrl |
| Jump | Space |
| Shoot | Left click |
| Reload | R |
| Interact (doors) | E |
| Pick up / drag / drop prop | F |
| Throw held prop | Q |
| Move held prop nearer / further | Mouse wheel |
| Restart after death | R |
| Release / lock cursor | Esc / Left click |
| First / third-person view (shows the Alien body) | V |

---

## Gameplay Systems

| System | Scripts | Notes |
|--------|---------|-------|
| Player | `Player/PlayerController`, `PlayerHealth`, `PlayerHud` | First-person controller on a `CharacterController`, 100 HP, HUD and restart |
| Weapon | `Combat/WeaponController`, `WeaponPickup`, `IDamageable` | Hitscan rifle the player starts with (the armoury pickup adds ammo), 25 damage, 12-round magazine |
| Doors | `Interaction/DoorController`, `DoorAutoOpenTrigger` | Two-panel kinematic doors with angle limits; open automatically for NavMesh agents; can be locked by the Facility AI |
| Props | `Interaction/PhysicsProp`, `PlayerInteractor`, `PlayerRigidbodyPusher` | Barricades (heavy, carve the NavMesh) and throwables (mugs, ammo boxes) |
| Pickups | `Interaction/HealthPickup` | Health packs heal 40 HP |
| Noise | `Interaction/InteractionNoise` | Doors, impacts, gunshots and footsteps raise noise events the AI can hear |

Design notes for the interaction physics are in [Docs/Systems_Engineer.md](Docs/Systems_Engineer.md).

---

## AI Agents

Each agent keeps its decision logic in a `Core` assembly with no Unity references, so every algorithm can be unit tested outside the engine.

| Agent | Owner | Technique | Status | Docs |
|-------|-------|-----------|--------|------|
| Facility AI ("Lockdown Director") | Mitheja | Utility-based intensity model + CSP (backtracking, MRV, forward checking) to pick spawn points and doors to lock | In `Main`, spawning robots | [FacilityAI.md](Docs/FacilityAI.md) |
| Security Hunter ("Sound Hunter") | Viduna | Probabilistic belief map from sound + A* with live door costs | Core and tests on `dev` | [HunterAI.md](Docs/HunterAI.md), [HunterWeights.md](Docs/HunterWeights.md) |
| Drone Stalker | Ravindu | Utility tactics + A* with light and line-of-sight costs | Feature branch | – |
| Tactical Flanker | Alen | STRIPS/GOAP planner with A* over actions and a threat map | Feature branch | – |

### Running the tests

Open **Window > General > Test Runner**, select **EditMode** and click **Run All**. The test assemblies are `LabLockdown.AI.Facility.Tests` and `LabLockdown.AI.Hunter.Tests`.

---

## Project Structure

```
GV-Game/
├── Assets/
│   ├── Art/Materials/      # Placeholder materials (doors, gun, tracer)
│   ├── Models/Characters/  # Custom Alien and Android FBX models
│   ├── Prefabs/
│   │   ├── Lighting/       # Light fixtures (white/red, small/wide/floor/corner/spot)
│   │   ├── Player/         # Player prefab
│   │   └── Props/          # Barricades, throwables, health pack
│   ├── Scenes/
│   │   ├── Main.unity      # Integrated game scene
│   │   ├── Main/           # Baked lightmaps, reflection probes, NavMesh, occlusion data
│   │   └── Dev/            # Personal and test scenes (e.g. Systems_Test)
│   ├── Scripts/
│   │   ├── AI/             # Facility, FacilityIntegration, Hunter (Core / Runtime / Tests)
│   │   ├── Combat/
│   │   ├── Interaction/
│   │   ├── Player/
│   │   ├── Dev/            # Test-only helpers
│   │   └── Editor/         # GV Game > Systems Setup menu tools
│   ├── Settings/           # URP assets and volume profiles
│   └── ThirdParty/Quaternius/  # MegaKit and Essentials, kept unmodified
├── Docs/                   # Design notes for the systems and AI agents
├── Packages/
└── ProjectSettings/
```

Keep `.blend` source files outside `Assets/` and export them to FBX in `Assets/Models/`. This keeps imports reliable on machines without Blender installed.

---

## Workflow Rules

### Branching

- `main` holds the submitted, buildable project. `dev` is the integration branch.
- Create a branch per feature from `dev` and merge it back through a pull request:

```bash
git switch dev
git pull
git switch -c feature/door-physics
```

- Branch prefixes: `feature/`, `fix/`, `art/`, `docs/`

### Scenes

- Work in your own scene under `Assets/Scenes/Dev/` and share work as **prefabs**.
- Only **one person at a time** edits `Main.unity`. Announce it in the group chat before you start.
- After changing level geometry in `Main`, rebake lighting, occlusion and the NavMesh and commit the files in `Assets/Scenes/Main/`.

### Commits

- Pull before you start working, and commit small and often.
- Write clear messages in the imperative form, for example:
  - `Add door hinge physics with angle limits`
  - `Bake NavMesh for dock area`
  - `Import custom android model with UVs`
- Always commit `.meta` files together with their assets.
- Save the scene and project in Unity before committing.

### Large files

Binary assets (`.fbx`, `.png`, `.exr`, `.wav`, etc.) are tracked by Git LFS through `.gitattributes`. Do not commit screen recordings, the demo video, or build output to this repo.

---

## Custom Models

Models created from scratch by the team in Blender:

| Model | Author | Polycount | Notes |
|-------|--------|-----------|-------|
| Android | Ravindu Senanayake | _–_ | Rigged 73-bone humanoid, PBR textures, red emission lights |
| Alien | Ravindu Senanayake | _–_ | Rigged 73-bone humanoid, base colour and normal textures |

---

## Building

1. Open **File > Build Profiles**.
2. Select **Windows** or **macOS** and make sure `Main.unity` is the first scene in the list.
3. Click **Build** and choose a folder **outside** the repo.

For Windows there is also **GV Game > Build > Windows**, which builds to `../GV-Game-Build/Windows/LabLockdown.exe` next to the repo. Without the Unity window:

```bash
Unity.exe -batchmode -quit -projectPath <project> -executeMethod GVGame.EditorTools.BuildTools.BuildWindows -buildPath <folder> -logFile build.log
```

The command exits with code 1 if the build fails.

---

## Third-Party Assets and Credits

| Asset | Source | License | Used for |
|-------|--------|---------|----------|
| Modular Sci-Fi MegaKit (Standard) by Quaternius | [quaternius.com](https://quaternius.com) | CC0 1.0 | Modular walls, columns, platforms, decals, props and alien models for the lab environment |
| Sci-Fi Essentials Kit (Standard) by Quaternius | [quaternius.com](https://quaternius.com) | CC0 1.0 | Weapons, ammo, enemy models and props such as crates, barrels and desks |

---

## Academic Use

This project was developed for academic assessment at SLIIT. Third-party assets remain the property of their respective creators.
