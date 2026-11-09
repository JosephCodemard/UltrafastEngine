"""Example 31: print the numbers of the two runs against their analytic values."""
import json
import math

LAM = 1030e-9
s = json.load(open("results-cavity/summary.json"))
v = s["elements"][0]["values"]
f, L = 0.5, 0.6
w0 = math.sqrt(LAM / math.pi * math.sqrt(L * (4 * f - L)) / 2)
print(f"(A + D) / 2      {v['stabilityX']:.4f}   analytic {1 - L / (2 * f):.4f}")
print(f"mode radius (mm) {v['modeRadiusXM'] * 1e3:.4f}   analytic {w0 * 1e3:.4f}")
print(f"Fox-Li radius    {v['foxLiRadiusXM'] * 1e3:.4f}   round-trip loss {v['roundTripLoss'] * 100:.3f} %, {v['foxLiRoundTrips']:.0f} round trips")
s = json.load(open("results-m2/summary.json"))
w = {e["id"]: e["beam"]["radiusXM"] for e in s["elements"]}
print(f"waist (mm)       {w['waist'] * 1e3:.4f}   analytic 0.5000")
print(f"at z_R (mm)      {w['screen'] * 1e3:.4f}   analytic {0.5 * math.sqrt(1 + 2.5 ** 2):.4f}")
