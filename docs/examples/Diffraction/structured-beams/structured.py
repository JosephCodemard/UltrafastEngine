"""Structured beams on a carried profile, with the Python API: beam quality of modes, and a Bessel core and zone-plate focus against their analytic values."""
import math

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

import ultrafast as uf
from ultrafast import build as b

LAM, N, HALF = 1030e-9, 256, 6e-3


def grid(profile=None):
    return b.transverse("cartesian", nx=N, ny=N, half_width_m=HALF, spectral_samples=2, initial_profile=profile, absorbing_boundary=True)


def screen(z):
    return b.gap("screen", z, medium="Vacuum", transverse=grid())


def source(kind="gaussian", **keys):
    return b.gap("source", 0.0, medium="Vacuum", transverse=grid(b.initial_profile(kind, **keys)))


def run(elements):
    spec = b.run_spec(b.pulse(LAM, 1e-12, energy=1e-3), instruments=[b.instrument("beamProfile", at="end", name="p")])
    p = uf.run(b.system("beam", elements), spec).instrument("p")
    return p, np.array(p["fluence_map_j_m2"]).reshape(N, N), np.array(p["x_m"])


w = 1e-3
modes = [("HG 3,1", source("hermiteGauss", radius_m=w, mode_x=3, mode_y=1), 7, 3), ("LG p=1, l=2", source("laguerreGauss", radius_m=w, mode_p=1, mode_l=2), 5, 5)]
panels = []
print("mode          M2x (analytic)   M2y (analytic)")
for name, first, ax, ay in modes:
    p, f, x = run([first, screen(1.0)])
    print("%-12s %6.3f (%d)       %6.3f (%d)" % (name, p["m2"], ax, p["m2Y"], ay))
    panels.append((name, f, x))

# axicon: the first zero of the Bessel core is at 2.405 / (k sin(angle)); a fine radial grid resolves it
angle = 2e-3
_, f, x = run([source(radius_m=4e-3), b.mask("axicon", "axicon", cone_angle_rad=angle), screen(1.0)])
radial = dict(points=2048, radius_m=12e-3, absorbing_boundary=True, spectral_samples=2)
res = uf.run(b.system("axicon", [b.gap("source", 0.0, medium="Vacuum", transverse=b.transverse("radial", **radial, initial_profile=b.initial_profile("gaussian", radius_m=4e-3))),
                                 b.mask("axicon", "axicon", cone_angle_rad=angle), b.gap("screen", 1.0, medium="Vacuum", transverse=b.transverse("radial", **radial))]),
             b.run_spec(b.pulse(LAM, 1e-12, energy=1e-3), instruments=[b.instrument("beamProfile", at="end", name="p")]))
r, cut = np.array(res.instrument("p")["radius_m"]), np.array(res.instrument("p")["fluence_j_m2"])
zero = r[np.argmax(np.diff(cut) > 0)]
print("Bessel first zero %.1f um   analytic %.1f um" % (zero * 1e6, 2.405 / (2 * math.pi / LAM * math.sin(angle)) * 1e6))
panels.append(("axicon, 1 m behind", f, x))

# zone plate: N open zones of a uniform beam add in phase at the focus, so the fluence there is (2 N)^2 x the incident one (N = 5: 100)
focal, zones = 0.2, 10
radius = math.sqrt(zones * LAM * focal)
incident = 1e-3 / (math.pi * radius ** 2)
fine = dict(points=1024, radius_m=2 * HALF, absorbing_boundary=True, spectral_samples=2)
sharp = uf.run(b.system("zp", [b.gap("source", 0.0, medium="Vacuum", transverse=b.transverse("radial", **fine, initial_profile=b.initial_profile("tophat", radius_m=radius))),
                               b.mask("zp", "zonePlate", focal_length_m=focal), b.gap("focus", focal, medium="Vacuum", transverse=b.transverse("radial", **fine))]),
               b.run_spec(b.pulse(LAM, 1e-12, energy=1e-3)))
print("zone plate gain %.0f   analytic 100" % (sharp.elements()[-1]["values"]["onAxisFluenceOutJPerM2"] / incident))
_, f, x = run([source("tophat", radius_m=radius), b.mask("zp", "zonePlate", focal_length_m=focal), screen(focal)])
panels.append(("zone plate focus", f, x))

fig, axes = plt.subplots(1, 4, figsize=(12, 3.2), constrained_layout=True)
for ax, (name, f, x) in zip(axes, panels):
    span = 3.5 if "focus" not in name else 0.6
    ax.pcolormesh(x * 1e3, x * 1e3, f.T / f.max(), cmap="Blues", shading="auto")
    ax.set(xlim=(-span, span), ylim=(-span, span), xlabel="x (mm)", title=name, aspect="equal")
axes[0].set_ylabel("y (mm)")
fig.savefig("structured.png", dpi=130)
