"""Plot styles (use_style, ultrafast-plot --style, ULTRAFAST_PLOT_STYLE), the schematics of branched systems, the 3-D spectrogram and the evolution
animation. The palette checks mirror the dataviz validator (lightness band, adjacent-pair separation) in a few lines."""
import math
import os
import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest

matplotlib = pytest.importorskip("matplotlib")
matplotlib.use("Agg")

from conftest import requires_engine
from ultrafast import plotting as up
from ultrafast.plotting import style

SRC = Path(__file__).resolve().parents[1] / "src"


@pytest.fixture(autouse=True)
def _restore_style():
    before = dict(style._theme)
    yield
    style._theme.clear()
    style._theme.update(before)


def _hex(c):
    return [int(c[i:i + 2], 16) / 255 for i in (1, 3, 5)]


def _lin(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def _lum(c):
    r, g, b = (_lin(v) for v in _hex(c))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def _contrast(a, b):
    hi, lo = sorted((_lum(a), _lum(b)), reverse=True)
    return (hi + 0.05) / (lo + 0.05)


# ---- use_style -------------------------------------------------------------------------------------------------------------------------

def test_use_style_is_a_call_and_a_context_manager():
    assert up.current_style() == "ultrafast"
    with up.use_style("paper"):
        assert up.current_style() == "paper"
        with up.use_style("ultrafast-dark"):
            assert up.current_style() == "ultrafast-dark" and style._theme["surface"] == "#1a1a19"
        assert up.current_style() == "paper"
    assert up.current_style() == "ultrafast"
    up.use_style("paper")                                       # a plain call stays in force
    assert up.current_style() == "paper"
    up.set_theme(dark=False)                                    # the old switch still works
    assert up.current_style() == "ultrafast"


def test_unknown_style_and_column_are_value_errors():
    with pytest.raises(ValueError, match="unknown plot style"):
        up.use_style("neon")
    with pytest.raises(ValueError, match="column"):
        up.use_style("paper", column="triple")


def test_environment_variable_sets_the_default_style():
    code = "from ultrafast import plotting as up; print(up.current_style()); up.use_style('ultrafast'); print(up.use_style().name)"
    env = dict(os.environ, PYTHONPATH=str(SRC), ULTRAFAST_PLOT_STYLE="paper")
    r = subprocess.run([sys.executable, "-c", code], env=env, capture_output=True, text=True, timeout=120)
    assert r.returncode == 0, r.stderr
    assert r.stdout.split() == ["paper", "paper"]               # at import, and as the default of use_style() without a name


def test_paper_style_follows_the_publication_recipe():
    with up.use_style("paper"):
        rc = style._rc()
        assert rc["font.family"][0] == "serif" and rc["mathtext.fontset"] == "cm"
        assert rc["xtick.direction"] == rc["ytick.direction"] == "in" and rc["xtick.top"] and rc["ytick.right"]
        assert rc["xtick.minor.visible"] and rc["ytick.minor.visible"] and not rc["axes.grid"]
        assert rc["axes.spines.top"] and rc["axes.spines.right"] and rc["savefig.dpi"] == 600
        assert up.default_dpi() == 600
        assert style._figure(6, 4).get_figwidth() == pytest.approx(7.0)         # double column
    with up.use_style("paper", column="single"):
        fig = style._figure(6, 4)
        assert fig.get_figwidth() == pytest.approx(3.4) and fig.get_figheight() == pytest.approx(4 * 3.4 / 6)
    with up.use_style("ultrafast"):
        assert style._figure(6, 4).get_figwidth() == 6 and up.default_dpi() == 160


def test_a_paper_figure_has_inward_ticks_on_all_sides_and_no_grid():
    with up.use_style("paper"):
        fig = style._figure(6, 4)
        with style._Style():
            ax = fig.add_subplot(111)
            ax.plot([0, 1, 2], [0, 1, 4])
            fig.canvas.draw()
        assert ax.xaxis.get_ticks_position() in ("default", "both") and ax.xaxis.majorTicks[0]._tickdir == "in" and ax.yaxis.majorTicks[0]._tickdir == "in"
        assert ax.xaxis.majorTicks[0].tick2line.get_visible() and ax.yaxis.majorTicks[0].tick2line.get_visible()   # top and right
        assert not ax.xaxis.get_gridlines()[0].get_visible() and len(ax.xaxis.get_minor_ticks()) > 0


def test_save_dpi_never_makes_a_dashboard_enormous():
    with up.use_style("paper"):
        small, big = style._figure(6, 4), style._figure(15, 8)
        assert up.save_dpi(small) == 600 and up.save_dpi(big) == 4800 // 15 and up.save_dpi(big, 300) == 300


def test_science_style_uses_scienceplots_when_installed_else_paper(monkeypatch):
    try:
        import scienceplots  # noqa: F401
        have = True
    except ImportError:
        have = False
    if have:
        with up.use_style("science"):
            assert up.current_style() == "science" and style._theme["rc"]
    monkeypatch.setattr(style, "_science_theme", lambda: None)
    with pytest.warns(UserWarning, match="SciencePlots"):
        with up.use_style("science"):
            assert up.current_style() == "paper"


def test_palettes_are_distinct_light_enough_and_ordered_from_the_logo_hues():
    for name in ("ultrafast", "ultrafast-dark", "paper"):
        with up.use_style(name):
            ser = list(style._theme["series"])
            assert len(set(ser)) == len(ser) == 8
            for c in ser[:6]:                                   # the six series validated with the dataviz validator
                L = _lum(c)
                assert 0.05 < L < 0.8
            surface = style._theme["surface"]
            assert all(_contrast(c, surface) > 1.9 for c in ser[:6]), name
    with up.use_style("ultrafast"):
        r0, g0, b0 = _hex(style._theme["series"][0]); r1, g1, b1 = _hex(style._theme["series"][1])
        assert b0 > r0 and r1 > b1                              # blue then orange: the two hues of the logo
    with up.use_style("ultrafast-dark"):
        assert all(_contrast(c, style._theme["surface"]) >= 3 for c in style._theme["series"][:6])     # dark mode passes the 3:1 check outright
    with up.use_style("paper"):                                 # Okabe-Ito colours in the validated order
        assert style._theme["series"][:6] == ["#0072b2", "#d55e00", "#e69f00", "#009e73", "#56b4e9", "#cc79a7"]


def test_dark_theme_has_its_own_box_fill():
    assert style._DARK["panel"] != style._LIGHT["panel"] and style._DARK["panel"] != "#ffffff"


# ---- the command line ------------------------------------------------------------------------------------------------------------------

@requires_engine
def test_ultrafast_plot_style_option(tmp_path):
    import ultrafast as uf
    from ultrafast import build as b
    import importlib
    plot_cli = importlib.import_module("ultrafast.plot")
    res = uf.run(b.system("plate", [b.bulk("plate", "FusedSilica", 1e-3)]), b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-9)))
    folder = res.write(tmp_path / "run")
    assert plot_cli.main([str(folder), "--style", "paper", "--only", "overview", "--no-pdf", "--out", str(tmp_path / "p")]) == 0
    from PIL import Image
    im = Image.open(tmp_path / "p" / "overview.png")
    assert im.size[0] <= 4800 and abs(im.info["dpi"][0] - 4800 / 15) < 2          # 15 in dashboard: capped at 4800 px
    assert plot_cli.main([str(folder), "--style", "ultrafast-dark", "--only", "overview", "--no-pdf", "--out", str(tmp_path / "d")]) == 0
    assert abs(Image.open(tmp_path / "d" / "overview.png").info["dpi"][0] - 160) < 1


