"""Pythonic wrapper over the C API: :class:`System`, :class:`Run`, :class:`Result`."""
from __future__ import annotations

import ctypes
import json
import os
from pathlib import Path
from typing import Any, Callable, Dict, Iterator, List, Mapping, Optional, Sequence, Tuple, Union

import numpy as np

from . import _native as N
from . import measured as _measured

PathLike = Union[str, os.PathLike]
JsonLike = Union[Mapping[str, Any], str, os.PathLike]


class UltrafastError(RuntimeError):
    """An error reported by the engine (bad JSON, unknown model, failed run...)."""

    def __init__(self, code: int, message: str):
        super().__init__(f"[{code}] {message}")
        self.code = code


def _lib():
    return N.library()


def _check(rc: int):
    if rc != 0:
        p = _lib().fn("uf_last_error")()
        msg = ctypes.string_at(p).decode("utf-8") if p else "unknown error"
        raise UltrafastError(rc, msg)


def _take_string(pp: ctypes.c_void_p) -> str:
    s = ctypes.string_at(pp.value).decode("utf-8")
    _lib().fn("uf_free")(pp)
    return s


def _b(s: Optional[str]) -> Optional[bytes]:
    return None if s is None else s.encode("utf-8")


def _json_text(x: JsonLike) -> Tuple[str, bool]:
    """Returns (text, is_path)."""
    if isinstance(x, Mapping):
        return json.dumps(x, allow_nan=False, default=_json_default), False
    s = os.fspath(x)
    if s.lstrip().startswith("{"):
        return s, False
    return s, True


def _json_default(o):
    if isinstance(o, np.ndarray):
        return o.tolist()
    if isinstance(o, (np.floating, np.integer)):
        return o.item()
    raise TypeError(f"not JSON serialisable: {type(o)}")


def version() -> str:
    """Engine version string."""
    return ctypes.string_at(_lib().fn("uf_version")()).decode()


def capabilities() -> dict:
    """Built-in model kinds, element types, materials and instruments."""
    pp = ctypes.c_void_p()
    _check(_lib().fn("uf_capabilities")(ctypes.byref(pp)))
    return json.loads(_take_string(pp))


def command(request: Mapping[str, Any]) -> dict:
    """Run a JSON command of the engine (``uf_command``): ``{"command": "study" | "retrieve" | "materials.list" |
    "materials.show" | "materials.import" | "devices" | "compute.default", ...}``. The higher-level :func:`study`, :func:`retrieve` and
    :mod:`ultrafast.materials` wrap it."""
    pp = ctypes.c_void_p()
    _check(_lib().fn("uf_command")(json.dumps(request, default=_json_default).encode(), ctypes.byref(pp)))
    return json.loads(_take_string(pp))


def devices(selftest: bool = False, benchmark: bool = False, backend: Optional[str] = None, device: Optional[int] = None) -> dict:
    """Compute back-ends and devices (docs/reference/speed.md): ``{"defaultBackend", "fft", "backends": [...],
    "devices": [{"backend", "index", "name", "memoryBytes", "doublePrecision", "usable", ...}]}`` -- the CPU first, then
    CUDA / OpenCL devices found by the gpu plugin. ``selftest=True`` compares every usable device (or only
    ``backend`` / ``device``) with the CPU on standard problems (result in ``"selftest"``); ``benchmark=True`` also
    times both."""
    req: Dict[str, Any] = {"command": "devices", "selftest": selftest, "benchmark": benchmark}
    if backend is not None: req["backend"] = backend
    if device is not None: req["device"] = device
    return command(req)


def benchmark(backend: Optional[str] = None, device: Optional[int] = None) -> List[dict]:
    """Seconds per run on the CPU and on each device for growing 1-D and spatial problems, with the speed-up
    (``[{"problem", "n", "cpuSeconds", "deviceSeconds", "speedup", "relativeError", ...}]``; the first device of each
    available back-end unless ``backend`` / ``device`` are given). Takes a few minutes."""
    r = devices(selftest=True, benchmark=True, backend=backend, device=device)
    rows: List[dict] = []
    for t in r.get("selftest", []):
        for row in t.get("benchmark", []):
            rows.append({"backend": t.get("backend"), "device": t.get("device"), **row})
    return rows


