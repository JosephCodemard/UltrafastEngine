// SplitterGlassTests.cs -- glass in beamSplitter and polarizingSplitter (material, transmittedThicknessM, reflectedThicknessM).
//
// A lossless 2x2 split is linear and the glass of each path is linear, so the glass may be applied after the split: a splitter
// with glass must give exactly what the same splitter without glass gives followed by a plate (ideal AR faces, normal incidence)
// in each arm, in the field and in the path ledger that sets interference where the arms meet again. Without the keys the
// element runs as before.

using System;
using System.Linq;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Branched systems: glass in beam splitters and polarising splitters")]
    public class SplitterGlassTests
    {
        private const double Lam = 1030e-9, Lt = 3e-3, Lr = 1e-3;

        private static SystemDescription Branched(params JsonObject[] elements) { var s = Sys(elements); s.Topology = SystemGraph.Branched; return s; }
        private static Specs.RunSpec Run()
        {
            // 30 deg linear polarisation: both x and y carry light, so the polarising splitter's two ports are tested
            var p = Gauss(Lam, 30e-15, 1e-9); p.Set("polarization", O("angleDeg", 30.0));
            return Spec(p, O("grid", O("n", 1 << 15, "windowS", 32e-12)));
        }
        private static JsonObject Plate(string id, double thicknessM, JsonValue next) =>
            WithNext(O("id", id, "type", "plate", "material", "FusedSilica", "thicknessM", thicknessM, "coating", O("kind", "ar", "R", 0.0)), next);
        private static JsonObject WithNext(JsonObject o, JsonValue next) { if (next != null) o.Set("next", next); return o; }
        private static JsonObject Splitter(string id, string type, JsonValue next, bool glass, params object[] kv)
        {
            var o = WithNext(O(new object[] { "id", id, "type", type }.Concat(kv).ToArray()), next);
            if (type == "beamSplitter") o.Set("reflectivity", 0.4);
            else { o.Set("transmissionP", 0.9); o.Set("reflectivityS", 0.8); }
            if (glass) { o.Set("material", "FusedSilica"); o.Set("transmittedThicknessM", Lt); o.Set("reflectedThicknessM", Lr); }
            return o;
        }

        /// <summary>Largest difference of the two fields' spectra, over the largest amplitude.</summary>
        private static double Diff(PulseField a, PulseField b)
        {
            double max = 0, d = 0;
            for (int i = 0; i < a.Grid.N; i++)
            {
                max = Math.Max(max, Math.Max(a.Ax[i].Magnitude, a.Ay[i].Magnitude));
                d = Math.Max(d, Math.Max((a.Ax[i] - b.Ax[i]).Magnitude, (a.Ay[i] - b.Ay[i]).Magnitude));
            }
            return d / max;
        }

        [Test(Reference = "linearity: glass after a lossless 2x2 split equals a plate in each arm (Mach-Zehnder output, interference included)", Tolerance = "1e-12")]
        public void SplitterWithGlass_EqualsTheSplitterAndAPlateInEachArm_InAMachZehnder()
        {
            foreach (var type in new[] { "beamSplitter", "polarizingSplitter" })
            {
                // 3 mm and 1 mm of fused silica: 2 mm of difference, 9.7 ps of group delay and 38 fs^2 of GDD between the arms (inside the 32 ps window), recombined on a 50:50 splitter
                var glass = Go(Branched(Splitter("bs1", type, O("c", "bs2:1", "d", "bs2:2"), true),
                    O("id", "bs2", "type", "beamSplitter", "next", O("c", "end", "d", "virtual"))), Run());
                var plates = Go(Branched(Splitter("bs1", type, O("c", "pc", "d", "pd"), false), Plate("pc", Lt, "bs2:1"), Plate("pd", Lr, "bs2:2"),
                    O("id", "bs2", "type", "beamSplitter", "next", O("c", "end", "d", "virtual"))), Run());
                var none = Go(Branched(Splitter("bs1", type, O("c", "bs2:1", "d", "bs2:2"), false),
                    O("id", "bs2", "type", "beamSplitter", "next", O("c", "end", "d", "virtual"))), Run());
                // rounding of the large absolute glass phases (2.6e4 rad) is the floor; the operations are the same as a plate's
                Assert.Less(Diff(plates.Output, glass.Output), 1e-12, type + ": output field against plates in the arms");
                Assert.Less(Diff(plates.At["bs2#d"], glass.At["bs2#d"]), 1e-12, type + ": the other output too");
                Assert.Greater(Diff(none.Output, glass.Output), 1e-2, type + ": and the glass matters");
                Assert.Close(plates.Graph.GetDouble("energyOutJ", 0), glass.Graph.GetDouble("energyOutJ", 0), 1e-12, 0, type + ": energy at the end");
            }
        }

        [Test(Reference = "linearity: a Sequential splitter with glass equals the splitter followed by a plate, on either continuing port", Tolerance = "1e-12")]
        public void SequentialSplitterWithGlass_EqualsTheSplitterAndAPlate_OnEachPort()
        {
            foreach (var type in new[] { "beamSplitter", "polarizingSplitter" })
                foreach (var port in new[] { "c", "d" })
                {
                    double l = port == "c" ? Lt : Lr;
                    var glass = Go(Sys(Splitter("bs", type, null, true, "output", port)), Run());
                    var plate = Go(Sys(Splitter("bs", type, null, false, "output", port), Plate("p", l, null)), Run());
                    Assert.Less(Diff(plate.Output, glass.Output), 1e-12, type + " port " + port + ": field");
                    var rg = glass.Records.Last(); var rp = plate.Records.Last();
                    Assert.Close(rp.System.GddS2, rg.System.GddS2, 1e-9, 0, type + " port " + port + ": reported GDD");
                    Assert.Close(rp.ArrivalS, rg.ArrivalS, 1e-12, 0, type + " port " + port + ": arrival time");
                }
        }

        [Test(Reference = "superposition: a fed beam on port b in a Sequential system equals the same splitter in a Branched system", Tolerance = "1e-9")]
        public void FedBeamThroughTheGlass_InASequentialSystem_EqualsTheBranchedSystem()
        {
            // the fed beam on b reaches c through the reflected glass, the main beam through the transmitted glass: they must add in one frame
            // same carrier as the main beam: with a detuned fed beam the Sequential and Branched systems already differ without glass
            // (a separate question, listed in docs/design/history.md under WP P1), so the reframing is checked at one carrier
            var inputs = O("b", O("pulse", Gauss(Lam, 30e-15, 0.5e-9), "delayS", 0.0, "coherentWith", "input"));
            var seq = Go(Sys(Splitter("bs", "beamSplitter", null, true, "inputs", inputs)), Run());
            var br = Go(Branched(Splitter("bs", "beamSplitter", O("c", "end", "d", "virtual"), true, "inputs", inputs)), Run());
            // the Branched system merges the two packets in the frame halfway between their delays, the Sequential one keeps the main
            // beam's frame: the stored fields differ by that common frame, so compare the spectrum, whose fringes (~1e4 rad of relative
            // glass phase, 9.7 ps of relative delay) hold the relative phase; a few ulps of those phases set the floor
            // the Branched merge puts the two carriers on a grid spanning both: compare bin by bin at the same absolute frequency
            var a = br.Output.SpectralIntensity(); var b = seq.Output.SpectralIntensity();
            var wa = br.Output.Grid.OmegaAbsolute; var wb = seq.Output.Grid.OmegaAbsolute; double dw = seq.Output.Grid.DOmega;
            int shift = (int)Math.Round((wb[0] - wa[0]) / dw);
            Assert.Less(Math.Abs(wa[shift] - wb[0]), 1e-6 * dw, "the grids share their frequencies");
            double max = b.Max(), d = 0;
            for (int i = 0; i < b.Length; i++) d = Math.Max(d, Math.Abs(a[i + shift] - b[i]));
            Assert.Less(d / max, 1e-9, "fed beam and main beam add the same way");
            var none = Go(Sys(Splitter("bs", "beamSplitter", null, false, "inputs", inputs)), Run()).Output.SpectralIntensity();
            Assert.Greater(none.Zip(b, (x, y) => Math.Abs(x - y)).Max() / max, 1e-2, "and the glass matters");
        }

        [Test(Reference = "group delay of fused silica, Malitson, JOSA 55, 1205 (1965): n_g L / c through the transmitted glass", Tolerance = "2e-5")]
        public void TransmittedGlass_DelaysThePulse_ByTheGroupIndex()
        {
            double l = Lam * 1e6;
            double N(double um) { double x = um * um; return Math.Sqrt(1 + 0.6961663 * x / (x - 0.0684043 * 0.0684043) + 0.4079426 * x / (x - 0.1162414 * 0.1162414) + 0.8974794 * x / (x - 9.896161 * 9.896161)); }
            // the catalogue's Malitson index is relative to air (referenceMedium "air"); the engine works in vacuum: n_abs = n n_air, with
            // n_air from Ciddor's standard-air formula (Appl. Opt. 35, 1566 (1996)); the air conditions may differ by ~1e-5 in n_air
            double Air(double um) { double s2 = 1 / (um * um); return 1 + 0.05792105 / (238.0185 - s2) + 0.00167917 / (57.362 - s2); }
            double Nv(double um) => N(um) * Air(um);
            double h = 1e-4, ng = Nv(l) - l * (Nv(l + h) - Nv(l - h)) / (2 * h);
            var glass = Go(Sys(Splitter("bs", "beamSplitter", null, true)), Run());
            var bare = Go(Sys(Splitter("bs", "beamSplitter", null, false)), Run());
            Assert.Close(ng * Lt / C, glass.Records[0].ArrivalS - bare.Records[0].ArrivalS, 2e-5, 0, "group delay of 3 mm");
        }

        [Test(Reference = "linearity: delayed light (grid.tail) crosses the splitter's glass as it would a plate after it", Tolerance = "1e-9")]
        public void DelayedTail_CrossesTheGlass_InSequentialAndBranchedSystems()
        {
            var run = Spec(Gauss(Lam, 30e-15, 1e-9), O("grid", O("n", 1 << 13, "windowS", 4e-12, "tail", true)));
            JsonObject Etalon(JsonValue next) => WithNext(O("id", "e", "type", "plate", "material", "FusedSilica", "thicknessM", 2e-3, "coating", O("kind", "ar", "R", 0.04), "ghosts", true), next);
            foreach (bool branched in new[] { false, true })
            {
                SystemDescription S(params JsonObject[] els) { var s = Sys(els); if (branched) s.Topology = SystemGraph.Branched; return s; }
                var glass = Go(S(Etalon(branched ? "bs" : null), Splitter("bs", "beamSplitter", branched ? O("c", "end", "d", "virtual") : null, true)), run);
                var plate = Go(S(Etalon(branched ? "bs" : null), Splitter("bs", "beamSplitter", branched ? O("c", "p", "d", "virtual") : null, false), Plate("p", Lt, branched ? "end" : null)), run);
                string m = branched ? "Branched" : "Sequential";
                Assert.Greater(plate.TailLedger.TailEnergyJ, 0, m + ": there is delayed light");
                Assert.Close(plate.TailLedger.TailEnergyJ, glass.TailLedger.TailEnergyJ, 1e-9, 0, m + ": tail energy");
                Assert.Less(Diff(plate.TailLedger.TailField(0), glass.TailLedger.TailField(0)), 1e-9, m + ": tail field");
            }
        }

        [Test]
        public void SplitterWithoutGlass_IsUnchanged_AndBadGlassKeysAreRefused()
        {
            var bare = Go(Sys(Splitter("bs", "beamSplitter", null, false, "output", "d")), Run());
            var zero = Go(Sys(Splitter("bs", "beamSplitter", null, false, "output", "d", "material", "FusedSilica", "transmittedThicknessM", 0.0)), Run());
            for (int i = 0; i < bare.Output.Grid.N; i++)
                if (bare.Output.Ax[i] != zero.Output.Ax[i] || bare.Output.Ay[i] != zero.Output.Ay[i]) throw new Exception("zero glass must leave the field bit for bit");
            Assert.True(zero.Records[0].Notes.Any(n => n.Contains("adds no dispersion")), "a material without a path length is noted");
            Assert.Throws<UltrafastEngine.Models.ModelException>(() => Go(Sys(Splitter("bs", "beamSplitter", null, false, "transmittedThicknessM", 1e-3)), Run()), "a path length without a material");
            Assert.Throws<UltrafastEngine.Models.ModelException>(() => Go(Sys(Splitter("bs", "beamSplitter", null, false, "material", "FusedSilica", "reflectedThicknessM", -1e-3)), Run()), "a negative path length");
        }
    }
}
