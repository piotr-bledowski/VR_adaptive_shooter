# VR Adaptive Shooter

Meta Quest VR shooting range with adaptive difficulty driven by online Q-learning.
Built for the _Deep Learning Methods in Vision Systems and Virtual Reality_ course at AGH UST.

---

## Stack

| Layer               | Technology                                   |
| ------------------- | -------------------------------------------- |
| Engine              | Unity 2022.3.62f3                            |
| VR SDK              | Meta XR SDK 201.0.0 / Oculus XR Plugin 4.5.4 |
| Adaptive difficulty | Tabular Q-learning (pure C#, no Python)      |
| Target platform     | Meta Quest (Android)                         |

---

## Getting started

1. Install **Unity 2022.3.62f3** via Unity Hub.
2. Open `VR_adaptive_shooter/` as a Unity project. First import takes a few minutes.
3. Build target: **Android** → device **Meta Quest**.
4. Open `Assets/Scenes/Shooter.unity` and hit **Build and Run**.

---

## Scenes

| Scene                    | Purpose                                                                 |
| ------------------------ | ----------------------------------------------------------------------- |
| `Shooter.unity`          | Main game — VR lobby + adaptive shooting range                          |
| `ShooterTraining.unity`  | Offline Q-table training — run in Play mode to produce base profiles    |
| `ShooterOnlineSim.unity` | Online-loop simulation — verifies how difficulty adapts per player type |

---

## Target system

Targets are bullseye shields with 4 concentric scoring zones:

| Zone       | Points | Normalized distance |
| ---------- | ------ | ------------------- |
| Bullseye   | 10     | < 0.15              |
| Inner ring | 5      | 0.15 – 0.40        |
| Middle     | 2      | 0.40 – 0.70        |
| Outer      | 1      | ≥ 0.70              |

Targets come in 3 movement types: **Stationary**, **Moving** (linear path), **Erratic** (3D Lissajous).

### Rotation

Any target can optionally rotate around its Y-axis, making it periodically face edge-on to the player. Three rotation speeds:

| Speed  | °/sec |
| ------ | ----- |
| Slow   | 45    |
| Medium | 120   |
| Fast   | 240   |

### Sequential spawning

Targets spawn one at a time with a configurable delay (1–6 seconds). Each target has a 5-second lifespan. The Q-learning agent controls the type, rotation, and spawn pace per round.

---

## Adaptive difficulty system

Difficulty adapts in real time using a tabular Q-learning agent per player. No Python or neural network is involved.

**State space:** 27 states (3 performance buckets × 3 pace buckets × 3 weakest-type indicators).
The performance bucket combines overall hit rate and average points per target. The weakest-type indicator identifies which of the three target types the player struggles with most.

**Actions:** 36 parameterized actions encoding:
- Emphasis type (Stationary / Moving / Erratic) — which type to spawn most
- Rotation level (None / Slow / Medium / Fast)
- Spawn pace (Fast 1-2s / Medium 3-4s / Slow 5-6s)

**Reward:** peaks when hit rate ≈ 55 %, avg time-to-hit ≈ 2.5 s, avg points/target ≈ 4, and per-type hit rates are balanced.

**Per-type stats as input:** The model tracks separate hit rates, time-to-hit, and average points for each target type (stationary, moving, erratic) plus a rotating-targets aggregate. High performance on a type signals the agent to spawn that type less and try more challenging ones.

Each round:

1. `AdaptiveSpawnController.OnRoundStart()` — ε-greedy action selects emphasis, rotation, and pace.
2. Every 5 s mid-round — emergency ease/push if hit rate < 15 % or > 85 %.
3. `AdaptiveSpawnController.OnRoundEnd()` — Bellman update with per-type metrics, profile saved.

New players are seeded from a pre-trained base profile (`_base_<skill>.json`) so the agent starts in a reasonable region of the Q-table from round 1.

### Explainability

After each round the HUD shows:
- Key learning statistics (hit rate, TTH, points per target, per-type breakdowns)
- Agent update explanation (e.g., "Target emphasis: Stationary → Moving", "Rotation: None → Slow rotation")

Training reports include the same data in JSON and CSV formats for offline analysis.

---

## ML workflow

### Step 1 — train base profiles (offline)

**MGU → Create Training Scene** then press Play.

Runs three headless environments (Naive / Average / Expert synthetic players) at 6× time scale. After 200 rounds each, saves base profiles and training reports:

```
Application.persistentDataPath/
  shooter_profiles/
    _base_beginner.json
    _base_intermediate.json
    _base_advanced.json
  shooter_training_reports/
    training_<skill>_<timestamp>.json
    training_<skill>_<timestamp>.csv
```

### Step 2 — plot training reports

```bash
pip install matplotlib
python python/plot_training.py <path_to_csv_or_directory>
```

Generates PNG plots showing reward, hit rate, per-type performance, action selection, epsilon decay, and more.

### Step 3 — verify online adaptation (simulation)

**MGU → Create Online Sim Scene** then press Play.

Each environment loads the corresponding base profile — exactly as a new player would — then runs 25 rounds at 8× time scale. Session reports are written in JSON and CSV.

### Step 4 — play

Open `Shooter.unity`, connect a Quest, Build and Run. New players select their skill level once; their profile initialises from the matching base file and adapts from there.

---

## Key scripts

| Script                                  | Role                                                             |
| --------------------------------------- | ---------------------------------------------------------------- |
| `Scripts/ML/PlayerSkillProfile.cs`      | Per-player Q-table (27×36), per-type EMAs, persistence           |
| `Scripts/ML/AdaptiveSpawnController.cs` | Maps Q-actions to spawn params; explainability strings           |
| `Scripts/ML/TrainingRoundController.cs` | Automated training loop with JSON+CSV report generation          |
| `Scripts/ML/OnlineSimController.cs`     | Simulates a real player session to test online adaptation        |
| `Scripts/ML/SyntheticPlayer.cs`         | Probabilistic shooter with rotation-aware accuracy               |
| `Scripts/ShooterRoundManager.cs`        | Round state machine + per-type shot attribution                  |
| `Scripts/ShooterTargetManager.cs`       | Sequential target spawning, lifespan management                  |
| `Scripts/ShooterTarget.cs`              | Target behaviour: movement, Y-axis rotation, 4-zone scoring     |
| `Scripts/ShooterStats.cs`              | Per-type statistics (hits, shots, TTH, points, expired)          |
| `Scripts/ShooterHUD.cs`                | VR HUD with round results and agent explainability               |
| `Scripts/Editor/ShooterSceneSetup.cs`   | Rebuilds Shooter.unity from scratch                              |
| `Scripts/Editor/TrainingSceneSetup.cs`  | Rebuilds ShooterTraining.unity                                   |
| `Scripts/Editor/OnlineSimSceneSetup.cs` | Builds ShooterOnlineSim.unity                                    |
| `python/plot_training.py`               | Training report plotter (matplotlib)                             |

---

## Project structure

```
Assets/
├── Scenes/          Shooter, ShooterTraining, ShooterOnlineSim
├── Scripts/         C# game logic + ML
│   ├── ML/          Q-learning, synthetic player, simulation
│   └── Editor/      Scene-builder menu items
├── Materials/       Shooter-scene PBR materials
├── Prefabs/         ShooterTarget, ShooterBullet, HUD entries
├── Low Poly Guns/   14 weapon models + PBR textures
├── Oculus/          OculusProjectConfig
├── Plugins/Android/ AndroidManifest.xml
├── Resources/       Meta XR runtime settings
└── XR/              Oculus loader + settings
python/              Training report plotter
Packages/            Unity package manifest
ProjectSettings/     Unity project settings
```