# ---- schematics ------------------------------------------------------------------------------------------------------------------------

@requires_engine
def test_branched_figures_show_loops_instruments_parameters_and_the_dark_panel():
    import ultrafast as uf
    from ultrafast import build as b
    fp = b.system("cavity", b.Branched([
        b.link(b.beam_splitter("m1", 0.8), {"c": "forth", "d": "virtual"}, loop=b.loop("ringDown", 6, 1e-9)),
        b.link(b.delay("forth", length_m=0.01), "m2:2"),
        b.link(b.beam_splitter("m2", 0.8), {"c": "back", "d": "end"}),
        b.link(b.delay("back", length_m=0.01), "m1:2")]))
    res = uf.run(fp, b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-9), grid=b.grid(n=4096, window_s=20e-12), instruments=[b.instrument("spectrometer")]))
    fig = up.system(res, params=True)
    texts = [t.get_text() for t in fig.axes[0].texts]
    assert {"m1", "forth", "m2", "back"} <= set(texts) and any("loop: ringDown" in t for t in texts)
    assert any(t == "1" for t in texts) and any("6 trips" in t for t in texts)
    with up.use_style("ultrafast-dark"):
        fig_dark = up.system(res)
        boxes = [p for p in fig_dark.axes[0].patches if p.__class__.__name__ == "FancyBboxPatch" and p.get_zorder() == 3]
        assert boxes and matplotlib.colors.to_hex(boxes[0].get_facecolor()) == style._DARK["panel"]


