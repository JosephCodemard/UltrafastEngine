"""Spatial chirp through the compressor from summary.json, and the GDD against the Treacy formula."""
import json
import math

LAM, D = 1030e-9, 1 / 1.2e6
s = json.load(open("results/summary.json"))
for e in s["elements"]:
    print("%-4s spatial chirp %9.3f mm per 100 nm   GDD %10.0f fs2" % (e["id"], e["values"].get("trackedSpatialChirpXMPerM", 0) * 1e-4, e["system"]["gddS2"] * 1e30))
beta = math.asin(LAM / D - math.sin(math.radians(45)))
slant = 0.2
# Treacy: GDD = -lambda^3 g / (2 pi c^2 d^2 cos^3 beta) for one pass of a pair at normal separation g = slant cos(beta)
gdd = -(LAM ** 3 / (2 * math.pi * 299792458.0 ** 2 * D ** 2)) * slant * math.cos(beta) / math.cos(beta) ** 3
print("Treacy, two pairs  %.0f fs2" % (2 * gdd * 1e30))
