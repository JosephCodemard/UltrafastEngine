"""The NOPA's geometry and gain from summary.json, and transverse momentum at the exit face: w_p sin t_p = w_s sin t_s + w_i sin t_i."""
import json
import math

s = json.load(open("results/summary.json"))
v = next(e for e in s["elements"] if e["id"] == "nopa")["values"]
print("cut %.2f deg; pump %.2f deg outside, %.2f deg inside the crystal; idler %.2f deg inside" % (
    v["thetaDeg"], v["externalAngleDeg"], v["noncollinearDeg"], v["angleToSignalDeg_idler"]))
print("leaving at %.2f deg (signal), %.2f deg (pump), %.2f deg (idler)" % (v["outputTiltDeg_signal"], v["outputTiltDeg_pump"], v["outputTiltDeg_idler"]))
w = {k: 1 / v["wavelengthM_" + k] for k in ("signal", "idler", "pump")}  # frequencies in units of c / (2 pi)
t = {k: math.radians(v["outputTiltDeg_" + k]) for k in w}
left, right = w["pump"] * math.sin(t["pump"]), w["signal"] * math.sin(t["signal"]) + w["idler"] * math.sin(t["idler"])
print("transverse momentum balance %.1e" % abs(1 - right / left))
print("signal %.2f uJ from a 10 nJ seed; idler %.2f uJ; Manley-Rowe residual %.0e" % (
    v["energy_signal_J"] * 1e6, v["energy_idler_J"] * 1e6, v["manleyRoweResidual"]))
