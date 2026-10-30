using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Physics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Tests
{
    [TestArea("Foundations: JSON, expressions, NPZ, FFT, dispersion maths, materials, Fresnel, thin films, Treacy")]
    public class FoundationTests
    {
        [Test]
        public void Json_RoundTripCommentsTrailingCommas()
        {
            var v = JsonValue.Parse("{ // comment\n \"a\": [1, 2.5e-3, -4,], /* block */ \"b\": {\"c\": \"x\\ny\\u00e9\", \"d\": true, \"e\": null}, }").AsObject();
            Assert.Close(2.5e-3, v.GetArray("a")[1].AsDouble(), 0);
            Assert.Equal("x\nyé", v.GetObject("b").GetString("c"));
            var again = JsonValue.Parse(v.ToJson(true)).AsObject();
            Assert.Equal(v.ToJson(false), again.ToJson(false), "round trip");
            Assert.Throws<JsonException>(() => JsonValue.Parse("{\"a\": [1, 2"), "unterminated");
        }

        [Test]
        public void Expression_PrecedenceFunctionsAndErrors()
        {
            var e = CompiledExpression.Compile("2 + 3*x^2 - -1 + max(x, 10)/5 + if(x > 1, 100, 0)", new[] { "x" });
            var r = e.Evaluate(new Dictionary<string, double[]> { { "x", new[] { 0.0, 2.0 } } }, 2);
            Assert.Close(2 + 0 + 1 + 2, r[0], 1e-15, 0, "x=0");
            Assert.Close(2 + 12 + 1 + 2 + 100, r[1], 1e-15, 0, "x=2");
            var p = CompiledExpression.Compile("a*sin(pi/2) + 2**3**0 + sech(0) + gauss(0, 1)", new string[0], new Dictionary<string, double> { { "a", 4 } });
            Assert.Close(4 + 2 + 1 + 1, p.EvaluateScalar(new Dictionary<string, double>()), 1e-15);
            Assert.Throws<FormatException>(() => CompiledExpression.Compile("x + y", new[] { "x" }), "unknown name");
            Assert.Throws<FormatException>(() => CompiledExpression.Compile("sin(1, 2)", new string[0]), "arity");
        }

        [Test]
        public void Npz_WriteReadRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "uf_test_" + Guid.NewGuid().ToString("N") + ".npz");
            var a = new NdArray("vec", new[] { 1.0, -2.5, Math.PI });
            var m = new NdArray("mat", Enumerable.Range(0, 6).Select(i => (double)i).ToArray(), 2, 3);
            NpzWriter.Write(path, new[] { a, m });
            var back = NpzWriter.Read(path);
            File.Delete(path);
            Assert.Equal(2, back.Count, "arrays");
            var m2 = back.First(x => x.Name == "mat");
            Assert.Equal(3, m2.Shape[1], "cols");
            Assert.Close(5.0, m2.Data[5], 0);
            Assert.Close(Math.PI, back.First(x => x.Name == "vec").Data[2], 0);
        }

        [Test(Reference = "Parseval's theorem", Tolerance = "1e-12, 0.1 %")]
        public void Fft_RoundTripParsevalAndSignConvention()
        {
            int n = 1024;
            var g = FrequencyGrid.Create(n, 1e-15, U.W(800e-9));
            var rnd = new Random(1);
            var x = Enumerable.Range(0, n).Select(_ => new Complex(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5)).ToArray();
            var f = SpectralTransform.TimeToFreq(x);
            var y = SpectralTransform.FreqToTime(f);
            Assert.Less(x.Zip(y, (a, b) => (a - b).Magnitude).Max(), 1e-12, "round trip");
            double et = x.Sum(c => c.Magnitude * c.Magnitude), ef = f.Sum(c => c.Magnitude * c.Magnitude) / n;
            Assert.Close(et, ef, 1e-12, 0, "Parseval");
            // physics convention: multiplying the spectrum by exp(+i w tau) DELAYS the pulse by tau
            var p = new PulseSpec { CenterWavelengthM = 800e-9, DurationFwhmS = 20e-15, EnergyJ = 1e-9 }.Build(g);
            double tau = 100e-15;
            var ax = p.Ax.Select((c, i) => c * Complex.FromPolarCoordinates(1, g.OmegaOffset[i] * tau)).ToArray();
            var q = p.WithSpectrum(ax, p.Ay);
            Assert.Close(tau, U.TemporalCentroid(q) - U.TemporalCentroid(p), 1e-3, 0, "delay");
        }

        [Test(Reference = "known polynomial phase", Tolerance = "1e-09, 1e-06, 0.01 %")]
        public void DispersionMath_RecoversPolynomialPhase()
        {
            var g = FrequencyGrid.Create(4096, 2e-15, U.W(1030e-9));
            double gd = 1e-12, gdd = 5000e-30, tod = 20000e-45;
            var ph = g.OmegaOffset.Select(d => gd * d + gdd * d * d / 2 + tod * d * d * d / 6).ToArray();
            var r = DispersionMath.FromUnwrappedPhase(g.OmegaAbsolute, ph, g.CarrierIndex, 0);
            Assert.Close(gd, r.GdAtCarrierS, 1e-9); Assert.Close(gdd, r.GddAtCarrierS2, 1e-6); Assert.Close(tod, r.TodAtCarrierS3, 1e-4);
        }

        [Test(Reference = "Ciddor (1996) air at 633 nm; Schott N-BK7 and Malitson fused silica n_d", Tolerance = "2e-05, 1 %, 3 %")]
        public void Materials_IndicesMatchReferenceValues()
        {
            // Ciddor at its standard conditions (15 C, 101325 Pa, dry, 450 ppm), 633 nm: 2.76533e-4
            var air = new CiddorAirIndex(15, 101325, 0, 450);
            var n = new double[1];
            air.Evaluate(new[] { U.W(633e-9) }, n);
            Assert.Close(2.76533e-4, n[0] - 1, 2e-5, 0, "Ciddor 633 nm");
            // catalogue (relative-to-air) values at the d line 587.56 nm
            var bk7 = new SellmeierIndex(new[] { 1.03961212, 0.231792344, 1.01046945 }, new[] { 0.00600069867, 0.0200179144, 103.560653 });
            bk7.Evaluate(new[] { U.W(587.56e-9) }, n);
            Assert.Close(1.5168, n[0], 2e-5, 0, "N-BK7 nd");
            var fs = MaterialLibrary.BuiltIn("F_SILICA");
            double nRel = fs.IndexAt(U.W(587.56e-9)) / MaterialLibrary.BuiltIn("Air").IndexAt(U.W(587.56e-9));
            Assert.Close(1.45846, nRel, 2e-5, 0, "fused silica nd (alias F_SILICA)");
            // fused silica GDD at 800 nm: 36.16 fs^2/mm (absolute index -- essentially identical)
            var t = new BulkTerm(fs, 1e-3);
            var d = t.DispersionAt(U.W(800e-9));
            Assert.Close(36.16e-30, d.GddS2, 0.01, 0, "GDD 1 mm FS @ 800 nm");
            Assert.Close(27.4e-45, d.TodS3, 0.03, 0, "TOD 1 mm FS @ 800 nm");
            // gas: Ar at 1 bar, 273 K, 800 nm: n - 1 ~ 2.8e-4
            var ar = new GasIndex("Ar", 1, 273); ar.Evaluate(new[] { U.W(800e-9) }, n);
            Assert.Close(2.8e-4, n[0] - 1, 0.03, 0, "Ar");
        }

        [Test(Reference = "Fresnel equations: R + T = 1, Brewster angle", Tolerance = "1e-12")]
        public void Fresnel_EnergyConservationNormalIncidenceAndBrewster()
        {
            var air = MaterialLibrary.BuiltIn("Vacuum");
            var glass = new Material { Name = "n1.5", Index = new ConstantIndex(1.5) };
            double w = U.W(800e-9);
            var c0 = ThinFilm.Evaluate(w, 0, 1.0, 1.5, null);
            Assert.Close(0.04, c0.Rs.Magnitude * c0.Rs.Magnitude, 1e-12, 0, "R normal");
            Assert.Close(1.0, c0.Rs.Magnitude * c0.Rs.Magnitude + c0.Ts.Magnitude * c0.Ts.Magnitude, 1e-12, 0, "R+T s");
            Assert.Close(c0.Rs.Real, c0.Rp.Real, 1e-12, 1e-15, "rs = rp at normal incidence");
            foreach (double deg in new[] { 20.0, 45.0, 70.0 })
            {
                var c = ThinFilm.Evaluate(w, deg * Math.PI / 180, 1.0, 1.5, null);
                Assert.Close(1.0, c.Rs.Magnitude * c.Rs.Magnitude + c.Ts.Magnitude * c.Ts.Magnitude, 1e-12, 0, "R+T s @" + deg);
                Assert.Close(1.0, c.Rp.Magnitude * c.Rp.Magnitude + c.Tp.Magnitude * c.Tp.Magnitude, 1e-12, 0, "R+T p @" + deg);
            }
            var b = ThinFilm.Evaluate(w, Math.Atan(1.5), 1.0, 1.5, null);
            Assert.Less(b.Rp.Magnitude, 1e-12, "rp at Brewster");
            // total internal reflection: glass -> air beyond the critical angle
            var tir = ThinFilm.Evaluate(w, 50 * Math.PI / 180, 1.5, 1.0, null);
            Assert.Close(1.0, tir.Rs.Magnitude, 1e-12, 0, "TIR |rs|");
            Assert.Close(1.0, tir.Rp.Magnitude, 1e-12, 0, "TIR |rp|");
        }

        [Test(Reference = "quarter-wave AR and Bragg mirror (analytic thin-film results)", Tolerance = "1e-10, 1e-09")]
        public void ThinFilm_QuarterWaveArAndBraggMirror()
        {
            double lam = 1030e-9, w = U.W(lam);
            double ns = 1.5, nAr = Math.Sqrt(ns);
            var arMat = new Material { Name = "ar", Index = new ConstantIndex(nAr) };
            var ar = ThinFilm.Evaluate(w, 0, 1.0, ns, new[] { new FilmLayer { Material = arMat, ThicknessM = lam / (4 * nAr) } });
            Assert.Less(ar.Rs.Magnitude * ar.Rs.Magnitude, 1e-20, "ideal single-layer AR");
            var H = new Material { Name = "H", Index = new ConstantIndex(2.1) };
            var L = new Material { Name = "L", Index = new ConstantIndex(1.45) };
            var layers = CoatingFactory.ExpandDesign("(HL)^10 H", new Dictionary<char, Material> { { 'H', H }, { 'L', L } }, lam, "test");
            Assert.Equal(21, layers.Count, "layers");
            var hr = ThinFilm.Evaluate(w, 0, 1.0, ns, layers);
            double y = Math.Pow(2.1 / 1.45, 20) * 2.1 * 2.1 / ns;
            double rExpected = Math.Pow((1 - y) / (1 + y), 2);
            Assert.Close(rExpected, hr.Rs.Magnitude * hr.Rs.Magnitude, 1e-9, 0, "Bragg reflectance");
            var obl = ThinFilm.Evaluate(w * 1.05, 0.5, 1.0, ns, layers);
            Assert.Close(1.0, obl.Rp.Magnitude * obl.Rp.Magnitude + obl.Tp.Magnitude * obl.Tp.Magnitude, 1e-10, 0, "lossless stack R+T (p, oblique)");
            // a reflection off a Bragg mirror has a positive group delay (light penetrates the stack)
            double h = w * 1e-5;
            var a1 = ThinFilm.Evaluate(w - h, 0, 1.0, ns, layers).Rs; var a2 = ThinFilm.Evaluate(w + h, 0, 1.0, ns, layers).Rs;
            double gd = (a2 / a1).Phase / (2 * h);
            Assert.Greater(gd, 0, "Bragg mirror group delay sign");
            Assert.Less(gd, 20e-15, "Bragg mirror group delay magnitude");
        }

        [Test(Reference = "Treacy, IEEE JQE 5, 454 (1969)", Tolerance = "0.01 %")]
        public void GratingPair_GroupDelayMatchesTreacy()
        {
            double lpm = 1500e3, aoi = 50 * Math.PI / 180, G = 0.2;
            var t = new GratingPairTerm(lpm, aoi, G, -1, 1, 1.0);
            var d = t.DispersionAt(U.W(1030e-9));
            double gddT = GratingPairTerm.TreacyGdd(1030e-9, lpm, aoi, G, -1);
            Assert.Close(gddT, d.GddS2, 1e-4, 0, "GDD vs Treacy");
            Assert.Less(d.GddS2, 0, "compressor GDD negative");
            Assert.Greater(d.TodS3, 0, "compressor TOD positive");
        }
    }
}