def set_default_backend(backend: Optional[str] = None, fft: Optional[str] = None) -> dict:
    """Process-wide defaults: the back-end of runs whose spec has no ``compute.backend`` ("cpu", "auto", "cuda",
    "opencl"; None keeps the current one) and the CPU FFT provider ("auto", "managed", "fftw", "accelerate").
    Returns the settings in force. The ULTRAFAST_BACKEND / ULTRAFAST_FFT environment variables set the initial values."""
    req: Dict[str, Any] = {"command": "compute.default"}
    if backend is not None: req["backend"] = backend
    if fft is not None: req["fft"] = fft
    return command(req)


def _as_source(x: Any) -> Any:
    """A system / run given as a path (made absolute), a dict, or a System object."""
    if isinstance(x, (str, os.PathLike)):
        return os.path.abspath(os.fspath(x))
    if hasattr(x, "to_dict"):
        return x.to_dict()
    return x


def study(system: Any, run_spec: Any, parameters: Sequence[Mapping[str, Any]] = (), outputs: Sequence[str] = (), mode: str = "scan",
          output: Optional[PathLike] = None, **options: Any) -> dict:
    """Parameter study (docs/reference/studies.md): ``mode`` "scan" (``sampling`` grid | random | lhs | sobol,
    ``samples``), "optimize" (``objective={"output": ..., "goal": "min" | "max" | "target", "target": ...}``) or
    "sensitivity" (``sensitivity={"method": "local" | "sobol"}``) or "coherence" (``coherence={"trials": 10, "at": "end"}``:
    repeated runs with independent quantum noise, result in ``analysis["coherence"]``). Parameters are ``{"path": "element:ID/key", "min",
    "max"}``; outputs are names like ``"pulse:end/fwhmS"``, ``"record:plate/BIntegralRad"``, ``"instrument:ac/fwhmS"``.
    Returns the study result (rows, analysis); ``output`` also writes study.json / study.csv."""
    req: Dict[str, Any] = {"command": "study", "system": _as_source(system), "run": _as_source(run_spec),
                           "parameters": list(parameters), "outputs": list(outputs), "mode": mode}
    req.update(options)
    if output is not None:
        req["output"] = os.path.abspath(os.fspath(output))
    return command(req)


def coherence(system: Any, run_spec: Any, trials: int = 10, at: str = "end", parallel: int = 4, seed: int = 1,
              output: Optional[PathLike] = None) -> dict:
    """First-order spectral coherence |g12(lambda)| over ``trials`` runs with independent one-photon-per-mode noise
    (docs/reference/fibers.md#noise-and-coherence). Returns ``{"meanCoherence", "wavelength_m", "g12", "spectrum", ...}``."""
    r = study(system, run_spec, mode="coherence", coherence={"trials": trials, "at": at}, parallel=parallel, seed=seed, output=output)
    return r["analysis"]["coherence"]


def retrieve(method: str, trace: Any, kind: Optional[str] = None, fundamental: Optional[PathLike] = None,
             material: Optional[str] = None, output: Optional[PathLike] = None, energy: float = 1e-9,
             instrument: Optional[str] = None, **options: Any) -> dict:
    """Pulse retrieval (docs/reference/retrieval.md): ``method`` "pcgpa" | "epie" (FROG) or "dscan"; ``trace`` an NPZ /
    JSON file or a dict of arrays (delay_s or insertion_m, omega_rad_s or wavelength_m, trace). A results folder's
    ``result.npz`` works too: ``instrument`` names the frog / dscan instrument when there are several. Returns the
    result with a ready-to-use run-spec ``"pulse"`` (the retrieved spectrum and phase)."""
    req: Dict[str, Any] = {"command": "retrieve", "method": method, "trace": _as_source(trace) if isinstance(trace, (str, os.PathLike)) else
                           {k: (np.asarray(v).tolist()) for k, v in trace.items()}, "options": options, "energyJ": energy}
    if kind: req["kind"] = kind
    if fundamental: req["fundamental"] = os.path.abspath(os.fspath(fundamental))
    if material: req["material"] = material
    if instrument: req["instrument"] = instrument
    if output is not None: req["output"] = os.path.abspath(os.fspath(output))
    return command(req)


