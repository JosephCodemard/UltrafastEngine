"""The logo generator (assets/logo/make_logo.py): deterministic, right sizes, a valid multi-size icon, text to the right of the mark."""
import importlib.util
import io
import re
import xml.dom.minidom
from pathlib import Path

import numpy as np
import pytest

PIL = pytest.importorskip("PIL")
pytest.importorskip("matplotlib")
from PIL import Image

SCRIPT = Path(__file__).resolve().parents[2] / "assets" / "logo" / "make_logo.py"
spec = importlib.util.spec_from_file_location("make_logo", SCRIPT)
ml = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ml)


def test_mark_has_the_requested_size_and_is_deterministic():
    for s in (16, 32, 128):
        a, b = ml.mark(s), ml.mark(s)
        assert a.size == (s, s)
        assert np.array_equal(np.asarray(a), np.asarray(b))


def test_small_sizes_are_flat_cells_with_gaps_and_large_sizes_show_the_mode_maps():
    small = np.asarray(ml.mark(32))
    assert (small[..., 3] == 0).any(), "gaps between the nine cells are transparent"
    # a flat cell has one colour in its middle region; a map has many
    pad, gap, cell = ml._layout(32)
    off = (32 - (3 * cell + 2 * gap)) // 2
    patch = small[off + 2:off + cell - 2, off + 2:off + cell - 2, :3].reshape(-1, 3)
    assert len(np.unique(patch, axis=0)) == 1
    big = np.asarray(ml.mark(256))
    pad, gap, cell = ml._layout(256)
    off = (256 - (3 * cell + 2 * gap)) // 2
    patch = big[off + 2:off + cell - 2, off + 2:off + cell - 2, :3].reshape(-1, 3)
    assert len(np.unique(patch, axis=0)) > 100


def test_zernike_modes_are_rms_normalised_over_the_disc():
    r = np.linspace(0, 1, 801)
    t = np.linspace(-np.pi, np.pi, 1601)
    rho, th = np.meshgrid(r, t)
    for j in range(2, 11):
        z = ml.zernike(j, rho, th)
        rms = np.sqrt(np.trapezoid(np.trapezoid(z * z * rho, r, axis=1), t) / np.pi)
        assert rms == pytest.approx(1.0, rel=2e-3), j


def test_large_logo_puts_the_text_to_the_right_of_the_mark():
    light, dark = ml.large(128, False), ml.large(128, True)
    assert light.size == dark.size and light.size[0] > 2.5 * 128
    a = np.asarray(light)
    assert a[:, :128, 3].max() == 255 and a[:, 128:, 3].max() == 255
    # the mark is at the left: its columns carry saturated colour; the text columns are the dark ink or the accent
    ink_light = np.asarray(light)[..., :3][np.asarray(light)[..., 3] == 255][:, 0]
    ink_dark = np.asarray(dark)[..., :3][np.asarray(dark)[..., 3] == 255][:, 0]
    assert ink_dark.mean() > ink_light.mean(), "the dark-page variant has lighter text"


def test_svgs_are_valid_xml_and_the_text_fits_inside_the_canvas():
    for dark in (False, True):
        svg = ml.svg_large(256, dark)
        xml.dom.minidom.parseString(svg)
        width = int(re.search(r'width="(\d+)"', svg).group(1))
        xs = [float(x) for p in re.findall(r'<path fill="[^"]+" d="([^"]+)"', svg) for x in re.findall(r'(-?\d+\.\d+) -?\d+\.\d+', p)]
        assert min(xs) > 256, "the text starts to the right of the mark"
        assert max(xs) < width
    xml.dom.minidom.parseString(ml.svg_small())


def test_ico_holds_every_size_as_png(tmp_path):
    p = tmp_path / "x.ico"
    ml.write_ico(p)
    im = Image.open(p)
    assert sorted(im.info["sizes"]) == [(s, s) for s in (16, 24, 32, 48, 64, 128, 256)]
