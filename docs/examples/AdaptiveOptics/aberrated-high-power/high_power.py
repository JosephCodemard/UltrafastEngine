"""An aberrated high-power beam through a nonlinear plate, with and without deformable-mirror correction: hot spots, nonlinear phase and focal quality
(python high_power.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b

uf.plugins.load_python_plugins()
from ultrafast_ao import DmBench                                      # noqa: E402

ABERRATION = {5: 0.9, 7: 0.7, 8: -0.5}            # astigmatism, coma, coma (waves rms at 1030 nm): a strongly aberrated beam
LAM, TAU, GAP_M, PLATE_M, F = 1030e-9, 100e-15, 0.6, 5e-3, 1.0
N2 = 3e-20


def build(bench, commands, energy_j):
    base = bench.system(commands)["elements"][:3]                       # source, aberration screen, deformable mirror
    g = lambda ip=None: b.transverse("cartesian", nx=bench.n, ny=bench.n, half_width_m=bench.half, spectral_samples=8, absorbing_boundary=True, initial_profile=ip)
    base[0] = b.gap("src", 0.0, medium="Vacuum", transverse=g(b.initial_profile("gaussian", radius_m=bench.beam_radius_m)))
    els = base + [b.gap("drift", GAP_M, medium="Vacuum", transverse=g()),
                  {"id": "plate", "type": "bulk", "material": "kerr", "lengthM": PLATE_M, "transverse": g()},
                  b.lens("lens", F), b.gap("z", F, medium="Vacuum", transverse=g())]
    s = b.system("high-power", els)
    s["materials"] = {"kerr": {"index": {"kind": "constant", "value": 1.45}, "n2": N2}}
    return s


def run_case(bench, commands, energy_j):
    inst = [b.instrument("beamProfile", at="drift", name="before"), b.instrument("beamProfile", at="z", name="focus"),
            b.instrument("wavefrontSensor", at="plate", name="wfs", pixels=32, apertureRadiusM=bench.pupil_radius_m * 0.9, threshold=0.0, zernikeTerms=15)]
    pulse = b.pulse(LAM, TAU, energy=energy_j)
    return uf.run(build(bench, commands, energy_j), b.run_spec(pulse, physics={"raman": False, "selfSteepening": False}, instruments=inst))


def run(energy_j=0.2e-3):
    bench = DmBench(aberration=ABERRATION, grid_n=128, half_width_m=3e-3, beam_radius_m=1.0e-3, pupil_radius_m=2.8e-3, actuators=14, pitch_m=0.45e-3, focal_length_m=F)
    cmd = bench.zernike_commands({j: -c for j, c in ABERRATION.items()})
    out = {"bench": bench, "energy": energy_j}
    for label, c in (("aberrated", np.zeros(bench.n_actuators)), ("corrected", cmd)):
        lo, hi = run_case(bench, c, energy_j * 1e-4), run_case(bench, c, energy_j)
        d = lambda r, k: r.instrument(k)
        w_lo, w_hi = d(lo, "wfs"), d(hi, "wfs")
        out[label] = {"before": d(hi, "before"), "focus_lo": d(lo, "focus"), "focus_hi": d(hi, "focus"), "wfs_lo": w_lo, "wfs_hi": w_hi,
                      "b_map_rad": 2 * np.pi * (np.asarray(w_hi["wavefront_waves"]) - np.asarray(w_lo["wavefront_waves"]))}
    return out


def metrics(case, bench):
    fb = np.asarray(case["before"]["fluence_map_j_m2"])
    fl = np.asarray(case["focus_lo"]["fluence_map_j_m2"]); fh = np.asarray(case["focus_hi"]["fluence_map_j_m2"])
    w = case["wfs_hi"]; X, Y = np.meshgrid(w["x_m"], w["y_m"], indexing="ij")
    inside = np.hypot(X, Y) <= 1.5e-3                                    # the lenslets outside the beam see no light: their phase is noise
    return {"peak_fluence_before_plate": float(fb.max()), "b_peak": float(np.nanmax(np.where(inside, case["b_map_rad"], np.nan))), "focus_peak_lo": float(fl.max()), "focus_peak_hi": float(fh.max())}


def main():
    r = run()
    P = r["energy"] / (TAU * 1.06)
    print("beam radius 1 mm, %.1f mJ, %.0f fs: peak power %.1f GW; aberration (Noll: waves rms) %s" % (r["energy"] * 1e3, TAU * 1e15, P / 1e9, ABERRATION))
    print("%.0f mm of Kerr medium (n2 %.0e m^2/W) after %.1f m of free space, then a lens of %.1f m" % (PLATE_M * 1e3, N2, GAP_M, F))
    m = {k: metrics(r[k], r["bench"]) for k in ("aberrated", "corrected")}
    for k, v in m.items():
        print("%-9s peak fluence on the plate %.1f J/m^2 (the flat-phase Gaussian: %.1f), peak nonlinear phase within 1.5 mm of the axis %.2f rad, focal peak fluence %.0f J/m^2 (the same beam without the Kerr plate's effect: %.0f)"
              % (k, v["peak_fluence_before_plate"] / 1e0, 2 * r["energy"] / (np.pi * 1e-6), v["b_peak"], v["focus_peak_hi"], v["focus_peak_lo"] * 1e4))
    assert m["corrected"]["peak_fluence_before_plate"] < m["aberrated"]["peak_fluence_before_plate"]
    assert m["corrected"]["b_peak"] < m["aberrated"]["b_peak"]
    assert m["corrected"]["focus_peak_hi"] > m["aberrated"]["focus_peak_hi"]
    print("checks passed")


if __name__ == "__main__":
    main()
