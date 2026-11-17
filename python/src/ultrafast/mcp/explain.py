"""``explain_warning``: what a warning or validity code of the engine means and what to do (no engine needed).

``explain(text=None, code=None)`` matches a warning text (regular expressions) or a validity ``code`` against :data:`ENTRIES` and returns
``{"matched", "meaning", "action", "docs"}``; an unknown text gets ``{"matched": None, ...}`` with advice on where to look.
"""
from __future__ import annotations

import re
from typing import Any, Dict, List, Optional

# (id, regex over the warning text, meaning, action, docs)
ENTRIES: List[Dict[str, Any]] = [
    {"id": "zeroFrequency", "re": r"zero frequency|reach(ed|es) zero", "meaning": "Nonlinear broadening reached zero frequency of the envelope grid, where the envelope model stops being valid (the pulse is close to one optical cycle in bandwidth).",
     "action": "Treat the result as unreliable. Use a longer pulse, lower energy or shorter element; a carrier-resolved model is the physics that applies for near single-cycle pulses.", "docs": "reference/results.md#validity-validity"},
    {"id": "criticalPower", "re": r"critical power|P_cr|self-focus", "meaning": "The peak power is above 0.3 of the critical power for self-focusing in a 1-D (plane-wave) element, so beam collapse can matter and the 1-D result may be wrong.",
     "action": "Use a spatial (transverse) model for that element, or reduce the peak power or lengthen the pulse. The warning is about the model, not a crash.", "docs": "reference/transverse.md"},
    {"id": "kerrLensCollapse", "re": r"kerr.?lens.*collapse|collapse", "meaning": "A Kerr-lens element is near or past collapse: the intensity grows without bound in the model.",
     "action": "Lower the power or use a spatial model with a physical limit (ionisation) before trusting any number from that element.", "docs": "reference/transverse.md"},
    {"id": "lidt", "re": r"\blidt\b|damage|fluence", "meaning": "The peak fluence is above the (scaled) laser-induced damage threshold of the element.",
     "action": "The real optic would be damaged. Enlarge the beam, lower the energy, or lengthen the pulse before the element.", "docs": "reference/results.md#per-element-records"},
    {"id": "windowEdge", "re": r"window|edgeEnergyFraction|wrap", "meaning": "Energy reached the edge of the time window, so wrap-around corrupts the result (edgeEnergyFraction above about 1e-6).",
     "action": "Let the grid planner size the window (remove a manual grid override), or enlarge grid.windowS to cover stretched pulses and satellites.", "docs": "concepts.md"},
    {"id": "quickStronglyNonlinear", "re": r"strongly nonlinear", "meaning": "Fidelity 'quick' with a total B-integral above 3 rad: every term quick drops (Raman, self-steepening, full polarisation) can matter.",
     "action": "Re-run in 'full'. Do not use quick numbers for this region.", "docs": "reference/fidelity.md"},
    {"id": "quickSteepening", "re": r"self-steepening dropped", "meaning": "Quick dropped self-steepening while B / (omega0 T0) is above 0.01.", "action": "Re-run in 'standard' or 'full' for this point.", "docs": "reference/fidelity.md"},
    {"id": "quickRaman", "re": r"raman dropped", "meaning": "Quick dropped the Raman response although B is above 1 rad and the pulse is shorter than 2 ps (spectral shift, nonlinear energy redistribution).", "action": "Re-run in 'standard' or 'full'.", "docs": "reference/fidelity.md"},
    {"id": "quickSpatial", "re": r"spatial model collapsed|collapsed spatial", "meaning": "Quick replaced a spatial model by the 1-D beam model although the peak power is above 0.3 of the critical power.", "action": "Re-run in 'standard' or 'full' (they keep the spatial model).", "docs": "reference/fidelity.md"},
    {"id": "quickPolarisation", "re": r"polari[sz]ation made scalar", "meaning": "Quick made the polarisation scalar but the input is not linearly polarised along x or y.", "action": "Use 'standard' or 'full' for non-linear-x/y polarisation.", "docs": "reference/fidelity.md"},
    {"id": "unknownKey", "re": r"unknown (key|option)|did you mean", "meaning": "A key in the system or run file is not known to the engine; it is ignored (a typo is the usual cause).",
     "action": "Fix the spelling using the suggestion in the warning, and check the key against the schema resources (ultrafast://schema/run, ultrafast://schema/system).", "docs": "reference/schema.md"},
    {"id": "unverifiedInput", "re": r"unverified|approximate (value|catalogue)", "meaning": "The run used a catalogue value (n2, dispersion, damage threshold, ...) that is approximate or not verified against a published source.",
     "action": "Check summary.unverifiedInputs; replace it by a measured or cited value before quoting a number.", "docs": "reference/materials.md"},
    {"id": "memory", "re": r"memory|free memory|GB", "meaning": "The estimated memory of the run or scan is large compared with the free memory.", "action": "Lower the grid, set outputs.keepProfiles to \"used\", use fidelity quick, or scan with fewer parallel points.", "docs": "reference/memory.md"},
    {"id": "peakCount", "re": r"peakCount|several pulses|multiple peaks|multi-peak", "meaning": "The output has more than one intensity peak, so the FWHM spans several pulses and is not a pulse duration.", "action": "Use rmsWidthS, temporalStrehl or the trace instead of fwhmS.", "docs": "reference/results.md"},
]


def explain(text: Optional[str] = None, code: Optional[str] = None) -> Dict[str, Any]:
    if code:
        for e in ENTRIES:
            if e["id"].lower() == code.lower():
                return _out(e)
    if text:
        for e in sorted(ENTRIES, key=lambda x: not x["id"].startswith("quick")):   # the fidelity warnings also mention critical power
            if re.search(e["re"], text, re.IGNORECASE):
                return _out(e)
    return {"matched": None, "meaning": "This warning is not in the table of known warnings.",
            "action": "Read the full text: it names the element and the quantity. Search the documentation (llms.txt lists it) for a key word of the message, "
                      "and check summary.json 'notes' and 'validity'. Do not ignore it silently: report it with the result.", "docs": "reference/results.md"}


def _out(e: Dict[str, Any]) -> Dict[str, Any]:
    return {"matched": e["id"], "meaning": e["meaning"], "action": e["action"], "docs": e["docs"]}
