# GV-Game

An interactive 3D shooting game built in Unity for the **SE3032 – Graphics and Visualization** group assignment at SLIIT.

> **Theme:** Lab Lockdown
> **Status:** In development

---

## Team

| Name | Student ID | Role | Responsibilities |
|------|-----------|------|------------------|
| _Name_ | _IT24103443_ | World Builder | Level design, textures, lighting bake, NavMesh bake |
| _Name_ | _ITxxxxxxxx_ | Systems Engineer | Physics interactions: doors, barricades, throwables |
| _Name_ | _ITxxxxxxxx_ | Core Developer | Custom 3D models (Blender), import pipeline, player systems |
| _Name_ | _ITxxxxxxxx_ | Agent Controller | AI agent movement, animation, IS module path integration |

---

## Tech Stack

| Tool | Version / Notes |
|------|-----------------|
| Unity | **6000.3.25f1 (Unity 6.3 LTS)** – everyone must use this exact version |
| Render pipeline | Universal Render Pipeline (URP) |
| Navigation | AI Navigation package (NavMesh) |
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
git clone https://github.com/<owner>/GV-Game.git
```

In Unity Hub, click **Add > Add project from disk** and select the cloned `GV-Game` folder. The first open takes a few minutes while Unity rebuilds the `Library` folder.

---

## Project Structure

```
GV-Game/
├── Assets/
│   ├── Art/
│   │   ├── Models/        # Exported FBX files (custom + third-party)
│   │   ├── Materials/
│   │   └── Textures/
│   ├── Audio/
│   ├── Prefabs/
│   ├── Scenes/
│   │   ├── Main.unity     # Integrated game scene
│   │   └── Dev/           # One personal scene per team member
│   ├── Scripts/
│   └── ThirdParty/        # Imported store/free assets, kept unmodified
├── Blender/               # .blend source files (outside Assets on purpose)
├── Packages/
└── ProjectSettings/
```

`.blend` source files live in `Blender/` and are exported to FBX in `Assets/Art/Models/`. This keeps imports reliable on machines without Blender installed.

---

## Workflow Rules

### Branching

- `main` always holds a working, buildable project.
- Create a branch per feature and merge through a pull request:

```bash
git switch main
git pull
git switch -c feature/door-physics
```

- Branch prefixes: `feature/`, `fix/`, `art/`, `docs/`

### Scenes

- Work in your own scene under `Assets/Scenes/Dev/` and share work as **prefabs**.
- Only **one person at a time** edits `Main.unity`. Announce it in the group chat before you start.

### Commits

- Pull before you start working, and commit small and often.
- Write clear messages in the imperative form, for example:
  - `Add door hinge physics with angle limits`
  - `Bake NavMesh for dock area`
  - `Import custom flintlock model with UVs`
- Always commit `.meta` files together with their assets.
- Save the scene and project in Unity before committing.

### Large files

Binary assets (`.fbx`, `.png`, `.wav`, etc.) are tracked by Git LFS through `.gitattributes`. Do not commit screen recordings, the demo video, or build output to this repo.

---

## Custom Models

Models created from scratch by the team in Blender:

| Model | Author | Polycount | Notes |
|-------|--------|-----------|-------|
| _Model 1_ | _Name_ | _–_ | _–_ |
| _Model 2_ | _Name_ | _–_ | _–_ |

---

## Controls

| Action | Key |
|--------|-----|
| Move | _WASD_ |
| Look | _Mouse_ |
| Shoot | _Left click_ |
| Interact | _E_ |
| Throw | _–_ |

---

## Building

1. Open **File > Build Profiles**.
2. Select **Windows** or **macOS** and make sure `Main.unity` is in the scene list.
3. Click **Build** and choose a folder **outside** the repo.

---

## Third-Party Assets and Credits

| Asset | Source | License | Used for |
|-------|--------|---------|----------|
| _Asset name_ | _Link_ | _CC0 / Standard Unity Asset Store EULA_ | _–_ |

---

## Academic Use

This project was developed for academic assessment at SLIIT. Third-party assets remain the property of their respective creators.
