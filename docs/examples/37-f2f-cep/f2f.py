"""An f-2f interferometer with the Python API: the spectral fringes between a pulse and its harmonic follow the carrier-envelope phase."""
import math

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

import ultrafast as uf
from ultrafast import build as b

BLUE, ORANGE = "#2a78d6", "#eb6834"
C = 299792458.0
LAM_OVERLAP = 470e-9        # where the blue wing of the 3 fs pulse meets the red wing of its second harmonic

system = b.system("f-2f", b.Branched([
    b.link(b.beam_splitter("bs"), {"c": "x", "d": "ref"}),
    b.link(b.crystal("x", "BBO", 5e-6, "shg", type="I", output="sh", beam=b.gaussian_beam(20e-6)), "merge:1"),
    b.link(b.delay("ref", delay_s=40e-15), "merge:2"),
    b.link(b.beam_splitter("merge"), {"c": "end", "d": "virtual"}),
]))


def spectrum(cep):
    pulse = b.pulse(800e-9, 3e-15, energy=1e-9, cepRad=cep, polarization="45")
    res = uf.run(system, b.run_spec(pulse, grid=b.grid(n=2 ** 15, window_s=2e-12)))
    w = res["output_omega_rad_s"]
    # type I: the harmonic is polarised along y, so the y part of the reference is what it beats with
    return w, np.abs(res["output_ay_re"] + 1j * res["output_ay_im"]) ** 2


ceps = np.arange(8) * math.pi / 4
curves = [spectrum(c) for c in ceps]
w = curves[0][0]
at = np.abs(w - 2 * math.pi * C / LAM_OVERLAP).argmin()
signal = np.array([c[1][at] for c in curves])
component = (signal * np.exp(-1j * ceps)).sum() / len(ceps)
second = abs((signal * np.exp(-2j * ceps)).sum() / len(ceps))
print("modulation at one cycle per 2 pi of CEP   %.3f of the mean" % (2 * abs(component) / signal.mean()))
print("at two cycles                             %.4f of the one-cycle part" % (second / abs(component)))

nm = 2e9 * math.pi * C / np.where(w > 0, w, np.inf)
band = (nm > 440) & (nm < 500)
order = np.argsort(nm[band])
fig, (left, right) = plt.subplots(1, 2, figsize=(10, 3.6), constrained_layout=True)
for k, color, label in ((0, BLUE, "CEP = 0"), (4, ORANGE, "CEP = pi")):
    left.plot(nm[band][order], curves[k][1][band][order] / signal.max(), color=color, lw=1.2, label=label)
left.set(xlabel="wavelength (nm)", ylabel="y-polarised spectrum", title="Fringes shift with the CEP")
left.legend(frameon=False)
fine = np.linspace(0, 2 * math.pi, 200)
right.plot(fine / math.pi, signal.mean() + 2 * (component * np.exp(1j * fine)).real, color=BLUE, lw=1.5, label="one-cycle fit")
right.plot(ceps / math.pi, signal, "o", color=ORANGE, ms=5, label="470 nm")
right.set(xlabel="CEP (units of pi)", ylabel="spectrum at 470 nm", title="One fringe per 2 pi")
right.legend(frameon=False)
for ax in (left, right):
    ax.spines[["top", "right"]].set_visible(False)
fig.savefig("f2f.png", dpi=130)
