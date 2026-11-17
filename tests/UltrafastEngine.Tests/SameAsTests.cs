// SameAsTests.cs -- the radial spatial model through many "sameAs" passes: a lens waveguide with a Kerr plate per pass (the transverse
// stand-in for a multipass cell; a user ran 46 sameAs passes and the 1-D model has no Kerr lens or transverse averaging).
// The sameAs copies must give exactly the run with explicit, separately written passes, and the loss budget must close.
using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests
{
    [TestArea("Identical elements (sameAs) with transverse models")]
    public class SameAsTests
    {
        private const double Lam = 1030e-9;
        private const int Passes = 46;

        private static JsonObject Radial() => O("model", "radial", "points", 32, "radiusM", 3e-3);

        // one pass: 1 mm Kerr plate (flat index, so the transverse result is pure diffraction + Kerr lens), 0.5 m of free space, f = 0.25 m lens
        // (a lens guide with d = 2f, stable)
        private static IEnumerable<JsonObject> Pass(int k, bool copy)
        {
            if (copy && k > 1)
            {
                yield return O("id", "plate" + k, "sameAs", "plate1");
                yield return O("id", "gap" + k, "sameAs", "gap1");
                yield return O("id", "lens" + k, "sameAs", "lens1");
                yield break;
            }
            yield return O("id", "plate" + k, "type", "bulk", "material", "flat", "lengthM", 1e-3, "transverse", Radial());
            yield return O("id", "gap" + k, "type", "gap", "lengthM", 0.5, "transverse", Radial());
            yield return O("id", "lens" + k, "type", "lens", "focalLengthM", 0.25);
        }

        private static SystemDescription Cell(bool copy)
        {
            var els = new List<JsonObject> { O("id", "inject", "type", "gap", "lengthM", 1e-3, "beam", O("kind", "gaussian", "w0M", 0.6e-3), "transverse", Radial()) };
            for (int k = 1; k <= Passes; k++) els.AddRange(Pass(k, copy));
            var s = O("format", "ultrafast-system", "name", "lens guide", "materials", O("flat", FlatMaterial(2.5e-19)));
            s.Set("elements", new JsonArray(els.Select(e => (JsonValue)e)));
            return SystemDescription.FromJson(s);
        }

        [Test(Reference = "sameAs copies equal explicitly written passes (same compiled physics); loss budget: input - output = sum of booked losses", Tolerance = "1e-12 (field), 1e-9 (energy closure)")]
        public void Radial_ThroughFortySixSameAsPasses_EqualsExplicitPasses_AndTheEnergyCloses()
        {
            var run = Spec(O("shape", "gaussian", "centerWavelengthM", Lam, "durationFwhmS", 300e-15, "energyJ", 2e-6),
                           O("grid", O("n", 256), "physics", O("selfSteepening", false, "raman", false), "outputs", O("keepProfiles", "used")));
            var copies = Go(Cell(true), run);
            var explicitRun = Go(Cell(false), run);
            Assert.Equal(1 + 3 * Passes, copies.Records.Count, "records: one per instance");
            double num = 0, den = 0;
            for (int i = 0; i < copies.Output.Grid.N; i++)
            {
                num += (copies.Output.Ax[i] - explicitRun.Output.Ax[i]).Magnitude * (copies.Output.Ax[i] - explicitRun.Output.Ax[i]).Magnitude;
                den += explicitRun.Output.Ax[i].Magnitude * explicitRun.Output.Ax[i].Magnitude;
            }
            Assert.Less(Math.Sqrt(num / den), 1e-12, "output field: sameAs copies vs explicit passes");
            Assert.Close(explicitRun.Records.Sum(r => r.BIntegralRad), copies.Records.Sum(r => r.BIntegralRad), 1e-12, 0, "total B");
            // every plate is nonlinear and spatial (a B per pass), the profile is carried through all passes
            Assert.True(copies.Records.Where(r => r.Id.StartsWith("plate")).All(r => r.BIntegralRad > 0), "each pass sees the Kerr plate");
            Assert.True(copies.Output.Profile != null, "the transverse profile is carried to the end");
            double eIn = copies.Input.TotalEnergyJ(), eOut = copies.Output.TotalEnergyJ(), booked = copies.Records.Sum(r => r.LossJ.Values.Sum());
            Assert.Close(eIn, eOut + booked, 1e-9, 0, "loss budget closes over 139 elements");
        }
    }
}