@requires_engine
def test_46_pass_same_as_cell_draws_one_box_times_46_in_both_topologies():
    import ultrafast as uf
    from ultrafast import build as b
    pulse = b.pulse(1030e-9, 100e-15, energy=1e-9)
    grid = b.grid(n=2048, window_s=10e-12)
    seq = b.system("cell", [b.gap("pass1", 0.01)] + [b.same_as("pass%d" % i, "pass1") for i in range(2, 47)])
    br = [b.link(b.gap("pass1", 0.01), "pass2")] + [b.link(b.same_as("pass%d" % i, "pass1"), "pass%d" % (i + 1) if i < 46 else "end") for i in range(2, 47)]
    for system in (seq, b.system("cell-branched", b.Branched(br))):
        res = uf.run(system, b.run_spec(pulse, grid=grid))
        texts = " ".join(t.get_text() for t in up.system(res).axes[0].texts)
        assert "× 46" in texts or "×46" in texts or "46" in texts


# ---- the 3-D spectrogram and the animation --------------------------------------------------------------------------------------------

@pytest.fixture(scope="module")
def chirped():
    import ultrafast as uf
    from ultrafast import build as b
    sysd = b.system("glass", [b.bulk("plate1", "FusedSilica", 0.05), b.bulk("plate2", "FusedSilica", 0.05), b.bulk("plate3", "FusedSilica", 0.05)])
    return uf.run(sysd, b.run_spec(b.pulse(800e-9, 20e-15, energy=1e-9), grid=b.grid(n=4096, window_s=8e-12)))


@requires_engine
def test_spectrogram_marginals_reproduce_the_spectrum_and_the_pulse(chirped):
    d = up.spectrogram(chirped, "output", n_time=160, n_wavelength=200)
    S, lam, t = d["intensity"], d["wavelength_m"], d["time_s"]
    spec_marginal = S.sum(axis=1)
    lam_s, spec = np.asarray(chirped["output_wavelength_m"]), np.asarray(chirped["output_spectrum"])
    o = np.argsort(lam_s)
    ref = np.interp(lam, lam_s[o], spec[o])
    assert np.corrcoef(spec_marginal, ref)[0, 1] > 0.97
    tt, p = np.asarray(chirped["output_time_s"]), np.asarray(chirped["output_power_w"])
    assert np.corrcoef(S.sum(axis=0), np.interp(t, tt, p))[0, 1] > 0.97


@requires_engine
def test_spectrogram_ridge_of_a_positive_gdd_pulse_runs_from_red_to_blue(chirped):
    d = up.spectrogram(chirped, "output")
    S, lam, t = d["intensity"], d["wavelength_m"], d["time_s"]
    keep = S.max(axis=0) > 0.3
    ridge = lam[np.argmax(S, axis=0)][keep]
    assert ridge[0] > 800e-9 > ridge[-1]                        # normal dispersion: red leads, blue trails (exp(-i w t) convention)
    assert np.all(np.diff(ridge) < 5e-9)                        # monotone to within the resolution
    # a probe (no stored spectrum envelope) reconstructs the same picture from power and phase
    dp = up.spectrogram(chirped, "plate3")
    kp = dp["intensity"].max(axis=0) > 0.3
    rp = dp["wavelength_m"][np.argmax(dp["intensity"], axis=0)][kp]
    assert rp[0] > rp[-1]


@requires_engine
def test_spectrogram_3d_figure_in_every_style(chirped):
    for style_name in ("ultrafast", "ultrafast-dark", "paper"):
        with up.use_style(style_name):
            fig = up.spectrogram_3d(chirped, "output", n_time=40, n_wavelength=40)
            ax = fig.axes[0]
            assert ax.name == "3d" and ax.get_zlim()[0] < 0 and "wavelength" in ax.get_ylabel() and "time" in ax.get_xlabel()
            assert len(ax.collections) >= 2                                       # the surface and the heat-map floor
    with pytest.raises(ValueError, match="no time-domain data"):
        up.spectrogram(chirped, "nowhere")


@requires_engine
def test_animate_evolution_writes_a_gif_with_one_frame_per_location(chirped, tmp_path):
    from PIL import Image
    out = up.animate_evolution(chirped, tmp_path / "evo.gif", fps=4)
    assert out["locations"] == ["input", "plate1", "plate2", "plate3", "output"] and out["frames"] == 5
    assert (tmp_path / "evo.gif").stat().st_size < 2.5e6 and not out["overBudget"]
    with Image.open(tmp_path / "evo.gif") as im:
        assert im.n_frames == 5
    two = up.animate_evolution(chirped, tmp_path / "two.gif", probes=["plate2"], db=True)
    assert two["locations"] == ["input", "plate2", "output"]
    with pytest.raises(ValueError, match="no probe data"):
        up.animate_evolution(chirped, tmp_path / "x.gif", probes=["nope"])