class _Handle:
    def __init__(self, h: ctypes.c_void_p):
        self._h = h

    def __del__(self):
        try:
            if self._h and self._h.value:
                _lib().fn("uf_release")(self._h)
                self._h = None
        except Exception:  # interpreter shutdown
            pass


class System(_Handle):
    """An optical system (a ``.ufs`` system file, or a dict with the same structure)."""

    @classmethod
    def load(cls, source: JsonLike) -> "System":
        text, is_path = _json_text(source)
        h = ctypes.c_void_p()
        if is_path:
            _check(_lib().fn("uf_system_load_file")(_b(os.path.abspath(text)), ctypes.byref(h)))
        else:
            _check(_lib().fn("uf_system_load_json")(_b(text), ctypes.byref(h)))
        return cls(h)

    from_dict = load

    def to_dict(self) -> dict:
        pp = ctypes.c_void_p()
        _check(_lib().fn("uf_system_to_json")(self._h, ctypes.byref(pp)))
        return json.loads(_take_string(pp))

    def validate(self) -> dict:
        """Structural checks + a trial compilation. Returns {'ok', 'errors', 'warnings', 'notes', 'provenance', ...}."""
        pp = ctypes.c_void_p()
        _check(_lib().fn("uf_system_validate")(self._h, ctypes.byref(pp)))
        return json.loads(_take_string(pp))


class ResponseContext:
    """What a Python time-response callback sees (NumPy views, valid only during the call).

    Attributes: ``t``, ``t_lab``, ``intensity``, ``intensity_x``, ``intensity_y``, ``instant_intensity``,
    ``field`` (None in envelope mode), ``t_arrival``, ``z``, ``length``, ``omega0``, ``area``,
    ``carrier_resolved``, ``element_id``, the transverse position ``x``, ``y``, ``r``, ``theta`` of this point
    (spatial models; 0 in 1-D, ``spatial`` tells which), and the outputs ``dn_re`` / ``dn_im`` (pre-zeroed, writable).
    """

    __slots__ = ("n", "t", "t_lab", "intensity", "intensity_x", "intensity_y", "instant_intensity", "field",
                 "t_arrival", "z", "length", "omega0", "area", "carrier_resolved", "element_id", "dn_re", "dn_im",
                 "x", "y", "r", "theta", "spatial")

    def __init__(self, c: N.ResponseCtx, dn_re, dn_im):
        n = c.n
        arr = lambda p: np.ctypeslib.as_array(p, (n,)) if p else None
        self.n = n
        self.t, self.t_lab = arr(c.t), arr(c.t_lab)
        self.intensity, self.intensity_x, self.intensity_y = arr(c.intensity), arr(c.intensity_x), arr(c.intensity_y)
        self.instant_intensity, self.field = arr(c.instant_intensity), arr(c.field)
        self.t_arrival, self.z, self.length = c.t_arrival, c.z, c.length
        self.omega0, self.area, self.carrier_resolved = c.omega0, c.area, bool(c.carrier_resolved)
        self.element_id = c.element_id.decode() if c.element_id else ""
        self.x, self.y, self.spatial = c.x, c.y, bool(c.spatial)
        self.r, self.theta = float(np.hypot(c.x, c.y)), float(np.arctan2(c.y, c.x))
        self.dn_re, self.dn_im = np.ctypeslib.as_array(dn_re, (n,)), np.ctypeslib.as_array(dn_im, (n,))


