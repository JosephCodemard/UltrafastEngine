"""Example 30: compare the simulated diffraction patterns with the Fresnel diffraction integral.

Run the four systems first (see README.md), then

    python compare.py            # table of engine vs Fresnel integral; figures if matplotlib is installed

For every beam and distance it prints the fluence on the axis (engine and integral) and the largest difference over the
pattern, relative to its peak. The integral is evaluated independently here (NumPy quadrature), for the field just after
the aperture: A(r) inside the opening, 0 outside, then free space over z (paraxial Fresnel integral):

    round:  U(r, z) = (k / z) * int_0^a A(rho) exp(i k rho^2 / 2z) J0(k r rho / z) rho d rho
    square: U(x, y, z) = u(x) u(y),  u(x) = sqrt(k / 2 pi z) * int_-a^a A1(xi) exp(i k (x - xi)^2 / 2z) d xi

and the fluence is |U|^2 for an incident field normalised to the pulse energy.
"""
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAM, A, E = 1030e-9, 1e-3, 1e-3                      # wavelength, iris radius / half-width, pulse energy (run.json)
K = 2 * np.pi / LAM
PLANES = [("n3", 3), ("n2", 2), ("n1", 1), ("far", None)]
Z = {name: (A * A / (LAM * n) if n else 5.0) for name, n in PLANES}

# incident field amplitudes (unnormalised), as in the .ufs files
ROUND = {
    "gaussian": lambda r: np.exp(-r ** 2 / (2e-3) ** 2),
    "flattop": lambda r: np.exp(-(r ** 2 / (1.5e-3) ** 2) ** 10),
    "tophat": lambda r: (r <= 3e-3) * 1.0,
}
SQUARE_1D = lambda x: np.exp(-(x ** 2 / (1.5e-3) ** 2) ** 10)


def j0(x):
    """Bessel J0 from its integral representation (accurate to ~1e-12 with 200 nodes)."""
    t = (np.arange(200) + 0.5) * np.pi / 200
    return np.cos(np.multiply.outer(np.asarray(x, float), np.sin(t))).mean(axis=-1)


def fresnel_round(amp, z, r, n=3000):
    rr = np.linspace(0, 20e-3, 200001)
    norm = E / np.trapezoid(amp(rr) ** 2 * 2 * np.pi * rr, rr)
    rho = (np.arange(n) + 0.5) * A / n
    w = np.sqrt(norm) * amp(rho) * np.exp(1j * K * rho ** 2 / (2 * z)) * rho * (A / n)
    return np.array([abs(K / z * np.sum(w * j0(K * ri * rho / z))) ** 2 for ri in r])


def fresnel_line(amp1, z, x, n=4000):
    xi = -A + (np.arange(n) + 0.5) * 2 * A / n
    return np.array([np.sqrt(K / (2 * np.pi * z)) * np.sum(amp1(xi) * np.exp(1j * K * (xx - xi) ** 2 / (2 * z))) * 2 * A / n for xx in x])


def fresnel_square(z, x):
    xx = np.linspace(-20e-3, 20e-3, 400001)
    n1 = np.trapezoid(SQUARE_1D(xx) ** 2, xx)
    uy0 = abs(fresnel_line(SQUARE_1D, z, np.array([0.0]))[0])
    return E / n1 ** 2 * np.abs(fresnel_line(SQUARE_1D, z, x)) ** 2 * uy0 ** 2


def main():
    rows, cuts = [], {}
    for shape in ["gaussian", "flattop", "tophat", "square"]:
        res = os.path.join(HERE, "results-" + shape)
        if not os.path.isdir(res):
            sys.exit("missing %s -- run: ultrafast run %s.ufs run.json -o results-%s" % (res, shape, shape))
        npz = np.load(os.path.join(res, "result.npz"))
        summary = json.load(open(os.path.join(res, "summary.json")))
        t = next(e for e in summary["elements"] if e["id"] == "iris")["values"]["transmission"]
        for name, n in PLANES:
            z = Z[name]
            if shape == "square":
                r, f = npz["p_%s_x_m" % name], npz["p_%s_fluence_x_j_m2" % name]
                sel = np.abs(r) < 8e-3
                r, f = r[sel], f[sel]
                fa = fresnel_square(z, r)
                axis = np.argmin(np.abs(r))
            else:
                r, f = npz["p_%s_radius_m" % name], npz["p_%s_fluence_j_m2" % name]
                sel = r < (8e-3 if name == "far" else 4e-3)
                r, f = r[sel][::3], f[sel][::3]
                fa = fresnel_round(ROUND[shape], z, r)
                axis = 0
            err = np.max(np.abs(f - fa)) / fa.max()
            rows.append((shape, name, z, n, t, f[axis], fa[axis], err))
            cuts[(shape, name)] = (r, f, fa)
    print("%-9s %-4s %8s %6s %7s %14s %14s %10s" % ("beam", "at", "z (m)", "N", "T iris", "F(0) engine", "F(0) Fresnel", "max err"))
    for shape, name, z, n, t, fe, fa, err in rows:
        print("%-9s %-4s %8.4f %6s %7.4f %14.4g %14.4g %9.2f%%" % (shape, name, z, n if n else "0.19", t, fe, fa, 100 * err))
    worst = max(r[-1] for r in rows)
    print("largest difference over all patterns: %.2f %% of the peak" % (100 * worst))
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        print("(matplotlib not installed: no figures)")
        return
    shapes = ["gaussian", "flattop", "tophat", "square"]
    fig, ax = plt.subplots(len(shapes), len(PLANES), figsize=(14, 11))
    for i, s in enumerate(shapes):
        for j, (name, n) in enumerate(PLANES):
            r, f, fa = cuts[(s, name)]
            a = ax[i, j]
            if s != "square":                                   # radial cut: show both sides of the axis
                r, f, fa = np.concatenate([-r[::-1], r]), np.concatenate([f[::-1], f]), np.concatenate([fa[::-1], fa])
            a.plot(r * 1e3, f, lw=1.5, label="engine")
            a.plot(r * 1e3, fa, "k--", lw=0.8, label="Fresnel integral")
            a.set_title("%s, %s (z = %.3f m)" % (s, "N = %d" % n if n else "far zone", Z[name]), fontsize=9)
            if j == 0:
                a.set_ylabel("fluence (J/m$^2$)")
            if i == len(shapes) - 1:
                a.set_xlabel("x (mm)")
    ax[0, 0].legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(os.path.join(HERE, "diffraction-cuts.png"), dpi=110)
    fig, ax = plt.subplots(len(shapes), len(PLANES), figsize=(13, 13))
    for i, s in enumerate(shapes):
        npz = np.load(os.path.join(HERE, "results-" + s, "result.npz"))
        for j, (name, n) in enumerate(PLANES):
            img, x, y = npz["cam_%s_fluence_j_m2" % name], npz["cam_%s_x_m" % name], npz["cam_%s_y_m" % name]
            img = img.reshape(len(x), len(y))
            ax[i, j].imshow(img.T, origin="lower", extent=[x[0] * 1e3, x[-1] * 1e3, y[0] * 1e3, y[-1] * 1e3], cmap="inferno")
            ax[i, j].set_title("%s, %s" % (s, "N = %d" % n if n else "far zone"), fontsize=9)
            ax[i, j].set_xlabel("x (mm)", fontsize=8)
    fig.tight_layout()
    fig.savefig(os.path.join(HERE, "diffraction-images.png"), dpi=110)
    print("wrote diffraction-cuts.png and diffraction-images.png")


if __name__ == "__main__":
    main()
