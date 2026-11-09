# 24 -- Air at a focus: rotational Raman, ionisation and pulse retrieval

A 0.4 mJ, 40 fs pulse at 800 nm focused (w0 = 100 µm) in the middle of 1 m of air. The Kerr response of air
includes the rotational Raman response of N2 and O2 ([Gases](../../reference/gases.md)), and photo-ionisation
(PPT rates, Drude plasma) is on ([Ionisation](../../reference/ionization.md)). A SHG-FROG measures the output,
and the pulse is retrieved from the simulated trace.

```bash
ultrafast run cell.ufs run.json -o results                        # ~2 s
ultrafast retrieve pcgpa results/result.npz -o retrieved           # FROG -> pulse (the frog instrument's trace)
```

What to look at:

* `summary.json → elements[0].values`: `peakElectronDensityM3` (~2·10²² m⁻³) and `peakIonizationFraction` (~10⁻³).
* The spectrum broadens (B ≈ 7 rad) and its centroid moves to the red (814 nm): the delayed rotational response
  red-shifts the trailing part of the pulse more than the weak plasma blue-shifts the leading part.
* The FROG and its retrieval: `retrieved/retrieval.json` (G ≈ 4·10⁻⁵, FWHM 47.6 fs vs 48.3 fs simulated);
  `retrieved/retrieved_spectrum.csv` can be the input of another run
  (`"pulse": {"shape": "spectrum", "spectrum": {"csv": "retrieved/retrieved_spectrum.csv"}, "energyJ": 3.7e-4}`).

1-D caveat: the peak power is 0.8 of the critical power of air, and the engine warns that self-focusing is not
modelled. For a real filament give the element `"transverse": {"model": "radial", "points": 128, "radiusM": 1e-3,
"equation": "uppe"}` (much slower: minutes).

Try: `"rotationalRaman": false` on a custom air material, or `"ionization": false`, and compare the spectra.
