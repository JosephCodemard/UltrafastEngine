"""Locating and loading the engine library.

Two back-ends expose the same C API (see ``include/ultrafast.h``):

* **native** -- the NativeAOT library ``ultrafast.dll`` / ``libultrafast.so`` / ``libultrafast.dylib``
  (``dotnet publish src/UltrafastEngine.Native -c Release -r <rid>``), loaded with :mod:`ctypes`.
  No .NET runtime is needed. This is what wheels ship.
* **hosted** -- the managed ``UltrafastEngine.Native.dll`` (plain ``dotnet build``), loaded through the
  .NET host with ``clr_loader``; the ``[UnmanagedCallersOnly]`` exports are fetched as function pointers.
  Needs the .NET 10 runtime. Convenient during development (no AOT toolchain required).

Search order: ``$ULTRAFAST_LIBRARY`` (a native library or a ``UltrafastEngine.Native.dll``), then
``ultrafast/bin/native``, then ``ultrafast/bin/hosted``, then a repository build
(``src/UltrafastEngine.Native/bin/{Release,Debug}/net10.0``).
"""
from __future__ import annotations

import ctypes
import os
import sys
from pathlib import Path
from typing import Callable, Dict, Optional

_HERE = Path(__file__).resolve().parent

# C entry point -> C# method in UltrafastEngine.Native.Exports (hosted back-end)
EXPORTS: Dict[str, str] = {
    "uf_version": "Version", "uf_last_error": "LastError", "uf_free": "Free", "uf_release": "Release",
    "uf_capabilities": "Capabilities", "uf_crystal_phase_match": "CrystalPhaseMatch", "uf_command": "Command",
    "uf_system_load_file": "SystemLoadFile", "uf_system_load_json": "SystemLoadJson",
    "uf_system_to_json": "SystemToJson", "uf_system_validate": "SystemValidate",
    "uf_run_create": "RunCreate", "uf_run_create_file": "RunCreateFile",
    "uf_run_set_time_response_callback": "SetTimeResponse", "uf_run_set_index_callback": "SetIndex",
    "uf_run_set_filter_callback": "SetFilter", "uf_run_set_beam_callback": "SetBeam",
    "uf_run_set_array_callback": "SetArray", "uf_run_set_progress_callback": "SetProgress",
    "uf_run_set_element_callback": "SetElement", "uf_run_set_field_callback": "SetField",
    "uf_run_execute": "RunExecute",
    "uf_result_summary_json": "ResultSummary", "uf_result_provenance_json": "ResultProvenance",
    "uf_result_array_names": "ResultArrayNames", "uf_result_array_info": "ResultArrayInfo",
    "uf_result_array_copy": "ResultArrayCopy", "uf_result_write": "ResultWrite",
}

c_double_p = ctypes.POINTER(ctypes.c_double)
c_char_pp = ctypes.POINTER(ctypes.c_char_p)
vp = ctypes.c_void_p
vpp = ctypes.POINTER(ctypes.c_void_p)


class ResponseCtx(ctypes.Structure):
    """Mirror of ``uf_response_ctx``."""
    _fields_ = [
        ("n", ctypes.c_int32),
        ("t", c_double_p), ("t_lab", c_double_p),
        ("t_arrival", ctypes.c_double), ("z", ctypes.c_double), ("length", ctypes.c_double),
        ("intensity", c_double_p), ("intensity_x", c_double_p), ("intensity_y", c_double_p),
        ("instant_intensity", c_double_p), ("field", c_double_p),
        ("omega0", ctypes.c_double), ("area", ctypes.c_double),
        ("carrier_resolved", ctypes.c_int32),
        ("element_id", ctypes.c_char_p),
        ("x", ctypes.c_double), ("y", ctypes.c_double),
        ("spatial", ctypes.c_int32),
    ]


class ElementCtx(ctypes.Structure):
    """Mirror of ``uf_element_ctx``."""
    _fields_ = [
        ("n", ctypes.c_int32),
        ("omega", c_double_p), ("t", c_double_p),
        ("omega0", ctypes.c_double), ("dt", ctypes.c_double),
        ("ax_re", c_double_p), ("ax_im", c_double_p), ("ay_re", c_double_p), ("ay_im", c_double_p),
        ("arrival", ctypes.c_double),
        ("element_id", ctypes.c_char_p), ("params_json", ctypes.c_char_p),
    ]


ELEMENT_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.POINTER(ElementCtx))
TIME_RESPONSE_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.POINTER(ResponseCtx), c_double_p, c_double_p)
INDEX_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.c_int32, c_double_p, c_double_p)
FILTER_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.c_int32, c_double_p, c_double_p, c_double_p)
BEAM_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.c_int32, c_double_p, c_double_p, c_double_p)
ARRAY_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.c_int32, c_double_p, c_double_p)
FIELD_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.c_int32, c_double_p, c_double_p, c_double_p, c_double_p)
PROGRESS_FN = ctypes.CFUNCTYPE(ctypes.c_int, vp, ctypes.c_char_p, ctypes.c_int32, ctypes.c_int32, ctypes.c_double)

