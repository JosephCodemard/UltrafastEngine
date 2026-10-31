"""Fringe period from the results of the CLI run, against lambda / angle."""
import numpy as np

LAM, TILT = 1030e-9, 2e-3
with np.load("results/result.npz") as z:
    x = z["screen_x_m"]
    f = z["screen_fluence_x_j_m2"]
dx = x[1] - x[0]
spectrum = np.abs(np.fft.rfft(f - f.mean()))
k = np.arange(len(spectrum))
first = int(len(x) * dx / 2e-3)      # above the Gaussian envelope: periods shorter than 2 mm
peak = first + spectrum[first:].argmax()
# refine between samples: the top of a parabola through the three largest bins
a, c, d = np.log(spectrum[peak - 1:peak + 2])
period = len(x) * dx / (peak + 0.5 * (a - d) / (a - 2 * c + d))
print("fringe period %.1f um   lambda / angle %.1f um" % (period * 1e6, LAM / TILT * 1e6))
