"""The four ao elements side by side, each doing one simple thing with an analytic answer: a deformable mirror pokes one actuator, an SLM steers a spot with a
blazed grating, a spectral shaper makes a double pulse and an AOPDF chirps a pulse (python ao_components.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b

uf.plugins.load_python_plugins()
from ultrafast_ao import DmBench, Slm, SpectralShaper                 # noqa: E402

C = 299792458.0


# ---------------------------------------------------------------------------------------------------------------- deformable mirror
def deformable_mirror(poke_m=100e-9):
    """One actuator of a 10 x 10 mirror (pitch 0.4 mm) moved by ``poke_m``; the wavefront sensor sees 2 x poke (the beam reflects off the surface and back)."""
    bench = DmBench(actuators=10, pitch_m=0.4e-3, beam_radius_m=1.0e-3, half_width_m=2.0e-3, grid_n=128)
    c = np.zeros(bench.n_actuators)
    k = int(np.argmin(np.hypot(bench.positions[:, 0] - 0.2e-3, bench.positions[:, 1] - 0.2e-3)))     # the actuator next to the axis
    c[k] = poke_m
    waves, x, y = bench.wavefront_waves(c)
    ax_, ay_ = bench.positions[k]

    def at(px, py):                                                   # bilinear reading of the sensor map
        i, j = np.interp(px, x, np.arange(len(x))), np.interp(py, y, np.arange(len(y)))
        i0, j0 = int(np.floor(i)), int(np.floor(j)); fi, fj = i - i0, j - j0
        return float(waves[i0, j0] * (1 - fi) * (1 - fj) + waves[i0 + 1, j0] * fi * (1 - fj) + waves[i0, j0 + 1] * (1 - fi) * fj + waves[i0 + 1, j0 + 1] * fi * fj)
    return {"waves": waves, "x": x, "y": y, "poke_m": poke_m, "wavelength_m": bench.wavelength_m, "peak": float(np.nanmax(waves)),
            "neighbour": at(ax_ + bench.pitch_m, ay_), "expected": 2 * poke_m / bench.wavelength_m, "pitch_m": bench.pitch_m, "actuator": (float(ax_), float(ay_))}


# ---------------------------------------------------------------------------------------------------------------- SLM
LAM, N, DX, W = 800e-9, 128, 20e-6, 1.4e-3


def slm_steering(period_pixels=8):
    """A blazed grating of ``period_pixels`` pixels written to a phase-only SLM (256 grey levels) in front of a lens at its critical focal length
    f = n dx^2 / lambda: the focal-plane pixels are then the FFT bins and the spot sits n / period pixels from the axis."""
    f = N * DX * DX / LAM
    ramp = np.mod(2 * np.pi * (np.arange(N)[:, None] / period_pixels) * np.ones((1, N)), 2 * np.pi)
    g = lambda ip=None: b.transverse("cartesian", nx=N, ny=N, half_width_m=N * DX / 2, spectral_samples=1, absorbing_boundary=True, initial_profile=ip)
    sysd = b.system("slm-grating", [b.gap("src", 0.0, medium="Vacuum", transverse=g(b.initial_profile("gaussian", radius_m=W))),
                                    Slm("slm", DX, pixels_x=N, pixels_y=N, phase_target_rad=ramp.tolist(), grey_levels=256, dispersion="none", offset_x_m=-DX / 2, offset_y_m=-DX / 2),
                                    b.lens("lens", f), b.gap("z", f, medium="Vacuum", transverse=g())])
    d = uf.run(sysd, b.run_spec(b.cw(LAM, 1.0), instruments=[b.instrument("beamProfile", at="z", name="cam")])).instrument("cam")
    F, x = np.asarray(d["fluence_map_j_m2"]), np.asarray(d["x_m"])
    i, j = np.unravel_index(np.argmax(F), F.shape)
    pitch = x[1] - x[0]
    return {"F": F, "x": x, "peak_pixel": (int(i), int(j)), "offset_m": float((x[i] - x[N // 2]) / 1.0), "expected_m": f * LAM / (period_pixels * DX),
            "pitch_m": float(pitch), "axis": (N // 2, N // 2), "first_order_fraction": float(F[i, j] / F.max())}


# ---------------------------------------------------------------------------------------------------------------- spectral shaper and AOPDF
TP, WINDOW = 30e-15, 4e-12


def go(elements, window=WINDOW):
    r = uf.run(b.system("shape", [b.gap("g", 0.0, medium="Vacuum")] + elements), b.run_spec(b.pulse(LAM, TP, energy=1e-6), grid={"windowS": window}))
    return {"t": r["output_time_s"], "p": r["output_power_w"], "sum": r.summary["output"]}


def double_pulse(spacing_s=400e-15, pixels=1024, span_nm=120.0):
    """The mask is the transfer function (e^{-i w tau/2} + e^{+i w tau/2}) / 2 of two pulses ``spacing_s`` apart, written to ``pixels`` pixels over ``span_nm``."""
    D = 1e6
    pitch = span_nm * 1e-9 * D / pixels
    lam = LAM + (np.arange(pixels) - (pixels - 1) / 2) * pitch / D
    w = 2 * np.pi * C / lam - 2 * np.pi * C / LAM
    T = np.cos(w * spacing_s / 2) + 0j
    el = SpectralShaper("comb", pixels=pixels, pixel_pitch_m=pitch, dispersion_m_per_m=D, spot_radius_m=0.5 * pitch, phase_rad=np.angle(T).tolist(),
                        amplitude=np.abs(T).tolist(), center_wavelength_m=LAM)
    r = go([el])
    t, p = r["t"], r["p"]
    i = np.flatnonzero((p[1:-1] > p[:-2]) & (p[1:-1] >= p[2:]) & (p[1:-1] > 0.05 * p.max())) + 1
    return {"t": t, "p": p, "peaks_t": t[i], "peaks_p": p[i], "expected_s": spacing_s}


def chirp(gdd_fs2=1000.0):
    """An AOPDF with GDD stretches a transform-limited Gaussian pulse to tau0 sqrt(1 + (4 ln2 GDD / tau0^2)^2) (the pulse's own spectrum is wide enough to show it)."""
    ref = go([{"id": "aopdf", "type": "aopdf"}])
    out = go([{"id": "aopdf", "type": "aopdf", "gddFs2": gdd_fs2}])
    tau0, tau1 = ref["sum"]["fwhmS"], out["sum"]["fwhmS"]
    return {"ref": ref, "out": out, "tau0": tau0, "tau": tau1, "expected": tau0 * np.sqrt(1 + (4 * np.log(2) * gdd_fs2 * 1e-30 / tau0 ** 2) ** 2)}


def run():
    return {"dm": deformable_mirror(), "slm": slm_steering(), "double": double_pulse(), "chirp": chirp()}


def main():
    r = run()
    dm, s, d, c = r["dm"], r["slm"], r["double"], r["chirp"]
    print("deformable mirror: one actuator moved %.0f nm: sensor peak %.3f waves (2 x poke = %.3f), %.3f waves one pitch away (coupling 0.15)"
          % (dm["poke_m"] * 1e9, dm["peak"], dm["expected"], dm["neighbour"]))
    print("SLM blazed grating (8 pixels): the spot is %.0f um from the axis (lambda f / period = %.0f um), %.0f pixels of %.0f um"
          % (abs(s["offset_m"]) * 1e6, s["expected_m"] * 1e6, abs(s["offset_m"]) / s["pitch_m"], s["pitch_m"] * 1e6))
    sep = abs(d["peaks_t"][-1] - d["peaks_t"][0]) if len(d["peaks_t"]) >= 2 else float("nan")
    print("spectral shaper: %d pulses, %.0f fs apart (programmed %.0f fs)" % (len(d["peaks_t"]), sep * 1e15, d["expected_s"] * 1e15))
    print("AOPDF: GDD 1000 fs^2 stretches %.1f fs to %.1f fs (%.1f fs for a Gaussian)" % (c["tau0"] * 1e15, c["tau"] * 1e15, c["expected"] * 1e15))
    assert abs(dm["peak"] / dm["expected"] - 1) < 0.12 and 0.05 < dm["neighbour"] / dm["peak"] < 0.25, "the poke shows up with about 2 x its height and a neighbour coupling near 0.15"
    assert abs(abs(s["offset_m"]) - s["expected_m"]) < s["pitch_m"], "the spot is where lambda f / period puts it, to within a pixel"
    assert len(d["peaks_t"]) == 2 and abs(sep - d["expected_s"]) < 0.02 * d["expected_s"], "two pulses, 2 % of the programmed spacing"
    assert abs(c["tau"] / c["expected"] - 1) < 0.02, "the AOPDF chirp follows the Gaussian-pulse formula"
    print("checks passed")
    return r


if __name__ == "__main__":
    main()
