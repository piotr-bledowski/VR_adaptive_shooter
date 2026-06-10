# VR Adaptive Shooter

A **Meta Quest VR shooting range** with ML-Agents reinforcement learning for adaptive target spawning and player-flow optimisation.

Built for the *Deep Learning Methods in Vision Systems and Virtual Reality* course at AGH UST.

---

## Tech stack

| Layer | Technology |
|-------|-----------|
| Engine | Unity 2022.3.62f3 |
| VR SDK | Meta XR SDK 201.0.0 / Oculus XR Plugin 4.5.4 |
| RL framework | Unity ML-Agents 2.0.1 |
| Training runtime | Python 3.10 · mlagents 0.30.0 · PyTorch 2.1.2 |
| Target platform | Meta Quest (Android / APK) |

---

## Project structure

```
VR_adaptive_shooter/
├── Assets/
│   ├── Scenes/              # Shooter.unity (main), ShooterTraining.unity (ML)
│   ├── Scripts/             # C# game logic + ML-Agents integration
│   ├── Materials/           # PBR materials for the shooter scene
│   ├── Prefabs/             # Runtime prefabs (targets, bullets, UI)
│   ├── Low Poly Guns/       # Third-party gun asset pack (models + textures)
│   ├── ML-Agents/           # Training timer / config assets
│   ├── Oculus/              # OculusProjectConfig
│   ├── Plugins/Android/     # Custom AndroidManifest.xml
│   ├── Resources/           # Meta XR runtime settings
│   └── XR/                  # Oculus XR loader & settings
│
├── config/                  # ML-Agents PPO+LSTM trainer config (shooter_flow.yaml)
├── python/                  # Explainability script + requirements.txt
├── Packages/                # Unity package manifest
└── ProjectSettings/         # Unity project settings
```

### Gitignored / not in version control

The following are excluded from git and must be recreated locally:

| Path | Why excluded | How to get it |
|------|-------------|---------------|
| `Library/` | Unity import cache (~10 GB) | Auto-generated when you open the project in Unity |
| `Temp/` | Unity build temp | Auto-generated |
| `Logs/` | Unity editor logs | Auto-generated |
| `UserSettings/` | Local editor layout | Auto-generated |
| `venv/` | Python virtual env | `pip install -r python/requirements.txt` |
| `venv_ml/` | ML-Agents training env (~1.35 GB) | See **ML training setup** below |
| `results/` | Trained model weights (`.pt` / `.onnx`) | Run training (see below) or request from team |
| `*.apk` | Android builds | Build via Unity → Build Settings → Android |
| `Assets/Archive/` | Archived Seal + TestScene assets | Not needed for the shooter scene |

---

## Getting started

### 1. Open in Unity

1. Install **Unity 2022.3.62f3** (use Unity Hub).
2. Clone the repo and open `VR_adaptive_shooter/` as a Unity project.
3. Unity will reimport all assets on first open — this takes a few minutes.
4. Install the **Meta XR SDK** package if prompted (already listed in `Packages/manifest.json`).

### 2. Build for Meta Quest

1. Go to **File → Build Settings**, select Android.
2. Ensure `Scenes/Shooter` is the active scene.
3. Connect your Quest via USB or enable wireless debugging.
4. Click **Build and Run**.

### 3. ML training setup

Install the ML training environment (requires [conda](https://conda.io)):

```powershell
# From the project root
conda create -n mgu_ml python=3.10.8 -y
conda activate mgu_ml
pip install -r python/requirements.txt
```

Then launch a training run from the project root with `mlagents-learn` pointing at the config:

```bash
mlagents-learn config/shooter_flow.yaml --run-id shooter_v2
```

Open Unity in **Play Mode** when prompted. Trained models are exported to `results/`.

### 4. ONNX model deployment

After training, export the `.onnx` model and place it in `Assets/ML-Agents/` (or wherever `ShooterModelManager` is configured to load it). See `docs/ML_AGENT_GUIDE.md` for the full export workflow.

---

## Key scenes

| Scene | Purpose |
|-------|---------|
| `Assets/Scenes/Shooter.unity` | **Main game** — VR lobby + adaptive shooting range |
| `Assets/Scenes/ShooterTraining.unity` | **Headless ML training** — same range, no VR rig |

---

## Key scripts

| Script | Role |
|--------|------|
| `Scripts/ShooterRoundManager.cs` | Round lifecycle, scoring, difficulty |
| `Scripts/ShooterTargetManager.cs` | Spawn pool and target placement |
| `Scripts/ML/ShooterFlowAgent.cs` | PPO+LSTM RL agent driving adaptive spawning |
| `Scripts/ML/PlayerSkillProfile.cs` | Per-player accuracy / reaction metrics |
| `Scripts/ML/ShooterModelManager.cs` | Loads and switches ONNX models at runtime |
| `Scripts/ShooterPlayerController.cs` | VR player movement and input |
| `Scripts/ShooterGun.cs` | Gun mechanics, fire rate, reload |
| `Scripts/Editor/ShooterSceneSetup.cs` | Editor tool — rebuild Shooter scene hierarchy |
| `Scripts/Editor/TrainingSceneSetup.cs` | Editor tool — rebuild ShooterTraining scene |

---

## Archive

`Assets/Archive/` (gitignored) contains the **Seal scene** and **TestScene** together with all their exclusive assets (terrain, water materials, obstacle prefabs, kluch model, VRGrabSystem, etc.). These scenes are not part of the shooter workflow and are stored locally only.
