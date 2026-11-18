"""Draw the data flow of the C++ examples into hero.png (nothing is built or run: the C++ build needs CMake and a native library)."""
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch

HERE = Path(__file__).resolve().parent
BLUE, ORANGE, GREY = "#2a78d6", "#eb6834", "#555555"


def box(ax, x, y, w, h, title, sub, color):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", fc="white", ec=color, lw=2))
    ax.text(x + w / 2, y + h * 0.62, title, ha="center", va="center", fontsize=10, fontweight="bold")
    ax.text(x + w / 2, y + h * 0.28, sub, ha="center", va="center", fontsize=8, color=GREY)


def arrow(ax, x0, y0, x1, y1, label=""):
    ax.annotate("", xy=(x1, y1), xytext=(x0, y0), arrowprops=dict(arrowstyle="->", color=GREY, lw=1.5))
    if label:
        ax.text((x0 + x1) / 2, (y0 + y1) / 2 + 0.12, label, ha="center", fontsize=8, color=GREY)


if __name__ == "__main__":
    fig, ax = plt.subplots(figsize=(11, 3.4))
    ax.set_xlim(0, 11); ax.set_ylim(0, 3.4); ax.set_axis_off()
    box(ax, 0.2, 1.2, 2.3, 1.0, "run_system.cpp", "your C / C++ / Fortran / MATLAB code", ORANGE)
    box(ax, 4.2, 1.2, 2.6, 1.0, "ultrafast.h (C API)", "uf_run_*, callbacks, uf_devices_json", BLUE)
    box(ax, 8.4, 1.2, 2.3, 1.0, "native engine", "CPU / CUDA / OpenCL", BLUE)
    arrow(ax, 2.5, 1.7, 4.2, 1.7, "system, run spec")
    arrow(ax, 6.8, 1.7, 8.4, 1.7, "run")
    arrow(ax, 8.4, 1.4, 6.8, 1.4); arrow(ax, 4.2, 1.4, 2.5, 1.4, "summary JSON, arrays")
    box(ax, 0.2, 0.0, 2.3, 0.7, "callbacks", "time response, progress", ORANGE)
    arrow(ax, 1.35, 0.7, 1.35, 1.2)
    box(ax, 4.2, 2.5, 2.6, 0.7, "devices.cpp", "list and self-test devices", ORANGE)
    arrow(ax, 5.5, 2.5, 5.5, 2.2)
    ax.set_title("The C++ examples: one header, one native library", loc="left", fontsize=11)
    fig.savefig(HERE / "hero.png", dpi=100)
    print("wrote hero.png")
