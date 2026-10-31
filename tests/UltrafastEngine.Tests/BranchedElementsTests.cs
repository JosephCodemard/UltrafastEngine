using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Optics;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>The splitter family, carrier phases of active elements, mode overlap, carried profiles meeting, ports of crystals.</summary>
    [TestArea("Branched systems: splitter family, Gouy and crystal phases, mode overlap, profiles meeting, crystal ports")]
    public class BranchedElementsTests
    {
        private const double Lam = 1030e-9, C = PhysicalConstants.SpeedOfLightMPerS;

        private static SystemDescription Branched(params JsonObject[] elements) { var s = Sys(elements); s.Topology = SystemGraph.Branched; return s; }
        private static Specs.RunSpec Cw(JsonObject extra = null) => Spec(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0), extra);
        private static Specs.RunSpec Short(JsonObject extra = null) => Spec(Gauss(Lam, 100e-15, 1e-9), extra);
        private static JsonObject El(string id, string type, JsonValue next, params object[] kv) => O(new object[] { "id", id, "type", type, "next", next }.Concat(kv).ToArray());
        private static double Closure(RunResult r) => Math.Abs(r.Graph.GetDouble("energyClosureJ", 1)) / r.Graph.GetDouble("energyInJ", 1);

        [Test(Reference = "Gouy phase of a Gaussian beam: -atan(z / z_R), from -z_R to +z_R is -pi / 2", Tolerance = "1e-9")]
        public void BeamState_GouyPhase_FollowsTheArctangent()
        {
            double w0 = 1e-3, zr = Math.PI * w0 * w0 / Lam;
            var b = BeamState.FromGaussianSpec(O("w0M", w0, "zWaistM", zr), Lam);
            b.Propagate(2 * zr, 1);
            Assert.Close(-Math.PI / 2, b.GouyRad, 1e-9, 0, "through the waist");
        }

        [Test(Reference = "Michelson with a symmetric splitter: equal arms send all the light back, none to the output", Tolerance = "1e-9")]
        public void Michelson_EqualArms_AreDarkAtTheOutput_AndFringeWithPeriodHalfLambda()
        {
            foreach (double extra in new[] { 0.0, Lam / 4 / 2, Lam / 4 })
            {
                var r = Go(Branched(
                    El("bs", "beamSplitter", O("c", "m1", "d", "m2")),
                    El("m1", "gap", "bsBack:1", "material", "Vacuum", "lengthM", 0.2),
                    El("m2", "gap", "bsBack:2", "material", "Vacuum", "lengthM", 0.2 + 2 * extra),
                    El("bsBack", "beamSplitter", O("c", "virtual", "d", "end"), "sameAs", "bs", "side", 2)), Cw());
                double wc = r.Output.Grid.OmegaCenter, phi = wc * 2 * extra / C;
                Assert.Close((1 - Math.Cos(phi)) / 2, r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-9, 1e-12, "output at mirror shift " + extra);
            }
        }

        [Test(Reference = "Malus: an input at 30 deg sends cos^2 to p and sin^2 to s", Tolerance = "1e-9")]
        public void PolarizingSplitter_SplitsByPolarisation()
        {
            double a = 30 * Math.PI / 180;
            var pulse = Gauss(Lam, 100e-15, 1e-9); pulse.Set("polarization", O("angleDeg", 30.0));
            var r = Go(Sys(O("id", "p", "type", "polarizingSplitter", "output", "c")), Spec(pulse));
            Assert.Close(Math.Pow(Math.Cos(a), 2), r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-9, 0, "p through");
            Assert.Close(Math.Pow(Math.Sin(a), 2) * r.Compiled.Input.TotalEnergyJ(), r.At["p#d"].TotalEnergyJ(), 1e-9, 0, "s reflected");
        }

        [Test]
        public void LeakMirror_PassesItsTransmission_ToThePort()
        {
            var r = Go(Sys(O("id", "m", "type", "leakMirror", "transmission", 0.02, "leakGddFs2", 500.0)), Short());
            Assert.Close(0.98, r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-9, 0, "reflected");
            Assert.Close(0.02 * r.Compiled.Input.TotalEnergyJ(), r.At["m#leak"].TotalEnergyJ(), 1e-9, 0, "leak");
            Assert.Greater(Results.PulseMetrics.Summarize(r.At["m#leak"]).FwhmS, Results.PulseMetrics.Summarize(r.Output).FwhmS, "the leak has the substrate's GDD");
        }

        [Test]
        public void Dichroic_OutputsAddUpAtEachWavelength()
        {
            var r = Go(Sys(O("id", "d", "type", "dichroic", "edgeWavelengthM", Lam, "edgeWidthM", 2e-9, "output", "c")), Spec(Gauss(Lam, 30e-15, 1e-9)));
            var a = r.Compiled.Input; var c = r.Output; var d = r.At["d#d"];
            for (int i = 0; i < a.Grid.N; i += 37)
                Assert.Close(a.Ax[i].Magnitude * a.Ax[i].Magnitude, c.Ax[i].Magnitude * c.Ax[i].Magnitude + d.Ax[i].Magnitude * d.Ax[i].Magnitude, 1e-9, 1e-30, "bin " + i);
            Assert.Close(0.5, c.TotalEnergyJ() / a.TotalEnergyJ(), 0.06, 0, "about half each side of an edge at the centre (the edge is in wavelength, the pulse symmetric in frequency)");
        }

        private static SystemDescription ArmsMeet(JsonObject bArm) => Branched(
            El("bs1", "beamSplitter", O("c", "a", "d", "b")),
            El("a", "delay", "bs2:1", "delayS", 0.0), bArm,
            El("bs2", "beamSplitter", O("c", "end", "d", "virtual")));

        [Test(Reference = "overlap of Gaussians in a tilt: exp(-(k theta w)^2 / 8), and in an offset: exp(-d^2 / (2 w^2))", Tolerance = "1e-6")]
        public void TrackedBeams_MeetWithTheirModeOverlap()
        {
            double w = 1e-3, k = 2 * Math.PI / Lam, th = 2 / (k * w), d = 0.7 * w;
            var beam = O("inputBeam", O("kind", "gaussian", "w0M", w));
            var tilted = Go(ArmsMeet(El("b", "delay", "bs2:2", "delayS", 0.0, "tiltXRad", th)), Cw(beam));
            var offset = Go(ArmsMeet(El("b", "delay", "bs2:2", "delayS", 0.0, "offsetXM", d)), Cw(beam));
            // arms in phase at the first port: c holds nothing of an equal pair (a - b) / 2 unless the overlap is below 1
            double Expect(double o) => Math.Pow(1 - o, 2) / 4;
            double e = tilted.Compiled.Input.TotalEnergyJ();
            Assert.Close(Expect(Math.Exp(-Math.Pow(k * th * w, 2) / 8)), tilted.Output.TotalEnergyJ() / e, 1e-6, 0, "tilt");
            Assert.Close(Expect(Math.Exp(-d * d / (2 * w * w))), offset.Output.TotalEnergyJ() / e, 1e-6, 0, "offset");
            Assert.Less(Closure(tilted), 1e-9, "mismatch is booked");
        }

        [Test]
        public void TiltWithNoTrackedBeam_IsAnError_NotAPerfectOverlap()
        {
            bool threw = false;
            try { Go(ArmsMeet(El("b", "delay", "bs2:2", "delayS", 0.0, "tiltXRad", 1e-3)), Cw()); } catch (Models.ModelException) { threw = true; }
            Assert.True(threw, "an error");
        }

        [Test(Reference = "two beams at an angle: fringe period lambda / theta", Tolerance = "2 %")]
        public void CarriedProfiles_Meet_WithTiltFringes()
        {
            double th = 1e-3;
            var grid = O("model", "cartesian", "nx", 1024, "ny", 1, "halfWidthM", 10e-3, "spectralSamples", 1);
            var src = (JsonObject)JsonValue.Parse(grid.ToJson(false)); src.Set("initialProfile", O("kind", "gaussian", "radiusM", 4e-3));
            var r = Go(Branched(
                El("s", "gap", "bs1", "material", "Vacuum", "lengthM", 0.0, "transverse", src),
                El("bs1", "beamSplitter", O("c", "a", "d", "b")),
                El("a", "delay", "bs2:1", "delayS", 0.0), El("b", "delay", "bs2:2", "delayS", 0.0, "tiltXRad", th),
                El("bs2", "beamSplitter", O("c", "end", "d", "virtual"))), Cw());
            var p = r.Output.Profile; var basis = (Transverse.CartesianBasis)p.Basis;
            int s = p.MainSample, nf = p.Omega.Length;
            var I = Enumerable.Range(0, basis.M).Select(m => { var z = p.Data[m * nf + s]; return z.Real * z.Real + z.Imaginary * z.Imaginary; }).ToArray();
            var minima = Enumerable.Range(1, basis.M - 2).Where(m => Math.Abs(basis.X[m]) < 4e-3 && I[m] < I[m - 1] && I[m] <= I[m + 1]).Select(m => basis.X[m]).ToArray();
            Assert.Close(Lam / th, (minima.Last() - minima.First()) / (minima.Length - 1), 0.02, 0, "fringe period");
        }

        [Test(Reference = "carrier phase of a harmonic: twice the fundamental's, plus the crystal's", Tolerance = "1e-6")]
        public void Crystal_HarmonicPhase_DoublesThePathsPhase()
        {
            JsonObject Shg(string next) => O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 20e-6, "cut", "auto", "beam", O("kind", "gaussian", "w0M", 100e-6),
                "interaction", O("process", "shg", "type", "I"), "output", "sh", "next", next);
            var run = Short();
            var bare = Go(Branched(Shg("end")), run);
            double L = 1e-3, n = 1.5;
            var s = Branched(El("g", "bulk", "x", "material", "flat", "lengthM", L), Shg("end"));
            s.Materials.Set("flat", FlatMaterial(0, n));
            var r = Go(s, run);
            double wFund = r.Compiled.Grid.OmegaCenter;
            Assert.True(r.Graph.GetBool("phaseKnown", false), "the phase is stated");
            Assert.Close(2 * n * wFund * L / C, r.Graph.GetDouble("pathPhaseRad", 0) - bare.Graph.GetDouble("pathPhaseRad", 0), 1e-6, 0, "twice the glass");
        }

        [Test]
        public void CrystalByproduct_IsAPort_AndTheBudgetCloses()
        {
            var r = Go(Branched(
                O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 1e-3, "cut", "auto", "beam", O("kind", "gaussian", "w0M", 50e-6),
                  "interaction", O("process", "shg", "type", "I"), "output", "sh", "next", O("out", "end", "fundamental", "left")),
                El("left", "delay", "virtual", "delayS", 0.0)), Spec(Gauss(Lam, 100e-15, 1e-6)));
            Assert.Greater(r.Output.TotalEnergyJ(), 1e-9, "second harmonic ends the system");
            Assert.True(r.At.ContainsKey("left#out"), "the fundamental continued");
            Assert.Less(Closure(r), 1e-4, "the budget closes");
        }

        [Test(Reference = "linear superposition: the tail splits and is booked with the main field", Tolerance = "1e-9")]
        public void DelayedTail_FollowsEachBranch()
        {
            JsonObject Plate(JsonValue next) => O("id", "m", "type", "plate", "material", "FusedSilica", "thicknessM", 5e-3, "coating", O("kind", "ar", "R", 0.01), "ghosts", true, "next", next);
            var run = Spec(Gauss(Lam, 100e-15, 1e-9), O("grid", O("tail", true)));
            SystemDescription WithAr(SystemDescription s) { s.Coatings.Set("ar", O("kind", "ar")); return s; }
            var bare = Go(WithAr(Branched(Plate("end"))), run);
            var split = Go(WithAr(Branched(Plate("bs"), El("bs", "beamSplitter", O("c", "end", "d", "virtual"), "reflectivity", 0.3))), run);
            Assert.Greater(bare.TailLedger.TailEnergyJ, 0, "there is delayed light");
            Assert.Close(0.7 * bare.TailLedger.TailEnergyJ, split.TailLedger.TailEnergyJ, 1e-9, 0, "the tail takes the port's share");
        }

        [Test(Reference = "f-2f: the fringe phase at the overlap moves with the CEP one to one", Tolerance = "3 %")]
        public void F2f_FringesFollowTheCep()
        {
            // a 3 fs pulse is broad enough for its blue wing to meet the red wing of its second harmonic
            double Fringe(double cep)
            {
                var pulse = O("shape", "gaussian", "centerWavelengthM", 800e-9, "durationFwhmS", 3e-15, "energyJ", 1e-9, "cepRad", cep, "polarization", "45");
                var r = Go(Branched(
                    O("id", "bs", "type", "beamSplitter", "next", O("c", "x", "d", "ref")),
                    O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 5e-6, "cut", "auto", "beam", O("kind", "gaussian", "w0M", 20e-6),
                      "interaction", O("process", "shg", "type", "I"), "output", "sh", "next", "d2:1"),
                    El("ref", "delay", "d2:2", "delayS", 40e-15),
                    El("d2", "beamSplitter", O("c", "end", "d", "virtual"))), Spec(pulse, O("grid", O("n", 1 << 15, "windowS", 2e-12))));
                var g = r.Output.Grid;
                double w = 2 * Math.PI * C / 470e-9;
                int i = Enumerable.Range(0, g.N).OrderBy(k => Math.Abs(g.OmegaAbsolute[k] - w)).First();
                var c = r.Output.Ay[i];   // type I: the harmonic is y, so the reference's y part is what it beats with
                return c.Real * c.Real + c.Imaginary * c.Imaginary;
            }
            double[] phases = Enumerable.Range(0, 8).Select(k => k * Math.PI / 4).ToArray();
            var s = phases.Select(Fringe).ToArray();
            double re = s.Zip(phases, (y, p) => y * Math.Cos(p)).Sum(), im = s.Zip(phases, (y, p) => y * Math.Sin(p)).Sum(), mean = s.Average();
            Assert.Greater(Math.Sqrt(re * re + im * im) / 4 / mean, 0.1, "the signal at one cycle per 2 pi of CEP");
            double second = Math.Abs(s.Select((y, k) => y * Math.Cos(2 * phases[k])).Sum());
            Assert.Less(second, 0.03 * Math.Sqrt(re * re + im * im), "and at no other period");
        }
    }
}
