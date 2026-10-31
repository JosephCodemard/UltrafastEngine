"""An interferometric autocorrelator, a Fourier-transform spectrometer and a FROG, each built from elements with the Python API."""
import math
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

import ultrafast as uf
from ultrafast import build as b

BLUE, ORANGE = "#2a78d6", "#eb6834"
C, LAM, TAU = 299792458.0, 1030e-9, 60e-15


def bbo(process, next_, **extra):
    return b.link(b.crystal("x", "BBO", 10e-6, process, type="I", output="sh" if process == "shg" else "sum", beam=b.gaussian_beam(200e-6), **extra), next_)


def michelson(last):
    return b.system("interferometer", b.Branched([
        b.link(b.beam_splitter("bs1"), {"c": "fixed", "d": "stage"}),
        b.link(b.delay("fixed", delay_s=0.0), "bs2:1"),
        b.link(b.delay("stage", delay_s=0.0), "bs2:2"),
        b.link(b.beam_splitter("bs2"), {"c": "virtual", "d": last["id"]}),
        last,
    ]))


def scan(system, delays, outputs=("energy:end",), spec=None, **options):
    spec = spec or b.run_spec(b.pulse(LAM, TAU, energy=1e-9), grid=b.grid(n=8192, window_s=8e-12))
    r = uf.study(system, spec, parameters=[{"path": "element:stage/delayS", "values": list(delays), "nominal": 0.0}], outputs=list(outputs), **options)
    return r, np.array([row["energy:end"] for row in r["rows"]])


# interferometric autocorrelation
delays = np.arange(-160, 160.01, 0.25) * 1e-15
_, iac = scan(michelson(bbo("shg", "end")), delays)
_, background = scan(michelson(bbo("shg", "end")), [10 * TAU])
print("IAC peak / background  %.3f   analytic 8" % (iac.max() / background[0]))

# Fourier-transform spectrometer: the interferogram of a plain detector
dt = 0.5e-15
times = (np.arange(1024) - 512) * dt
_, interferogram = scan(michelson(b.link(b.delay("det", delay_s=0.0), "end")), times, spec=b.run_spec(b.pulse(LAM, TAU, energy=1e-9), grid=b.grid(n=2048, window_s=2e-12)))
padded = 16 * len(times)      # zero padding only draws the spectrum more finely; the resolution stays 1 / (scan length)
transform = np.abs(np.fft.rfft(interferogram - interferogram.mean(), padded))
freq_hz = np.fft.rfftfreq(padded, dt)
inp = uf.run(b.system("input", [b.probe("p")]), b.run_spec(b.pulse(LAM, TAU, energy=1e-9), grid=b.grid(n=2048, window_s=2e-12)))
f_in, s_in = inp["input_omega_rad_s"] / (2 * math.pi), inp["input_spectrum"]
centroid = (freq_hz * transform).sum() / transform.sum()
print("FTS centroid %.2f nm   input %.2f nm" % (C / centroid * 1e9, C / ((f_in * s_in).sum() / s_in.sum()) * 1e9))

# FROG: a delay scan of a spectrometer after a sum-frequency crystal. The delay marginal is the autocorrelation
frog = b.system("frog", b.Branched([
    b.link(b.beam_splitter("bs"), {"c": "fixed", "d": "stage"}),
    b.link(b.delay("fixed", delay_s=0.0), "x"),
    b.link(b.delay("stage", delay_s=0.0), "x:b"),
    bbo("sfg", "end", aux=[b.aux_beam("b", radius_m=200e-6, from_port=True)]),
]))
frog_delays = np.arange(-30, 31) * 0.15 * TAU
frog_spec = b.run_spec(b.pulse(LAM, TAU, energy=1e-9), instruments=[b.instrument("spectrometer", name="spec", normalize="none")])
frog_nm = np.array(uf.run(frog, frog_spec).instrument("spec")["wavelength_m"]) * 1e9
scan(frog, frog_delays, spec=frog_spec, arrayOutputs=["spec/spectrum"], output="results-frog")
with np.load(Path("results-frog") / "study.npz") as z:
    trace = np.array(z[[k for k in z.files if "spectrum" in k][0]])
marginal = trace.sum(axis=1)
fine = np.linspace(frog_delays[0], frog_delays[-1], 20001)
above = fine[np.interp(fine, frog_delays, marginal) >= marginal.max() / 2]
print("FROG marginal FWHM %.1f fs   analytic %.1f fs" % ((above[-1] - above[0]) * 1e15, math.sqrt(2) * TAU * 1e15))

fig, (a, c, d) = plt.subplots(1, 3, figsize=(12, 3.4), constrained_layout=True)
a.plot(delays * 1e15, iac / background[0], color=BLUE, lw=0.5)
a.axhline(8, color="#52514e", lw=0.8, ls=":")
a.set(xlabel="delay (fs)", ylabel="signal / background", title="Interferometric autocorrelation")
band = (freq_hz > 0.97 * C / LAM) & (freq_hz < 1.03 * C / LAM)
order = np.argsort(f_in)
c.plot(f_in[order] * 1e-12, s_in[order] / s_in.max(), color=BLUE, lw=1.5, label="input")
c.plot(freq_hz[band] * 1e-12, transform[band] / transform[band].max(), "--", color=ORANGE, lw=1.5, label="from interferogram")
c.set(xlim=(0.97 * C / LAM * 1e-12, 1.03 * C / LAM * 1e-12), xlabel="frequency (THz)", ylabel="spectrum (peak = 1)", title="Fourier-transform spectrometer")
c.legend(frameon=False, fontsize=8)
keep = (frog_nm > 495) & (frog_nm < 535)
d.pcolormesh(frog_delays * 1e15, frog_nm[keep], trace[:, keep].T / trace.max(), cmap="Blues", shading="auto")
d.set(xlim=(-150, 150), xlabel="delay (fs)", ylabel="wavelength (nm)", title="FROG trace")
for ax in (a, c):
    ax.spines[["top", "right"]].set_visible(False)
fig.savefig("instruments.png", dpi=130)
