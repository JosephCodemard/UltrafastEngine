"""Run the OpticStudio-free part of this example (the built-in fake argon cell) and draw the system and the overview into hero.png."""
import os
import subprocess
import sys
import tempfile
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.image as mpimg
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
sys.path.insert(0, str(ROOT / "python" / "src"))
from ultrafast import plotting as up  # noqa: E402

CLI = ROOT / "src" / "UltrafastEngine.Cli" / "bin" / "Release" / "net10.0" / "ultrafast.dll"

if __name__ == "__main__":
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1")
    with tempfile.TemporaryDirectory() as tmp:
        for args in (["zemax-demo", "mpc", str(HERE / "export.json"), "-o", f"{tmp}/mpc_from_zemax.ufs"],
                     ["run", f"{tmp}/mpc_from_zemax.ufs", str(HERE / "run.json"), "-o", f"{tmp}/results"]):
            subprocess.run(["dotnet", str(CLI), *args], check=True, env=env, stdout=subprocess.DEVNULL)
        res = up.load(f"{tmp}/results")
        a, b = up.system(res), up.overview(res)
        for name, f in (("a", a), ("b", b)):
            f.savefig(f"{tmp}/{name}.png", dpi=100)
        fig, ax = plt.subplots(2, 1, figsize=(12, 11), constrained_layout=True,
                               gridspec_kw={"height_ratios": [a.get_figheight(), b.get_figheight()]})
        for axis, name, title in zip(ax, "ab", ("the 18 elements exported from the (fake) OpticStudio argon cell", "the run: pulse in and out, spectrum, losses")):
            axis.imshow(mpimg.imread(f"{tmp}/{name}.png")); axis.set_axis_off(); axis.set_title(title, loc="left", fontsize=10)
        fig.savefig(HERE / "hero.png", dpi=70)
    print("wrote hero.png")
