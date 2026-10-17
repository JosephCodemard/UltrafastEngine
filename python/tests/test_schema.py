"""The JSON Schemas in schemas/ accept every example file and catch typos (needs the jsonschema package)."""
import json
import re
from pathlib import Path

import pytest

jsonschema = pytest.importorskip("jsonschema")
REPO = Path(__file__).resolve().parents[2]


def _jsonc(text: str):
    out, i, n, ins = [], 0, len(text), False
    while i < n:
        c = text[i]
        if ins:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1]); i += 2; continue
            if c == '"':
                ins = False
            i += 1; continue
        if c == '"':
            ins = True; out.append(c); i += 1; continue
        if text.startswith("//", i):
            j = text.find("\n", i); i = n if j < 0 else j; continue
        if text.startswith("/*", i):
            i = text.find("*/", i) + 2; continue
        out.append(c); i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


SCHEMAS = {"ultrafast-system": REPO / "schemas" / "ufs.schema.json", "ultrafast-run": REPO / "schemas" / "run.schema.json"}


def _validator(fmt):
    return jsonschema.Draft7Validator(json.loads(SCHEMAS[fmt].read_text()))


def test_every_example_file_is_valid():
    n = 0
    for f in list((REPO / "docs").rglob("*.ufs")) + list((REPO / "docs").rglob("*.json")):
        try:
            d = _jsonc(f.read_text())
        except ValueError:
            continue
        fmt = d.get("format") if isinstance(d, dict) else None
        if fmt not in SCHEMAS:
            continue
        n += 1
        errors = [f"{list(e.absolute_path)}: {e.message[:120]}" for e in _validator(fmt).iter_errors(d)]
        assert not errors, f"{f}: {errors[:3]}"
    assert n > 30


def test_schema_catches_typos_and_wrong_types():
    v = _validator("ultrafast-system")
    bad = {"format": "ultrafast-system", "elements": [{"id": "p", "type": "bulk", "material": "FusedSilica", "lenghtM": 1e-3, "lengthM": 1e-3}]}
    assert any("lenghtM" in e.message for e in v.iter_errors(bad))
    r = _validator("ultrafast-run")
    bad_run = {"format": "ultrafast-run", "pulse": {"centerWavelengthM": 8e-7}, "instruments": [{"kind": "spectrometer", "resolutionM": "1 nm"}]}
    assert list(r.iter_errors(bad_run))
    good_run = {"format": "ultrafast-run", "pulse": {"centerWavelengthM": 8e-7}, "instruments": [{"kind": "camera", "imaging": {"plane": "far", "focalLengthM": 0.5}, "sensor": {"bitDepth": 12}}]}
    assert not list(r.iter_errors(good_run))


def test_generated_types_and_api_docs_are_current():
    """python/src/ultrafast/types.py and docs/reference/python-api.md match their generators."""
    import subprocess, sys as _sys
    for tool in ("gen_types.py", "gen_api_docs.py"):
        r = subprocess.run([_sys.executable, str(REPO / "python" / "tools" / tool), "--check"], capture_output=True, text=True)
        assert r.returncode == 0, r.stdout + r.stderr


def test_typed_dicts_cover_every_element_type():
    import json
    from ultrafast import types as T
    schema = json.loads((REPO / "schemas" / "ufs.schema.json").read_text())
    for d in schema["definitions"]:
        if d.startswith("element."):
            et = d.split(".", 1)[1]
            name = et[:1].upper() + et[1:] + "Element"
            assert hasattr(T, name), name
            assert "id" in getattr(T, name).__annotations__