# restype, argtypes for each entry point
SIGNATURES = {
    "uf_version": (ctypes.c_void_p, []),
    "uf_last_error": (ctypes.c_void_p, []),
    "uf_free": (None, [vp]),
    "uf_release": (None, [vp]),
    "uf_capabilities": (ctypes.c_int, [vpp]),
    "uf_command": (ctypes.c_int, [ctypes.c_char_p, vpp]),
    "uf_crystal_phase_match": (ctypes.c_int, [ctypes.c_char_p, vpp]),
    "uf_system_load_file": (ctypes.c_int, [ctypes.c_char_p, vpp]),
    "uf_system_load_json": (ctypes.c_int, [ctypes.c_char_p, vpp]),
    "uf_system_to_json": (ctypes.c_int, [vp, vpp]),
    "uf_system_validate": (ctypes.c_int, [vp, vpp]),
    "uf_run_create": (ctypes.c_int, [vp, ctypes.c_char_p, vpp]),
    "uf_run_create_file": (ctypes.c_int, [vp, ctypes.c_char_p, vpp]),
    "uf_run_set_time_response_callback": (ctypes.c_int, [vp, ctypes.c_char_p, TIME_RESPONSE_FN, vp, ctypes.c_int32]),
    "uf_run_set_index_callback": (ctypes.c_int, [vp, ctypes.c_char_p, INDEX_FN, vp]),
    "uf_run_set_filter_callback": (ctypes.c_int, [vp, ctypes.c_char_p, FILTER_FN, vp]),
    "uf_run_set_beam_callback": (ctypes.c_int, [vp, ctypes.c_char_p, BEAM_FN, vp]),
    "uf_run_set_array_callback": (ctypes.c_int, [vp, ctypes.c_char_p, ARRAY_FN, vp]),
    "uf_run_set_progress_callback": (ctypes.c_int, [vp, PROGRESS_FN, vp]),
    "uf_run_set_element_callback": (ctypes.c_int, [vp, ctypes.c_char_p, ELEMENT_FN, vp]),
    "uf_run_set_field_callback": (ctypes.c_int, [vp, ctypes.c_char_p, FIELD_FN, vp]),
    "uf_run_execute": (ctypes.c_int, [vp, vpp]),
    "uf_result_summary_json": (ctypes.c_int, [vp, vpp]),
    "uf_result_provenance_json": (ctypes.c_int, [vp, vpp]),
    "uf_result_array_names": (ctypes.c_int, [vp, vpp]),
    "uf_result_array_info": (ctypes.c_int, [vp, ctypes.c_char_p, ctypes.POINTER(ctypes.c_int32), ctypes.POINTER(ctypes.c_int64), ctypes.POINTER(ctypes.c_int64)]),
    "uf_result_array_copy": (ctypes.c_int, [vp, ctypes.c_char_p, c_double_p, ctypes.c_int64]),
    "uf_result_write": (ctypes.c_int, [vp, ctypes.c_char_p]),
}


def _native_name() -> str:
    if sys.platform.startswith("win"):
        return "ultrafast.dll"
    if sys.platform == "darwin":
        return "libultrafast.dylib"
    return "libultrafast.so"


def _candidates():
    env = os.environ.get("ULTRAFAST_LIBRARY")
    if env:
        yield Path(env)
    yield _HERE / "bin" / "native" / _native_name()
    if not sys.platform.startswith("win"):   # older builds named it ultrafast.so / ultrafast.dylib
        yield _HERE / "bin" / "native" / _native_name()[3:]
    yield _HERE / "bin" / "hosted" / "UltrafastEngine.Native.dll"
    repo = _HERE.parents[2] if len(_HERE.parents) > 2 else _HERE
    for cfg in ("Release", "Debug"):
        yield repo / "src" / "UltrafastEngine.Native" / "bin" / cfg / "net10.0" / "UltrafastEngine.Native.dll"


class Library:
    """The loaded engine: ``lib.fn('uf_version')()`` etc."""

    def __init__(self, path: Path):
        self.path = path
        self.mode = "hosted" if path.name.lower() == "ultrafastengine.native.dll" else "native"
        self._fns: Dict[str, Callable] = {}
        if self.mode == "native":
            self._cdll = ctypes.CDLL(str(path))
            for name, (res, args) in SIGNATURES.items():
                f = getattr(self._cdll, name)
                f.restype, f.argtypes = res, args
                self._fns[name] = f
        else:
            self._load_hosted(path)

    def _load_hosted(self, path: Path):
        try:
            import clr_loader  # type: ignore
            from clr_loader.ffi import ffi  # type: ignore
            import clr_loader.hostfxr as hostfxr  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise ImportError("The hosted engine needs clr_loader (pip install clr_loader) and the .NET 10 runtime; "
                              "or build the NativeAOT library with tools/build_engine.py --aot.") from e
        cfg = path.with_name("UltrafastEngine.Native.runtimeconfig.json")
        self._runtime = clr_loader.get_coreclr(runtime_config=str(cfg))
        load = self._runtime._get_load_func()
        import atexit
        atexit.register(self._runtime.shutdown)  # close the host handle while cffi is still alive
        asm = hostfxr.encode(str(path))
        type_name = hostfxr.encode("UltrafastEngine.Native.Exports, UltrafastEngine.Native")
        for name, method in EXPORTS.items():
            p = ffi.new("void**")
            # delegate type (void*)-1 == UNMANAGEDCALLERSONLY_METHOD
            rc = load(asm, type_name, hostfxr.encode(method), ffi.cast("void*", -1), ffi.NULL, p)
            if rc != 0:
                raise OSError("could not bind %s (hostfxr error 0x%08x)" % (name, rc & 0xFFFFFFFF))
            res, args = SIGNATURES[name]
            proto = ctypes.CFUNCTYPE(res, *args)
            self._fns[name] = proto(int(ffi.cast("uintptr_t", p[0])))

    def fn(self, name: str) -> Callable:
        return self._fns[name]


_lib: Optional[Library] = None


def library() -> Library:
    """Load (once) and return the engine library."""
    global _lib
    if _lib is None:
        tried = []
        for c in _candidates():
            tried.append(str(c))
            if c.exists():
                _lib = Library(c)
                break
        if _lib is None:
            raise OSError("UltrafastEngine library not found. Build it with `python tools/build_engine.py` "
                          "(or set ULTRAFAST_LIBRARY). Looked in:\n  " + "\n  ".join(tried))
    return _lib
