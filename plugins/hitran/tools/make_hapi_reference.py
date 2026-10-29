"""Reference data for the opt-in test ``HitranTests.AgainstHapi_RealLines_AgreeWithTheReferenceImplementation``.

Downloads a few real line lists (needs the network and ``pip install hitran-api``), writes them and the partition sums with the plugin's
downloader, and lets HAPI (the HITRAN team's own code, TIPS partition sums) compute the Voigt absorption coefficient at several
temperatures. The engine's model must reproduce it. Usage::

    python plugins/hitran/tools/make_hapi_reference.py DIR
    ULTRAFAST_HITRAN_VALIDATE_DIR=DIR dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll AgainstHapi

Nothing is downloaded by the tests themselves, and the data are not part of the repository (HITRAN has its own terms of use).
"""
from __future__ import annotations

import contextlib
import io
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python"))
import ultrafast_hitran as hit  # noqa: E402
import importlib  # noqa: E402
_dl = importlib.import_module("ultrafast_hitran.download")  # the module (the package exports the function download)

try:
    import hapi  # noqa: E402
except ImportError:
    import hapi2 as hapi  # type: ignore  # noqa: E402

# molecule, band (cm-1), mole fraction of the absorber, why
CASES = [
    ("H2O", 9690.0, 9730.0, 0.0116, "the 1 um water band: the case of the delayed-light work"),
    ("H2O", 7180.0, 7200.0, 0.0116, "the strong 1.39 um water band"),
    ("CO2", 6970.0, 7000.0, 4.2e-4, "the CO2 combination band near 1.43 um"),
    ("O2", 7870.0, 7900.0, 0.2095, "the O2 1.27 um (a1Delta_g) band"),
    ("O2", 13110.0, 13150.0, 0.2095, "the O2 A band (0.76 um)"),
]
TEMPERATURES = [250.0, 296.0, 350.0]


def main(out: str) -> None:
    out_dir = Path(out); out_dir.mkdir(parents=True, exist_ok=True)
    cases = []
    for mol, lo, hi, x, why in CASES:
        tag = f"{mol}_{int(lo)}_{int(hi)}"
        case_dir = out_dir / tag
        with contextlib.redirect_stdout(io.StringIO()):
            files = hit.download([mol], lo, hi, str(case_dir), api=hapi)
        hapi.db_begin(str(case_dir / "hapi_db"))
        tables = [t for t in (f"{mol}_{iso}_{int(lo)}_{int(hi)}" for iso in _dl.MAIN_ISOTOPOLOGUES[mol]) if (case_dir / "hapi_db" / (t + ".data")).exists()]
        for T in TEMPERATURES:
            with contextlib.redirect_stdout(io.StringIO()):
                nu, k = hapi.absorptionCoefficient_Voigt(SourceTables=tables, Environment={"p": 1.0, "T": T}, Diluent={"air": 1 - x, "self": x},
                                                        WavenumberRange=[lo + 3, hi - 3], WavenumberStep=0.002, HITRAN_units=False)
            np.savetxt(case_dir / f"ref_{int(T)}.csv", np.c_[nu, k])
        cases.append({"tag": tag, "molecule": mol, "lines": files[mol], "moleFraction": x, "temperaturesK": TEMPERATURES, "why": why})
        print(tag, "ok")
    (out_dir / "cases.json").write_text(json.dumps(cases, indent=1))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "hapi_reference")
