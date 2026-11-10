"""Focusing through a scattering layer by wavefront shaping: measure the transmission matrix with a phase-only device from intensities alone, then
focus (Vellekoop and Mosk 2007) (python scatterer.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import sensorless

N_GRID, DX, LAM, Z = 64, 20e-6, 800e-9, 0.1
SEG = 8                                          # the phase-only device has (64 / 8)^2 = 64 segments of 8 x 8 grid cells
N_IN = (N_GRID // SEG) ** 2
OUT = [(i, j) for i in range(24, 41, 5) for j in range(24, 41, 5)][:16]          # 16 camera pixels near the centre (4 x 4 lattice, 5 pixels apart)
BEAM_R = 0.9e-3


def scatterer_phase(seed=1):
    return np.random.default_rng(seed).uniform(0, 2 * np.pi, (N_GRID, N_GRID))


def system(segment_phases, diffuser):
    cells = np.kron(np.asarray(segment_phases).reshape(N_GRID // SEG, N_GRID // SEG), np.ones((SEG, SEG)))
    g = lambda ip=None: b.transverse("cartesian", nx=N_GRID, ny=N_GRID, half_width_m=N_GRID * DX / 2, spectral_samples=1, absorbing_boundary=False, initial_profile=ip)
    mod = lambda id, ph: {"id": id, "type": "modulator", "cellsX": N_GRID, "cellsY": N_GRID, "pitchXM": DX, "phaseRad": ph.tolist(), "designWavelengthM": LAM}
    return b.system("scatter", [b.gap("src", 0.0, medium="Vacuum", transverse=g(b.initial_profile("gaussian", radius_m=BEAM_R))), mod("slm", cells), mod("diffuser", diffuser),
                                b.gap("z", Z, medium="Vacuum", transverse=g())])


def camera(phases, diffuser):
    d = uf.run(system(phases, diffuser), b.run_spec(b.cw(LAM, 1.0), instruments=[b.instrument("beamProfile", at="z", name="cam")])).instrument("cam")
    return np.asarray(d["fluence_map_j_m2"])


def segment_weights():
    """Amplitude of the Gaussian beam on each segment (what a flat-phase input puts on the diffuser)."""
    x = (np.arange(N_GRID) - (N_GRID - 1) / 2) * DX
    a = np.exp(-(x[:, None] ** 2 + x[None, :] ** 2) / (2 * BEAM_R ** 2))           # amplitude: exp(-r^2 / w^2) is the field, so intensity is its square
    return a.reshape(N_GRID // SEG, SEG, N_GRID // SEG, SEG).mean(axis=(1, 3)).ravel()


def run():
    diff = scatterer_phase()
    counter = {"runs": 0}

    def intensity(u):
        counter["runs"] += 1
        F = camera(u, diff)
        return np.array([F[i, j] for i, j in OUT])
    base = intensity(np.zeros(N_IN))
    # the baseline of a random input: the mean speckle over many random phase patterns' outputs (cheap enough: 8 patterns)
    rng = np.random.default_rng(5)
    rand = np.array([intensity(rng.uniform(0, 2 * np.pi, N_IN)) for _ in range(8)])
    counter["runs"] = 0
    H = sensorless.transmission_matrix(intensity, N_IN)
    runs_tm = counter["runs"]
    foc = []
    for m in range(len(OUT)):
        u = sensorless.focus_phases(H, m)
        F = camera(u, diff)
        foc.append(F)
    enh = np.array([foc[m][OUT[m]] for m in range(len(OUT))]) / rand.mean()
    # expected enhancement (Rayleigh transmission, unequal segment amplitudes): (pi/4) (sum a)^2 / sum a^2 + 1 - pi/4, a = field amplitude on each segment
    amp = segment_weights()
    theory = np.pi / 4 * amp.sum() ** 2 / (amp ** 2).sum() + 1 - np.pi / 4
    return {"diffuser": diff, "H": H, "runs": runs_tm, "enhancement": enh, "theory": theory, "foc": foc, "baseline_mean": rand.mean(), "random": rand,
            "unfocused": camera(np.zeros(N_IN), diff)}


def main():
    r = run()
    e = r["enhancement"]
    print("%d-segment phase-only input device, random diffuser (pixel phases uniform in 0..2 pi), camera %.0f mm behind it" % (N_IN, Z * 1e3))
    print("transmission matrix of %d outputs measured from intensities alone: %d engine runs (4 (N - 1) + 2 = %d)" % (len(OUT), r["runs"], 4 * (N_IN - 1) + 2))
    print("focusing enhancement over the %d outputs: mean %.1f (+- %.1f), range %.1f to %.1f" % (len(e), e.mean(), e.std(ddof=1) / np.sqrt(len(e)), e.min(), e.max()))
    print("expected (pi/4)(sum a)^2 / sum a^2 + 1 - pi/4 for the Gaussian beam's segment amplitudes: %.1f (equal amplitudes: (pi/4)(N - 1) + 1 = %.1f)" % (r["theory"], np.pi / 4 * (N_IN - 1) + 1))
    assert abs(e.mean() / r["theory"] - 1) < 0.15
    assert e.min() > 0.4 * r["theory"]
    print("checks passed")


if __name__ == "__main__":
    main()
