using System;
using System.Linq;
using UltrafastEngine.Engine;
using UltrafastEngine.Numerics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    /// <summary>Branched systems: ports, the graph compiler's checks, interference from the path ledger, and loops.</summary>
    [TestArea("Branched systems: ports, graph compiler, interference, loops")]
    public class BranchingTests
    {
        private const double Lam = 1030e-9, C = PhysicalConstants.SpeedOfLightMPerS;
        private static double W0 => 2 * Math.PI * C / Lam;

        private static SystemDescription Branched(params JsonObject[] elements)
        {
            var s = Sys(elements);
            s.Topology = SystemGraph.Branched;
            return s;
        }

        private static Specs.RunSpec Spec1() => Spec(Gauss(Lam, 100e-15, 1e-9));
        private static Specs.RunSpec SpecCw() => Spec(O("shape", "cw", "centerWavelengthM", Lam, "powerW", 1.0));
        private static JsonObject Bs(string id, double r, JsonValue next) => O("id", id, "type", "beamSplitter", "reflectivity", r, "next", next);
        private static JsonObject Vac(string id, double l, JsonValue next) => O("id", id, "type", "gap", "material", "Vacuum", "lengthM", l, "next", next);

        private static JsonException Fails(Action a)
        {
            try { a(); } catch (JsonException e) { return e; } catch (Models.ModelException e) when (e.InnerException is JsonException j) { return j; }
            throw new InvalidOperationException("expected an error");
        }

        private static SystemDescription MachZehnder(double armA, double armB) => Branched(
            Bs("bs1", 0.5, O("c", "armA", "d", "armB")),
            Vac("armA", armA, "bs2:1"), Vac("armB", armB, "bs2:2"),
            Bs("bs2", 0.5, O("c", "end", "d", "virtual")));

        [Test(Reference = "carrier phase of glass, n omega L / c", Tolerance = "1e-9")]
        public void PathLedger_KeepsTheCarrierPhaseTheFieldDoesNotCarry()
        {
            var s = Branched(O("id", "g", "type", "bulk", "material", "flat", "lengthM", 1e-3, "next", "end"));
            s.Materials.Set("flat", FlatMaterial(0, 1.45));
            var r = Go(s, Spec1());
            Assert.Close(1.45 * r.Output.Grid.OmegaCenter * 1e-3 / C, r.Graph.GetDouble("pathPhaseRad", 0), 1e-9, 0, "phase of 1 mm of silica");
        }

        [Test(Reference = "Mach-Zehnder: output (1 - cos phi) / 2 of the input", Tolerance = "1e-9")]
        public void MachZehnder_FringesFollowThePathDifference()
        {
            foreach (double phi in new[] { 0.0, 0.7, Math.PI / 2, 2.5, Math.PI })
            {
                var w0 = W0;
                var r = Go(MachZehnder(0.2, 0.2 + phi * C / w0), SpecCw());
                double wc = r.Output.Grid.OmegaCenter, ph = wc * phi / w0 / C * C;
                Assert.Close(r.Compiled.Input.TotalEnergyJ() * (1 - Math.Cos(phi * wc / w0)) / 2, r.Output.TotalEnergyJ(), 1e-9, 1e-15, "fringe at " + phi);
                Assert.Less(Math.Abs(r.Graph.GetDouble("energyClosureJ", 1)), 1e-12 * r.Compiled.Input.TotalEnergyJ(), "the budget closes");
            }
        }

        [Test(Reference = "spectral interference, |1 - exp(i omega tau)|^2 / 4", Tolerance = "1e-6")]
        public void MachZehnder_SpectralFringes_PinTheSignOfPhaseAgainstDelay()
        {
            // a delay of 300 fs on a 100 fs pulse: S(omega) = S0 (1 - cos(omega tau)) / 2 at the absolute frequency. A wrong relative
            // sign between the carrier phase and the delay would move these fringes.
            double tau = 300e-15;
            var s = Branched(
                Bs("bs1", 0.5, O("c", "armA", "d", "armB")),
                O("id", "armA", "type", "delay", "delayS", 0.0, "next", "bs2:1"), O("id", "armB", "type", "delay", "delayS", tau, "next", "bs2:2"),
                Bs("bs2", 0.5, O("c", "end", "d", "virtual")));
            var r = Go(s, Spec(Gauss(Lam, 100e-15, 1e-9), O("grid", O("n", 1 << 13, "windowS", 20e-12))));
            var g = r.Output.Grid;
            double peak = r.Compiled.Input.Ax.Max(z => z.Magnitude * z.Magnitude);
            for (int i = 0; i < g.N; i++)
            {
                double s0 = Math.Pow(r.Compiled.Input.Ax[i].Magnitude, 2);
                if (s0 < 1e-3 * peak) continue;
                double w = g.OmegaCenter + g.OmegaOffset[i];
                Assert.Close(s0 * (1 - Math.Cos(w * tau)) / 2, Math.Pow(r.Output.Ax[i].Magnitude, 2), 1e-6, 1e-6 * peak, "fringe at bin " + i);
            }
        }

        [Test]
        public void ArmsFurtherApartThanTheWindow_StaySeparatePackets_AndDoNotInterfere()
        {
            var r = Go(MachZehnder(0.2, 3.2), Spec1());
            Assert.Close(0.25, r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-6, 0, "one of two packets of a quarter each");
            Assert.Less(Math.Abs(r.Graph.GetDouble("energyClosureJ", 1)), 1e-12 * r.Compiled.Input.TotalEnergyJ(), "the budget closes");
        }

        [Test(Reference = "Airy transmission of a Fabry-Perot, (1-R)^2 / (1 + R^2 - 2 R cos delta)", Tolerance = "1e-5")]
        public void FabryPerot_LoopGivesTheAiryFunction()
        {
            foreach (double delta in new[] { 0.0, 0.1, 0.5, 1.5, Math.PI })
            {
                double R = 0.8, L = 0.01 + delta / 2 * C / W0;
                // m1 sends light into the gap; m2 returns it through its own port b, so both mirrors reflect the inside wave alike
                var s = Branched(
                    O("id", "m1", "type", "beamSplitter", "reflectivity", R, "next", O("c", "forth", "d", "virtual"), "loop", O("maxRoundTrips", 2000, "tolerance", 1e-16)),
                    Vac("forth", L, "m2:2"),
                    Bs("m2", R, O("c", "back", "d", "end")),
                    Vac("back", L, "m1:2"));
                var r = Go(s, SpecCw());
                double wc = r.Output.Grid.OmegaCenter, d = 2 * wc * L / C;
                double airy = Math.Pow(1 - R, 2) / (1 + R * R - 2 * R * Math.Cos(d));
                Assert.Close(airy, r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-5, 0, "T at delta " + delta);
                Assert.Less(Math.Abs(r.Graph.GetDouble("energyClosureJ", 1)), 1e-7 * r.Compiled.Input.TotalEnergyJ(), "the budget closes");
            }
        }

        [Test(Reference = "ring ring-down: pass n carries R^(n-1) T^2", Tolerance = "1e-9")]
        public void Ring_RoundTripLongerThanTheWindow_GivesAPulseTrain()
        {
            double R = 0.5;
            var s = Branched(
                O("id", "oc", "type", "beamSplitter", "reflectivity", R, "next", O("c", "leg", "d", "end"), "loop", O("maxRoundTrips", 60, "tolerance", 1e-12)),
                Vac("leg", 3.0, "oc:2"));
            var r = Go(s, Spec1());
            // the strongest packet is the first reflection or first transmission, here equal; all others are booked at end
            Assert.Close(R, r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-9, 0, "first reflection R");
            Assert.Less(Math.Abs(r.Graph.GetDouble("energyClosureJ", 1)), 1e-9 * r.Compiled.Input.TotalEnergyJ(), "a lossless ring returns everything");
        }

        [Test]
        public void SteadyStateLoop_SettlesOnlyWhenShapeAndEnergyRepeat()
        {
            // saturated gain balancing an output coupler: a lossless linear network cannot hold light, so a loop needs gain to settle
            var s = Branched(
                Bs("oc", 0.5, O("c", "leg", "d", "end")).Set("loop", O("kind", "steadyState", "maxRoundTrips", 400, "tolerance", 1e-4)),
                Vac("leg", 3.0, "g"),
                O("id", "g", "type", "gain", "medium", "Yb:YAG", "lengthM", 0.003, "inversion", O("smallSignalGain", 3.0), "spm", false, "modeRadiusM", 50e-6, "next", "oc:2"));
            var r = Go(s, Spec(Gauss(Lam, 3e-12, 1e-6)));
            Assert.True(r.Records.First(x => x.Id == "oc").Values["loopConverged"] == 1, "energy and shape repeat");
            Assert.Greater(r.Output.TotalEnergyJ(), 0.01 * r.Compiled.Input.TotalEnergyJ(), "the settled pass still holds light");
            // an output coupler makes it decay: the shape repeats from the start, but this is not a steady state
            var leaky = Branched(
                O("id", "oc", "type", "beamSplitter", "reflectivity", 0.5, "next", O("c", "leg", "d", "end"), "loop", O("kind", "steadyState", "maxRoundTrips", 50, "tolerance", 1e-9)),
                Vac("leg", 3.0, "oc:2"));
            r = Go(leaky, Spec1());
            Assert.True(r.Records[0].Values["loopConverged"] == 0, "a decaying loop has not settled");
            Assert.Less(Math.Abs(r.Graph.GetDouble("energyClosureJ", 1)), 1e-9 * r.Compiled.Input.TotalEnergyJ(), "the budget still closes");
        }

        [Test]
        public void TwoSourcesOnOnePort_AreAnError_NamingThePort()
        {
            var e = Fails(() => Go(Branched(
                Bs("bs", 0.5, O("c", "a", "d", "b")), Vac("a", 0.1, "x"), Vac("b", 0.1, "x"), Vac("x", 0.1, "end")), Spec1()));
            Assert.True(e.Message.Contains("'x'") && e.Message.Contains("2 sources"), e.Message);
        }

        [Test]
        public void Targets_ByNumberNameAndDict_AreTheSame()
        {
            foreach (JsonValue t in new JsonValue[] { "bs2:2", "bs2:b", O("id", "bs2", "port", 2), O("id", "bs2", "port", "b") })
            {
                var g = SystemGraph.Build(Branched(Vac("g", 0.1, t), Bs("bs2", 0.5, O("c", "end", "d", "virtual"))));
                Assert.Equal("b", g.Edges.First(x => x.From == "g").ToPort, "port b");
            }
        }

        [Test]
        public void UnusedPorts_RaiseAlerts_AndBecomeVirtual_UnlessDeclared()
        {
            var g = SystemGraph.Build(Branched(O("id", "bs", "type", "beamSplitter", "next", O("c", "end"))));
            Assert.True(g.Warnings.Any(w => w.Contains("Unused output port") && w.Contains("'d'")), "alert on d");
            Assert.True(g.Warnings.Any(w => w.Contains("Unused input port") && w.Contains("'b'")), "alert on b");
            Assert.True(g.VirtualOutputs.Contains("bs#d") && g.VirtualInputs.Contains("bs#b"), "virtual ports added");
            var quiet = SystemGraph.Build(Branched(O("id", "bs", "type", "beamSplitter", "next", O("c", "end", "d", "virtual"), "inputs", O("b", O("virtual", true)))));
            Assert.Equal(0, quiet.Warnings.Count, "declared ports raise no alert");
        }

        [Test]
        public void MissingNext_IsAnAlert_NotASilentFallThrough()
        {
            var g = SystemGraph.Build(Branched(Vac("a", 0.1, "b"), O("id", "b", "type", "gap", "lengthM", 0.1)));
            Assert.True(g.Warnings.Any(w => w.Contains("'b'") && w.Contains("Unused output")), "alert");
            Assert.True(g.Warnings.Any(w => w.Contains("\"end\"")), "alert: nothing reaches end");
        }

        [Test]
        public void GraphErrors_AreReported_AllAtOnce()
        {
            void Bad(string expect, params JsonObject[] els)
            {
                var e = Fails(() => SystemGraph.Build(Branched(els)));
                Assert.True(e.Message.Contains(expect), expect + " in: " + e.Message);
            }
            Bad("not an element id", Vac("a", 0.1, "nope"));
            Bad("input port '3'", Bs("bs", 0.5, O("c", "end", "d", "virtual")), Vac("a", 0.1, "bs:3"));
            Bad("no output port", O("id", "bs", "type", "beamSplitter", "next", O("z", "end")));
            Bad("unreachable", Vac("a", 0.1, "end"), Vac("b", 0.1, "virtual"));
            Bad("no settings", Bs("bs", 0.5, O("c", "a", "d", "end")), Vac("a", 0.1, "bs:2"));
        }

        [Test]
        public void SequentialFile_WithNext_IsRejected_WithAPointer()
        {
            var e = Fails(() => SystemGraph.Build(Sys(Vac("a", 0.1, "end"))));
            Assert.True(e.Message.Contains("topology"), e.Message);
        }

        [Test]
        public void Sequential_SplitterKeepsOnePort_AndBooksTheOther()
        {
            var s = Sys(O("id", "bs", "type", "beamSplitter", "reflectivity", 0.3, "output", "reflected"));
            var r = Go(s, Spec1());
            Assert.Close(0.3, r.Output.TotalEnergyJ() / r.Compiled.Input.TotalEnergyJ(), 1e-12, 0, "reflected port");
            Assert.True(r.At.ContainsKey("bs#c"), "the other port is recorded");
            Assert.Close(0.7 * r.Compiled.Input.TotalEnergyJ(), r.Records[0].LossJ["port c (not tracked)"], 1e-12, 0, "and booked");
        }

        [Test(Reference = "2x2 unitary: c = t a - r b", Tolerance = "1e-9")]
        public void Sequential_VirtualBeam_InterferesAtTheSplitter()
        {
            var pulse = Gauss(Lam, 100e-15, 1e-9);
            var s = Sys(O("id", "bs", "type", "beamSplitter", "reflectivity", 0.5, "inputs", O("b", O("pulse", pulse, "phaseRad", Math.PI, "coherentWith", "input"))));
            var r = Go(s, Spec(pulse));
            Assert.Close(2 * r.Compiled.Input.TotalEnergyJ(), r.Output.TotalEnergyJ(), 1e-9, 0, "all of both beams leaves by c");
        }

        private static JsonObject Crystal(JsonObject aux, JsonValue next) => O("id", "x", "type", "crystal", "crystal", "BBO", "lengthM", 20e-6,
            "interaction", O("process", "sfg", "type", "I"), "cut", "auto", "beam", O("kind", "gaussian", "w0M", 100e-6), "aux", A(aux), "output", "sum", "next", next);

        [Test(Reference = "cross-check: a branch-fed port against the same pulse as an aux beam", Tolerance = "1e-6")]
        public void CrystalPort_FedFromABranch_MatchesTheSameBeamGivenAsAux()
        {
            var pulse = Gauss(Lam, 100e-15, 1e-9);
            var half = Gauss(Lam, 100e-15, 0.5e-9);
            var beam = O("kind", "gaussian", "radiusM", 100e-6);
            var aux = Crystal(O("role", "b", "pulse", half, "beam", beam, "delayS", 0.0), "end"); aux.Remove("next");
            var viaAux = Go(Sys(aux), Spec(half));
            var viaPort = Go(Branched(Bs("bs", 0.5, O("c", "x", "d", "x:b")), Crystal(O("role", "b", "fromPort", true, "beam", beam), "end")), Spec(pulse));
            Assert.Close(viaAux.Output.TotalEnergyJ(), viaPort.Output.TotalEnergyJ(), 1e-6, 0, "sum-frequency energy");
            Assert.Less(Math.Abs(viaPort.Graph.GetDouble("energyClosureJ", 1)), 1e-6 * pulse.GetDouble("energyJ", 1), "the budget closes");
        }

        [Test(Reference = "intensity autocorrelation of a Gaussian: FWHM sqrt 2 tau", Tolerance = "1 %")]
        public void ModelledAutocorrelator_ThinCrystal_GivesTheIdealWidth()
        {
            double tau = 100e-15;
            double[] delay = Enumerable.Range(-20, 41).Select(k => k * 10e-15).ToArray();
            var signal = delay.Select(d => Go(Branched(
                Bs("bs", 0.5, O("c", "fixed", "d", "stage")),
                O("id", "fixed", "type", "delay", "delayS", 0.0, "next", "x"), O("id", "stage", "type", "delay", "delayS", d, "next", "x:b"),
                Crystal(O("role", "b", "fromPort", true, "beam", O("kind", "gaussian", "radiusM", 100e-6)), "end")), Spec(Gauss(Lam, tau, 1e-9))).Output.TotalEnergyJ()).ToArray();
            double half = signal.Max() / 2;
            int i = Array.FindIndex(signal, v => v >= half);
            double left = delay[i - 1] + (half - signal[i - 1]) / (signal[i] - signal[i - 1]) * (delay[i] - delay[i - 1]);
            Assert.Close(Math.Sqrt(2) * tau, -2 * left, 0.01, 0, "FWHM of the modelled trace");
        }

        [Test]
        public void Validate_PrintsTheGraph()
        {
            var g = SystemGraph.Build(MachZehnder(0.2, 0.2));
            Assert.True(g.ToText().Contains("link: bs1:c -> armA:in"), g.ToText());
            Assert.True(g.ToMermaid().StartsWith("flowchart"), "Mermaid");
        }
    }
}
