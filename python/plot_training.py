"""
Plot training reports from VR Adaptive Shooter.

Usage:
    python plot_training.py <csv_path_or_directory>

If a directory is given, all .csv files in it are plotted.
Reports are saved as PNG next to each CSV.
"""

import sys
import os
import csv
from pathlib import Path

try:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import matplotlib.gridspec as gridspec
except ImportError:
    print("matplotlib required: pip install matplotlib")
    sys.exit(1)


def load_csv(path):
    rows = []
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        for r in reader:
            rows.append({k: float(v) if k != "actionDesc" else v for k, v in r.items()})
    return rows


def col(rows, key):
    return [r[key] for r in rows]


def plot_report(csv_path):
    rows = load_csv(csv_path)
    if not rows:
        print(f"  Empty: {csv_path}")
        return

    rounds = col(rows, "round")
    name = Path(csv_path).stem

    fig = plt.figure(figsize=(18, 22))
    fig.suptitle(f"Training Report: {name}", fontsize=14, y=0.98)
    gs = gridspec.GridSpec(5, 2, hspace=0.35, wspace=0.25)

    # 1. Reward over rounds
    ax = fig.add_subplot(gs[0, 0])
    ax.plot(rounds, col(rows, "reward"), linewidth=0.8, alpha=0.5, label="per-round")
    window = min(20, len(rows))
    if window > 1:
        smoothed = running_avg(col(rows, "reward"), window)
        ax.plot(rounds[window - 1:], smoothed, linewidth=2, label=f"{window}-round avg")
    ax.axhline(0, color="gray", linestyle="--", linewidth=0.5)
    ax.set_xlabel("Round")
    ax.set_ylabel("Reward")
    ax.set_title("Flow Reward")
    ax.legend(fontsize=8)

    # 2. Overall hit rate + TTH
    ax = fig.add_subplot(gs[0, 1])
    ax.plot(rounds, col(rows, "hitRate"), label="Hit rate", linewidth=1.2)
    ax.axhline(0.55, color="green", linestyle="--", linewidth=0.5, label="Target (55%)")
    ax.set_ylabel("Hit Rate")
    ax.set_xlabel("Round")
    ax.set_title("Overall Hit Rate")
    ax.legend(fontsize=8)

    ax2 = ax.twinx()
    ax2.plot(rounds, col(rows, "avgTTH"), color="orange", linewidth=1.0, alpha=0.7, label="TTH")
    ax2.axhline(2.5, color="red", linestyle="--", linewidth=0.5)
    ax2.set_ylabel("Avg TTH (s)", color="orange")

    # 3. Per-type hit rates
    ax = fig.add_subplot(gs[1, 0])
    ax.plot(rounds, col(rows, "hrStat"), label="Stationary", linewidth=1)
    ax.plot(rounds, col(rows, "hrMov"), label="Moving", linewidth=1)
    ax.plot(rounds, col(rows, "hrErr"), label="Erratic", linewidth=1)
    ax.plot(rounds, col(rows, "hrRot"), label="Rotating", linewidth=1, linestyle="--")
    ax.set_xlabel("Round")
    ax.set_ylabel("Hit Rate")
    ax.set_title("Per-Type Hit Rates")
    ax.legend(fontsize=8)

    # 4. Points per target and per-type points per hit
    ax = fig.add_subplot(gs[1, 1])
    ax.plot(rounds, col(rows, "ppt"), label="Pts/target (overall)", linewidth=1.5)
    ax.plot(rounds, col(rows, "pphStat"), label="Pts/hit stat", linewidth=0.8, alpha=0.7)
    ax.plot(rounds, col(rows, "pphMov"), label="Pts/hit mov", linewidth=0.8, alpha=0.7)
    ax.plot(rounds, col(rows, "pphErr"), label="Pts/hit err", linewidth=0.8, alpha=0.7)
    ax.axhline(4.0, color="green", linestyle="--", linewidth=0.5, label="Target (4 pts)")
    ax.set_xlabel("Round")
    ax.set_ylabel("Points")
    ax.set_title("Points per Target / per Hit")
    ax.legend(fontsize=8)

    # 5. Action components: emphasis type
    ax = fig.add_subplot(gs[2, 0])
    ax.scatter(rounds, col(rows, "emphType"), s=8, alpha=0.6)
    ax.set_yticks([0, 1, 2])
    ax.set_yticklabels(["Stationary", "Moving", "Erratic"])
    ax.set_xlabel("Round")
    ax.set_title("Emphasis Type Chosen")

    # 6. Action components: rotation level
    ax = fig.add_subplot(gs[2, 1])
    ax.scatter(rounds, col(rows, "rotLevel"), s=8, alpha=0.6, color="purple")
    ax.set_yticks([0, 1, 2, 3])
    ax.set_yticklabels(["None", "Slow", "Medium", "Fast"])
    ax.set_xlabel("Round")
    ax.set_title("Rotation Level Chosen")

    # 7. Spawn pace
    ax = fig.add_subplot(gs[3, 0])
    ax.scatter(rounds, col(rows, "pace"), s=8, alpha=0.6, color="teal")
    ax.set_yticks([0, 1, 2])
    ax.set_yticklabels(["Fast (1-2s)", "Medium (3-4s)", "Slow (5-6s)"])
    ax.set_xlabel("Round")
    ax.set_title("Spawn Pace Chosen")

    # 8. Epsilon decay
    ax = fig.add_subplot(gs[3, 1])
    ax.plot(rounds, col(rows, "epsilon"), linewidth=1.2, color="red")
    ax.set_xlabel("Round")
    ax.set_ylabel("Epsilon")
    ax.set_title("Exploration Rate (ε) Decay")

    # 9. Targets spawned vs expired
    ax = fig.add_subplot(gs[4, 0])
    ax.bar(rounds, col(rows, "spawned"), width=0.8, label="Spawned", alpha=0.6)
    ax.bar(rounds, col(rows, "expired"), width=0.8, label="Expired", alpha=0.6, color="red")
    ax.set_xlabel("Round")
    ax.set_ylabel("Count")
    ax.set_title("Targets Spawned vs Expired")
    ax.legend(fontsize=8)

    # 10. Q-state visited
    ax = fig.add_subplot(gs[4, 1])
    ax.scatter(rounds, col(rows, "qState"), s=8, alpha=0.5, color="brown")
    ax.set_xlabel("Round")
    ax.set_ylabel("State index")
    ax.set_title("Q-State Visited")
    ax.set_ylim(-1, 27)

    png_path = csv_path.replace(".csv", ".png")
    fig.savefig(png_path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print(f"  Saved: {png_path}")


def running_avg(data, window):
    out = []
    for i in range(window - 1, len(data)):
        out.append(sum(data[i - window + 1:i + 1]) / window)
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    target = sys.argv[1]
    if os.path.isdir(target):
        csvs = sorted(Path(target).glob("*.csv"))
        if not csvs:
            print(f"No .csv files in {target}")
            sys.exit(1)
        for p in csvs:
            print(f"Plotting {p.name}...")
            plot_report(str(p))
    else:
        plot_report(target)

    print("Done.")


if __name__ == "__main__":
    main()
