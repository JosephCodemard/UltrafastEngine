# 06 -- Carrier-resolved propagation (index varying on the optical-cycle scale)

The time response in `fast-index.ufs` is written in **lab time** (`t_lab`) and oscillates at 60 THz. Because it
declares `"carrierResolved": true`, the engine propagates this element with the real optical field e(t)
(`summary.json` -> `elements[0].solver.representation` says "carrier-resolved field") instead of the envelope:

* the nonlinear polarisation is computed from e(t) itself (third-harmonic generation and self-steepening come
  out automatically; energy generated outside the envelope band is reported as `harmonics (out of band)`);
* the modulation creates spectral sidebands at +-60 THz around the carrier (visible in the spectrometer, dB).

`fieldHarmonic` sets how far the real-field grid extends (3 = up to the third harmonic); `grid.minSpanFraction`
widens the envelope grid so the sidebands stay in band when the result is mapped back.
Use `"fieldMode": "carrier"` to force the real-field model everywhere, `"envelope"` to forbid it.