class ElementContext:
    """What a Python user element sees (``{"type": "user", "callback": "@name"}``).

    ``omega`` (absolute rad/s, ascending), ``t`` (co-moving time, s), ``omega0``, ``dt``, ``arrival`` (s),
    ``element_id``, ``params`` (dict from the element's "params"), and the main beam's spectrum ``ax`` / ``ay``
    (complex NumPy copies; sqrt(W) per bin). Return ``(ax, ay)`` (or just ``ax``) from the callback, or call
    ``set(ax, ay)``; returning None leaves the beam unchanged. ``time_x()`` gives the temporal envelope.
    """

    __slots__ = ("n", "omega", "t", "omega0", "dt", "arrival", "element_id", "params", "ax", "ay", "_c")

    def __init__(self, c: N.ElementCtx):
        n = c.n
        arr = lambda p: np.ctypeslib.as_array(p, (n,))
        self._c = c
        self.n = n
        self.omega, self.t = arr(c.omega).copy(), arr(c.t).copy()
        self.omega0, self.dt, self.arrival = c.omega0, c.dt, c.arrival
        self.element_id = c.element_id.decode() if c.element_id else ""
        self.params = json.loads(c.params_json.decode()) if c.params_json else {}
        self.ax = arr(c.ax_re) + 1j * arr(c.ax_im)
        self.ay = arr(c.ay_re) + 1j * arr(c.ay_im)

    def set(self, ax, ay=None):
        n = self.n
        ax = np.asarray(ax, dtype=complex)
        np.ctypeslib.as_array(self._c.ax_re, (n,))[:] = ax.real
        np.ctypeslib.as_array(self._c.ax_im, (n,))[:] = ax.imag
        if ay is not None:
            ay = np.asarray(ay, dtype=complex)
            np.ctypeslib.as_array(self._c.ay_re, (n,))[:] = ay.real
            np.ctypeslib.as_array(self._c.ay_im, (n,))[:] = ay.imag

    def time_x(self):
        """Temporal envelope of x (sqrt(W)) on ``t`` (engine convention: A(t) = (1/N) sum A(W) exp(-i W t))."""
        return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(self.ax))) / self.n


