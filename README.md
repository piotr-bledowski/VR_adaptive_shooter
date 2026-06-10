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

## Adaptive difficulty system

Difficulty adapts in real time using a tabular Q-learning agent per player. No Python or neural network is involved.

**State space:** 9 states (3 accuracy buckets × 3 reaction-time buckets).  
**Actions:** 7 spawn presets from `VeryEasy` (3 stationary targets, slow spawn) to `VeryHard` (10 targets, fast erratic mix).  
**Reward:** peaks when hit rate ≈ 55 % and avg time-to-hit ≈ 2.5 s (flow state).

Each round:

1. `AdaptiveSpawnController.OnRoundStart()` — ε-greedy action selects a preset.
2. Every 5 s mid-round — emergency ease/push if hit rate < 15 % or > 85 %.
3. `AdaptiveSpawnController.OnRoundEnd()` — Bellman update, EMA metrics, profile saved to disk.

New players are seeded from a pre-trained base profile (`_base_<skill>.json`) so the agent starts in a reasonable region of the Q-table from round 1.

---

## ML workflow

### Step 1 — train base profiles (offline)

**MGU → Create Training Scene** then press Play.

Runs three headless environments (Naive / Average / Expert synthetic players) at 6× time scale. After 200 rounds each, saves:

```
Application.persistentDataPath/shooter_profiles/
  _base_beginner.json
  _base_intermediate.json
  _base_advanced.json
```

### Step 2 — verify online adaptation (simulation)

**MGU → Create Online Sim Scene** then press Play.

Each environment loads the corresponding base profile — exactly as a new player would — then runs 25 rounds at 8× time scale. The profile is saved after every round (identical to live gameplay). Console logs show preset selection, hit rate, and reward per round.

Session reports are written to:

```
Application.persistentDataPath/shooter_sim_reports/
  sim_<skill>_<timestamp>.json
```

### Step 3 — play

Open `Shooter.unity`, connect a Quest, Build and Run. New players select their skill level once; their profile initialises from the matching base file and adapts from there.

---

## Key scripts

| Script                                  | Role                                                      |
| --------------------------------------- | --------------------------------------------------------- |
| `Scripts/ML/PlayerSkillProfile.cs`      | Per-player Q-table, EMA metrics, persistence              |
| `Scripts/ML/AdaptiveSpawnController.cs` | Maps Q-actions to spawn presets; drives mid-round checks  |
| `Scripts/ML/TrainingRoundController.cs` | Automated training loop for base profile generation       |
| `Scripts/ML/OnlineSimController.cs`     | Simulates a real player session to test online adaptation |
| `Scripts/ML/SyntheticPlayer.cs`         | Probabilistic shooter for headless environments           |
| `Scripts/ShooterRoundManager.cs`        | Round state machine (Idle → Active → Results)             |
| `Scripts/ShooterTargetManager.cs`       | Target pool, spawning, respawn scheduling                 |
| `Scripts/ShooterGun.cs`                 | VR trigger → bullet → hit detection                       |
| `Scripts/ShooterTarget.cs`              | Stationary / Moving / Erratic target behaviour            |
| `Scripts/Editor/ShooterSceneSetup.cs`   | Rebuilds Shooter.unity from scratch                       |
| `Scripts/Editor/TrainingSceneSetup.cs`  | Rebuilds ShooterTraining.unity                            |
| `Scripts/Editor/OnlineSimSceneSetup.cs` | Builds ShooterOnlineSim.unity                             |

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
config/              ML-Agents YAML (unused — kept for reference)
python/              ONNX explainability script + requirements.txt
Packages/            Unity package manifest
ProjectSettings/     Unity project settings
```
