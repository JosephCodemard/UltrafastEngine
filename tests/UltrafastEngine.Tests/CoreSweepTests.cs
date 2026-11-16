// CoreSweepTests.cs -- WP-D step D3: keys that the schema declares for an element but the compiler read only through override rules.
// Each test runs the same physics twice, once with the key on the material and once on the element: the outputs must agree.

using System;
using UltrafastEngine.Results;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Core sweep: element keys that override the material's must be read from the element itself")]
    public class CoreSweepTests
    {
        private static RunResult Plate(Serialization.JsonObject mat, Serialization.JsonObject elementExtra)
        {
            var el = O("id", "plate", "type", "bulk", "material", "M", "lengthM", 5e-3, "beam", O("kind", "constant", "radiusM", 60e-6));
            if (elementExtra != null) foreach (var kv in elementExtra) el.Set(kv.Key, kv.Value);
            var j = O("format", "ultrafast-system", "version", 2, "name", "sweep");
            j.Set("materials", O("M", mat)); j.Set("elements", A(el));
            var pulse = O("shape", "gaussian", "centerWavelengthM", 1030e-9, "durationFwhmS", 100e-15, "energyJ", 2e-6);
            var extra = O("physics", O("selfSteepening", false, "raman", true), "grid", O("auto", false, "n", 2048, "windowS", 4e-12));
            return Go(SystemDescription.FromJson(j), Spec(pulse, extra));
        }

        private static double Diff(RunResult a, RunResult b)
        {
            var x = a.Output.TemporalAx(); var y = b.Output.TemporalAx(); double d = 0, n = 0;
            for (int i = 0; i < x.Length; i++) { d += (x[i] - y[i]).Magnitude * (x[i] - y[i]).Magnitude; n += x[i].Magnitude * x[i].Magnitude; }
            return Math.Sqrt(d / n);
        }

        [Test(Category = "physics", Reference = "identity: a key on the element gives the same run as the same key on the material", Tolerance = "1e-12")]
        public void BulkElement_RamanModelAndFractionOnTheElement_EqualThoseOnTheMaterial()
        {
            var onMaterial = FlatMaterial(3e-20); onMaterial.Set("raman", O("kind", "silica")); onMaterial.Set("ramanFraction", 0.18);
            var a = Plate(onMaterial, null);
            var b = Plate(FlatMaterial(3e-20), O("raman", O("kind", "silica", "fR", 0.18)));
            var none = Plate(FlatMaterial(3e-20), null);
            Assert.Greater(Diff(a, none), 1e-4, "the Raman response changes the output (test is sensitive)");
            Assert.Less(Diff(a, b), 1e-12, "raman model on the element");
        }

        [Test(Category = "physics", Reference = "identity: the element's fR equals the material's ramanFraction", Tolerance = "1e-12")]
        public void BulkElement_FRKeyOnTheElement_EqualsRamanFractionOnTheMaterial()
        {
            var a = Plate(FlatMaterial(3e-20).Set("raman", O("kind", "silica")).Set("ramanFraction", 0.3) as Serialization.JsonObject, null);
            var m = FlatMaterial(3e-20); m.Set("raman", O("kind", "silica")); m.Set("ramanFraction", 0.18);
            var b = Plate(m, O("fR", 0.3));
            var c = Plate(m, null);
            Assert.Greater(Diff(a, c), 1e-4, "fR matters");
            Assert.Less(Diff(a, b), 1e-12, "fR on the element");
        }

        [Test(Category = "physics", Reference = "identity: an element raman kind name equals the material's", Tolerance = "1e-12")]
        public void BulkElement_RamanKindNameOnTheElement_EqualsTheMaterial()
        {
            var onMaterial = FlatMaterial(3e-20); onMaterial.Set("raman", O("kind", "silica"));
            Assert.Less(Diff(Plate(onMaterial, null), Plate(FlatMaterial(3e-20), O("raman", "silica"))), 1e-12, "raman: \"silica\"");
        }
    }
}
