import os
import sys
from pathlib import Path

import pytest

SRC = Path(__file__).resolve().parents[1] / "src"
if str(SRC) not in sys.path:
    sys.path.insert(0, str(SRC))


def _engine_available():
    try:
        import ultrafast
        ultrafast.version()
        return True
    except Exception as e:  # pragma: no cover
        print("engine not available:", e)
        return False


requires_engine = pytest.mark.skipif(not _engine_available(), reason="engine library not built (python tools/build_engine.py)")


slow = pytest.mark.skipif(os.environ.get("ULTRAFAST_SLOW") != "1", reason="slow test (set ULTRAFAST_SLOW=1)")


def import_pynlo():
    """Import pyNLO (optional) with the compatibility shims its unmaintained code needs on current Python/NumPy/SciPy.

    pyNLO is found on sys.path or via PYNLO_SRC (a source checkout's ``src`` folder; ``python tools/get_pynlo.py``
    fetches the pinned commit). Skips the calling test when it is unavailable.
    """
    import builtins
    import types

    import numpy as np
    src = os.environ.get("PYNLO_SRC")
    if src and src not in sys.path:
        sys.path.insert(0, src)
    try:
        import scipy.misc, scipy.special  # noqa: E401
        if not hasattr(scipy.misc, "factorial"):
            scipy.misc.factorial = scipy.special.factorial
        if not hasattr(np, "trapz"):
            np.trapz = np.trapezoid
        for alias, t in (("float", float), ("int", int), ("complex", complex)):
            if not hasattr(np, alias):
                setattr(np, alias, t)
        sys.modules.setdefault("exceptions", builtins)  # Python-2 module used by the three-wave-mixing code
        import pynlo  # type: ignore
        # pyNLO's DFG code calls two pyfftw functions unconditionally; give that one module a NumPy stand-in
        # (installing a fake pyfftw globally would switch pyNLO's SSFM onto it as well)
        import pynlo.interactions.ThreeWaveMixing.DFG_integrand as dfg  # type: ignore
        if not hasattr(dfg, "pyfftw"):
            pf = types.ModuleType("pyfftw_numpy_shim")
            pf.n_byte_align_empty = lambda n, align, dtype: np.zeros(n, dtype=dtype)

            class _FFTW:
                def __init__(self, i, o, direction="FFTW_FORWARD", **kw):
                    self.i, self.o, self.d = i, o, direction

                def __call__(self):
                    self.o[:] = np.fft.fft(self.i) if self.d == "FFTW_FORWARD" else np.fft.ifft(self.i)
                    return self.o
            pf.FFTW = _FFTW
            dfg.pyfftw = pf
        return pynlo
    except Exception as e:  # pragma: no cover
        pytest.skip("pyNLO not available (%s); set PYNLO_SRC or run tools/get_pynlo.py" % e)