class Run(_Handle):
    """A system + a run spec (+ callbacks). Call :meth:`execute` to propagate.

    >>> run = Run(system, {"pulse": {...}, "overrides": [...]})
    >>> run.time_response("plasma", lambda ctx: 1e-4 * np.tanh(ctx.t_lab / 50e-15))
    >>> result = run.execute()
    """

    def __init__(self, system: System, spec: JsonLike):
        text, is_path = _json_text(spec)
        h = ctypes.c_void_p()
        if is_path:
            _check(_lib().fn("uf_run_create_file")(system._h, _b(os.path.abspath(text)), ctypes.byref(h)))
        else:
            _check(_lib().fn("uf_run_create")(system._h, _b(text), ctypes.byref(h)))
        super().__init__(h)
        self.system = system
        self._keep = []          # ctypes callbacks must outlive the run
        self._error: Optional[BaseException] = None

    def _guard(self, f):
        def wrapped(*a):
            try:
                return f(*a)
            except BaseException as e:  # report to the engine, re-raise after the run
                self._error = e
                return 1
        return wrapped

    # -------------------------------------------------------------- callbacks
    def time_response(self, name: str, fn: Callable[[ResponseContext], Any], carrier_resolved: bool = False) -> "Run":
        """Register a Delta n(t) model under ``name``; reference it from the run spec / system file as
        ``{"kind": "callback", "name": name}`` (e.g. an override with ``"property": "timeResponse"``).
        ``fn(ctx)`` may write ``ctx.dn_re`` / ``ctx.dn_im`` in place, or return ``dn_re`` or ``(dn_re, dn_im)``."""
        def cb(user, cptr, re, im):
            ctx = ResponseContext(cptr.contents, re, im)
            out = fn(ctx)
            if out is not None:
                if isinstance(out, tuple):
                    ctx.dn_re[:] = out[0]
                    if len(out) > 1 and out[1] is not None:
                        ctx.dn_im[:] = out[1]
                else:
                    ctx.dn_re[:] = out
            return 0
        c = N.TIME_RESPONSE_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_time_response_callback")(self._h, _b(name), c, None, 1 if carrier_resolved else 0))
        return self

    def index(self, name: str, fn: Callable[[np.ndarray], np.ndarray]) -> "Run":
        """Register n(omega) under ``name`` (use as a material ``"index": "@name"``)."""
        def cb(user, n, w, out):
            np.ctypeslib.as_array(out, (n,))[:] = fn(np.ctypeslib.as_array(w, (n,)))
            return 0
        c = N.INDEX_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_index_callback")(self._h, _b(name), c, None))
        return self

    def field(self, name: str, fn: Callable[[np.ndarray, np.ndarray, np.ndarray], np.ndarray]) -> "Run":
        """Register a position-dependent property f(x, y, z) (arrays in m, x/y from the beam axis, z from the
        element start) -> values; use it as ``"indexProfile": "@name"``, ``"absorptionProfile": "@name"``,
        ``"n2Scale": "@name"`` or in gain media (docs/reference/spatial-properties.md)."""
        def cb(user, n, x, y, z, out):
            a = lambda p: np.ctypeslib.as_array(p, (n,))
            np.ctypeslib.as_array(out, (n,))[:] = np.broadcast_to(np.asarray(fn(a(x), a(y), a(z)), dtype=float), (n,))
            return 0
        c = N.FIELD_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_field_callback")(self._h, _b(name), c, None))
        return self

    def filter(self, name: str, fn: Callable[[np.ndarray], Union[np.ndarray, Tuple[np.ndarray, np.ndarray]]]) -> "Run":
        """Register a spectral filter: ``fn(omega) -> T`` or ``(T, phase)`` (power transmission, rad)."""
        def cb(user, n, w, t, p):
            out = fn(np.ctypeslib.as_array(w, (n,)))
            if isinstance(out, tuple):
                np.ctypeslib.as_array(t, (n,))[:] = out[0]
                np.ctypeslib.as_array(p, (n,))[:] = out[1]
            else:
                np.ctypeslib.as_array(t, (n,))[:] = out
            return 0
        c = N.FILTER_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_filter_callback")(self._h, _b(name), c, None))
        return self

    def beam(self, name: str, fn: Callable[[np.ndarray], Union[np.ndarray, Tuple[np.ndarray, np.ndarray]]]) -> "Run":
        """Register a beam model: ``fn(z) -> radius`` or ``(radius_x, radius_y)`` (1/e^2 radii, m)."""
        def cb(user, n, z, x, y):
            out = fn(np.ctypeslib.as_array(z, (n,)))
            rx, ry = (out if isinstance(out, tuple) else (out, out))
            np.ctypeslib.as_array(x, (n,))[:] = rx
            np.ctypeslib.as_array(y, (n,))[:] = ry
            return 0
        c = N.BEAM_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_beam_callback")(self._h, _b(name), c, None))
        return self

    def array_function(self, name: str, fn: Callable[[np.ndarray], np.ndarray]) -> "Run":
        """Register a generic y = f(x) (e.g. a Raman response h(t), or attenuation via ``name + '#index'``)."""
        def cb(user, n, x, y):
            np.ctypeslib.as_array(y, (n,))[:] = fn(np.ctypeslib.as_array(x, (n,)))
            return 0
        c = N.ARRAY_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_array_callback")(self._h, _b(name), c, None))
        return self

    def element(self, name: str, fn: Callable[["ElementContext"], Any]) -> "Run":
        """Register a user element under ``name``; use it as ``{"type": "user", "callback": "@name", "params": {...}}``.
        ``fn(ctx)`` returns ``(ax, ay)``, ``ax``, or None (see :class:`ElementContext`)."""
        def cb(user, cptr):
            ctx = ElementContext(cptr.contents)
            out = fn(ctx)
            if out is not None:
                if isinstance(out, tuple):
                    ctx.set(out[0], out[1] if len(out) > 1 else None)
                else:
                    ctx.set(out)
            return 0
        c = N.ELEMENT_FN(self._guard(cb))
        self._keep.append(c)
        _check(_lib().fn("uf_run_set_element_callback")(self._h, _b(name), c, None))
        return self

    # -------------------------------------------------------------- run
    def execute(self, progress: Optional[Callable[[str, int, int, float], Optional[bool]]] = None) -> "Result":
        """Propagate. ``progress(element_id, index, count, fraction)`` may return True to cancel."""
        if progress is not None:
            def pcb(user, eid, i, n, f):
                return 1 if progress(eid.decode() if eid else "", i, n, f) else 0
            c = N.PROGRESS_FN(self._guard(pcb))
            self._keep.append(c)
            _check(_lib().fn("uf_run_set_progress_callback")(self._h, c, None))
        h = ctypes.c_void_p()
        self._error = None
        rc = _lib().fn("uf_run_execute")(self._h, ctypes.byref(h))
        if self._error is not None:
            err, self._error = self._error, None
            raise err
        _check(rc)
        return Result(h)


