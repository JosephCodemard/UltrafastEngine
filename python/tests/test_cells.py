"""The cellMode command from Python: Kogelnik-Li mode of the Schulte cell and a Kerr-shifted mode."""
import math

from conftest import requires_engine

pytestmark = requires_engine

from ultrafast import cells


def test_empty_cell_of_the_schulte_cell():
    r = cells.cell_mode(1030e-9, mirror_radius=0.35, separation=0.54)
    assert abs(r["emptyCell"]["waistRadiusM"] - 220e-6) < 1e-6
    assert abs(r["emptyCell"]["radiusAtMirror1M"] - 459e-6) < 2e-6
    g = 1 - 0.54 / 0.35
    assert abs(r["roundTripGouyPhaseRad"] - 2 * math.acos(g)) < 1e-9     # > pi for the quasi-concentric side


def test_kerr_lens_shifts_the_matched_mode():
    r = cells.cell_mode(800e-9, mirror_radius=1.5, separation=2.9, peak_power=2e9, gas={"species": "Ar", "pressureBar": 0.4})
    assert r["matched"] and r["residual"] < 1e-9
    assert abs(r["shiftOfWaistRadius"]) > 0.01
