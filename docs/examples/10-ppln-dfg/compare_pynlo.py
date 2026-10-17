#!/usr/bin/env python3
"""Example 10b -- the same DFG in UltrafastEngine and in pyNLO, side by side.

pyNLO's three-wave-mixing solver couples all fields as plane waves at the pump waist and resolves the poling domain
by domain; the engine reproduces that with "overlap": {"model": "planeWave"} and "qpm": {"model": "domains"}.
The set-up is shared with the automated cross-check in python/tests/test_pynlo_dfg.py (see that file for the
details of the mapping). Needs pyNLO: point PYNLO_SRC at a checkout's src folder (python python/tools/get_pynlo.py).

    PYNLO_SRC=/path/to/pyNLO/src python docs/examples/10-ppln-dfg/compare_pynlo.py [pump_energy_J] [length_m]
"""
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "python", "src"))
sys.path.insert(0, os.path.join(HERE, "..", "..", "python", "tests"))
from test_pynlo_dfg import SIGNAL_EPP, _engine, _pynlo  # noqa: E402


def main():
    ep = float(sys.argv[1]) if len(sys.argv) > 1 else 0.5e-6
    length = float(sys.argv[2]) if len(sys.argv) > 2 else 3e-3
    print(f"pump {ep * 1e6:.2f} uJ, 1 ps; signal {SIGNAL_EPP * 1e9:.0f} nJ; MgO:PPLN {length * 1e3:.1f} mm, 100 um waist")
    ref = _pynlo(length, ep)
    res, v = _engine(length, ep, ref["period"])
    rows = [("idler", ref["idler"], v["energy_idler_J"]), ("signal", ref["signal"], v["energy_signal_J"]),
            ("pump", ref["pump"], v["energy_pump_J"])]
    print(f"{'field':8s} {'pyNLO (J)':>12s} {'engine (J)':>12s} {'diff':>8s}")
    for name, a, b in rows:
        print(f"{name:8s} {a:12.4e} {b:12.4e} {100 * (b / a - 1):+7.2f} %")
    try:
        import matplotlib.pyplot as plt
    except ImportError:
        return
    sp = ref["idler_spectrum"]
    o = np.argsort(sp["wl"])
    wl, s = res["probe_ppln_idler_wavelength_m"], res["probe_ppln_idler_spectrum"]
    fig, ax = plt.subplots(figsize=(6, 3.5))
    ax.plot(sp["wl"][o] * 1e9, sp["s"][o] / sp["s"].max(), label="pyNLO", lw=2)
    ax.plot(wl * 1e9, s / s.max(), "--", label="UltrafastEngine", lw=1.5)
    ax.set(xlabel="idler wavelength (nm)", ylabel="spectrum (norm.)", xlim=(3300, 3490), title="DFG idler")
    ax.legend(frameon=False)
    fig.tight_layout()
    out = os.path.join(HERE, "pynlo_comparison.png")
    fig.savefig(out, dpi=150)
    print("->", out)


if __name__ == "__main__":
    main()
