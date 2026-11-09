# 04 -- Your own n(t, I) in Python

`custom_response.py` adds a toy plasma (free-carrier) response with memory, and the multiphoton absorption that creates it, to the
built-in Kerr effect of a fused-silica plate -- the kind of model the engine cannot know about, supplied as a
vectorised NumPy function:

```python
def plasma(ctx):                       # ctx.t, ctx.t_lab, ctx.intensity, ctx.z, ctx.omega0, ... (NumPy views)
    rho = np.cumsum(SIGMA_K * ctx.intensity**K) * dt
    ctx.dn_re[:] = -rho / (2 * N0 * RHO_C)
    ctx.dn_im[:] = K * HBAR * SIGMA_K * ctx.intensity**(K - 1) * C / 2      # multiphoton absorption

run = uf.Run(system, spec)             # spec contains an override: element:plate / timeResponse / "@plasma" (mode add)
run.time_response("plasma", plasma)
result = run.execute()
```

The same model could be given as an expression in the run spec (no code), or as a C/C++ function through the
C API (`uf_run_set_time_response_callback`, see `docs/examples/cpp`). Use `"mode": "replace"` instead of `"add"` to
switch the built-in Kerr response off and supply the whole Delta n(t) yourself.

Models that vary on the optical-cycle time scale should be registered with `carrier_resolved=True`: the engine
then propagates that element with the carrier-resolved real field (example 06).
