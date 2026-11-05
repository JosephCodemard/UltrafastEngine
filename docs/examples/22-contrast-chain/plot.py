"""Figure for example 22: third-order cross-correlation trace of the chain and the pre-pulse versus pulse energy.

Run after `ultrafast run chain.ufs run.json -o results` and `ultrafast study study.json -o study-results`,
then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

x = np.genfromtxt("results/xc.csv", delimiter=",", names=True)
st = np.genfromtxt("study-results/study.csv", delimiter=",", names=True, dtype=None, encoding=None)
e = np.array([r[0] for r in st])
pre = np.array([r[2] for r in st])
fig, ax = plt.subplots(1, 2, figsize=(10, 3.8))
ax[0].plot(x["xc_delay_s"] * 1e12, x["xc_signal_db"])
ax[0].annotate("pre-pulse\n-30.6 dB", (-29.4, -30.6), (-52, -12), arrowprops=dict(arrowstyle="->"))
ax[0].annotate("post-pulse\n-26.6 dB", (29.1, -26.6), (35, -12), arrowprops=dict(arrowstyle="->"))
ax[0].set(xlabel="delay (ps)", ylabel="cross-correlation signal (dB, rel. to peak)", ylim=(-100, 5), title="Contrast after the compressor")
ax[1].semilogx(e * 1e3, pre, "o-", label="pre-pulse contrast (simulated)")
ax[1].axhline(-58, color="0.5", ls=":", label="correlator artefact floor")
ax[1].set(xlabel="pulse energy (mJ)", ylabel="pre-pulse contrast (dB)", title="Pre-pulse versus energy")
ax[1].legend(fontsize=8)
fig.tight_layout()
fig.savefig("contrast-chain.png", dpi=110)