class Result(_Handle):
    """Everything a run produced. ``result.summary`` (dict), ``result['output_power_w']`` (NumPy),
    ``result.arrays`` (lazy mapping), ``result.write(directory)``."""

    def __init__(self, h):
        super().__init__(h)
        self._summary = None
        self._cache: Dict[str, np.ndarray] = {}

    @property
    def summary(self) -> dict:
        if self._summary is None:
            pp = ctypes.c_void_p()
            _check(_lib().fn("uf_result_summary_json")(self._h, ctypes.byref(pp)))
            self._summary = json.loads(_take_string(pp))
        return self._summary

    @property
    def provenance(self) -> list:
        pp = ctypes.c_void_p()
        _check(_lib().fn("uf_result_provenance_json")(self._h, ctypes.byref(pp)))
        return json.loads(_take_string(pp))

    def names(self) -> list:
        pp = ctypes.c_void_p()
        _check(_lib().fn("uf_result_array_names")(self._h, ctypes.byref(pp)))
        return json.loads(_take_string(pp))

    def __getitem__(self, name: str) -> np.ndarray:
        if name not in self._cache:
            nd = ctypes.c_int32()
            shape = (ctypes.c_int64 * 4)()
            length = ctypes.c_int64()
            _check(_lib().fn("uf_result_array_info")(self._h, _b(name), ctypes.byref(nd), shape, ctypes.byref(length)))
            a = np.empty(length.value, dtype=np.float64)
            _check(_lib().fn("uf_result_array_copy")(self._h, _b(name), a.ctypes.data_as(N.c_double_p), length.value))
            self._cache[name] = a.reshape(tuple(shape[i] for i in range(nd.value)))
        return self._cache[name]

    @property
    def arrays(self) -> Mapping[str, np.ndarray]:
        res = self

        class _Lazy(Mapping):
            def __getitem__(self, k): return res[k]
            def __iter__(self) -> Iterator[str]: return iter(res.names())
            def __len__(self): return len(res.names())
        return _Lazy()

    def elements(self):
        """Per-element table as a list of dicts (also in summary['elements'])."""
        return self.summary["elements"]

    def instrument(self, name: str) -> dict:
        for i in self.summary["instruments"]:
            if i["name"] == name:
                d = dict(i["values"])
                for a in i["arrays"]:
                    d[a[len(name) + 1:] if a.startswith(name + "_") else a] = self[a]
                return d
        raise KeyError(name)

    @property
    def comparisons(self) -> "List[_measured.Comparison]":
        """Instruments compared with recorded data (their ``measured`` key): chi^2, rms error, scalar readings, notes.
        See :mod:`ultrafast.measured`."""
        return _measured.comparisons(self)

    def comparison(self, instrument: str) -> "_measured.Comparison":
        """The comparison of one instrument with its recorded data (KeyError if it has none)."""
        return _measured.comparison(self, instrument)

    def write(self, directory: PathLike) -> Path:
        p = Path(directory).resolve()
        _check(_lib().fn("uf_result_write")(self._h, _b(str(p))))
        return p


def run(system: Union[System, JsonLike], spec: JsonLike, callbacks: Optional[Mapping[str, Any]] = None,
        progress=None, elements: Optional[Mapping[str, Any]] = None, fields: Optional[Mapping[str, Any]] = None) -> Result:
    """One-call simulation. ``callbacks``: {"name": function} time responses (take a ResponseContext);
    ``elements``: {"name": function} user elements (take an ElementContext); ``fields``: {"name": f(x, y, z)}
    position-dependent properties. Use :class:`Run` directly for index/filter/beam callbacks or carrier-resolved
    responses."""
    sys_ = system if isinstance(system, System) else System.load(system)
    r = Run(sys_, spec)
    for name, fn in (callbacks or {}).items():
        r.time_response(name, fn)
    for name, fn in (elements or {}).items():
        r.element(name, fn)
    for name, fn in (fields or {}).items():
        r.field(name, fn)
    return r.execute(progress)
