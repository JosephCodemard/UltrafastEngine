// CatalogueSnapshotTests.cs -- pins every number the material library hands to the engine.
//
// What it does: evaluates every built-in material, gas and Raman model (index at 24 wavelengths, n2, Raman fraction, h(t) at 80
// times, rotational-Raman lines, ionisation data, gas data) and compares the result with Data/catalogue-snapshot.txt.
// Why: WP3 moved these numbers from C# tables into catalogue files (src/UltrafastEngine.Core/Data/materials). That move had to be a pure
// refactor, so the snapshot was written from the old tables first and the new loader reproduces it to 1e-15.
// A value is changed on purpose in two steps: edit the catalogue file, then regenerate the snapshot
// (ULTRAFAST_WRITE_SNAPSHOT=1) and say in the commit which value moved and why.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;

namespace UltrafastEngine.Tests
{
    [TestArea("Material catalogue: values pinned, provenance, user catalogues")]
    public class CatalogueSnapshotTests
    {
        private static string SnapshotPath() => Path.Combine(SchemaTests.RepoRoot() ?? ".", "tests", "UltrafastEngine.Tests", "Data", "catalogue-snapshot.txt");
        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        internal static SortedDictionary<string, string> Dump()
        {
            var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var c = CultureInfo.InvariantCulture;
            var lambdaM = Enumerable.Range(0, 24).Select(i => (0.4 + 0.07 * i) * 1e-6).ToArray();
            var omega = lambdaM.Select(l => 2 * Math.PI * 2.99792458e8 / l).ToArray();
            var t = Enumerable.Range(0, 80).Select(i => i * 25e-15).ToArray();

            void Material(string key, Material m)
            {
                var n = new double[omega.Length];
                m.Index.Evaluate(omega, n);
                for (int i = 0; i < n.Length; i++) d[key + ".n[" + i + "]"] = R(n[i]);
                d[key + ".n2"] = R(m.N2); d[key + ".fR"] = R(m.RamanFraction); d[key + ".sigma"] = R(m.KerrAnisotropy);
                d[key + ".isGas"] = m.IsGas.ToString(); d[key + ".indexReference"] = m.IndexReference;
                if (m.Raman != null)
                {
                    var h = m.Raman.Evaluate(t);
                    for (int i = 0; i < h.Length; i++) d[key + ".h[" + i + "]"] = R(h[i]);
                    if (m.Raman is IVectorDelayedResponse v)
                    {
                        d[key + ".hAnisoFraction"] = R(v.AnisotropicFraction);
                        var hb = v.EvaluateAnisotropic(t);
                        for (int i = 0; i < hb.Length; i += 4) d[key + ".hb[" + i + "]"] = R(hb[i]);
                    }
                }
                if (m.Rotational is RotationalRamanResponse r)
                {
                    int k = 0;
                    foreach (var (om, w, g) in r.Lines) { d[key + ".rot[" + k + "]"] = R(om) + " " + R(w) + " " + R(g); k++; }
                }
            }

            foreach (var name in MaterialLibrary.BuiltInNames)
            {
                Material("mat:" + name, MaterialLibrary.BuiltIn(name));
                var ion = SolidIonizationData.For(name);
                if (ion != null) d["ion:" + name] = string.Join(" ", new[] { ion.BandGapEv, ion.EffectiveMass, ion.NeutralDensityM3, ion.CollisionTimeS, ion.RecombinationTimeS }.Select(R));
            }
            foreach (var sp in new[] { "He", "Ne", "Ar", "Kr", "Xe", "N2", "O2", "CO2", "H2", "Air" })
            {
                Material("gas:" + sp + "@2.5bar310K", MaterialLibrary.Gas(sp, 2.5, 310));
                Material("gas:" + sp + "@1bar293K", MaterialLibrary.Gas(sp, 1.0, 293.15));
            }
            foreach (var g in GasData.Table.Values)
                d["gasdata:" + g.Species] = string.Join(" ", new[] { g.IonizationEv, g.L, g.ZEff, g.N2Electronic, g.RotB, g.RotD, g.AnisotropyA3, g.GEven, g.GOdd, g.T2At1Bar }.Select(R));
            foreach (var kv in GasN2Table()) d["gasn2:" + kv.Key] = R(kv.Value);
            foreach (var kv in GasIndex.Coefficients) d["gasindex:" + kv.Key] = string.Join(" ", kv.Value.Select(R));

            var raman = new Dictionary<string, IDelayedResponse>
            {
                ["silica"] = new DampedOscillatorResponse(12.2e-15, 32e-15),
                ["linAgrawal"] = new LinAgrawalRamanResponse(),
                ["hollenbeck"] = new HollenbeckCantrellRamanResponse(),
                ["orientational"] = new OrientationalResponse(0.2e-12, 1.6e-12)
            };
            foreach (var kv in raman)
            {
                var h = kv.Value.Evaluate(t);
                for (int i = 0; i < h.Length; i++) d["raman:" + kv.Key + ".h[" + i + "]"] = R(h[i]);
            }
            foreach (var kind in new[] { "silica", "blowWood", "hollenbeck", "linAgrawal", "other" }) d["ramanfraction:" + kind] = R(MaterialLibrary.DefaultRamanFraction(kind));
            d["const:BaF2N2PerW"] = R(MaterialLibrary.BaF2N2PerW); d["const:BaF2Sigma"] = R(MaterialLibrary.BaF2Sigma);
            d["const:CS2"] = string.Join(" ", new[] { MaterialLibrary.CS2N2TotalPerW, MaterialLibrary.CS2OrientationalFraction, MaterialLibrary.CS2RiseS, MaterialLibrary.CS2DecayS }.Select(R));
            return d;
        }

        private static IEnumerable<KeyValuePair<string, double>> GasN2Table() => MaterialLibrary.GasN2PerBar.OrderBy(k => k.Key, StringComparer.Ordinal);

        [Test(Category = "materials", Reference = "the values of the catalogue files equal the old C# tables", Tolerance = "1e-15 relative")]
        public void Catalogue_ReproducesTheValuesPinnedBeforeTheMove()
        {
            var path = SnapshotPath();
            var now = Dump();
            if (Environment.GetEnvironmentVariable("ULTRAFAST_WRITE_SNAPSHOT") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, now.Select(kv => kv.Key + "\t" + kv.Value));
                Console.WriteLine("wrote " + path + " (" + now.Count + " entries)");
                return;
            }
            if (!File.Exists(path)) { Assert.Skip("snapshot not found: " + path); }
            var old = File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => l.Split(new[] { '\t' }, 2)).ToDictionary(p => p[0], p => p[1]);
            Assert.Equal(old.Count, now.Count, "number of pinned entries");
            int bad = 0; string first = null;
            foreach (var kv in old)
            {
                if (!now.TryGetValue(kv.Key, out var v)) { bad++; first = first ?? "missing " + kv.Key; continue; }
                if (v == kv.Value) continue;
                var a = kv.Value.Split(' '); var b = v.Split(' ');
                bool same = a.Length == b.Length;
                for (int i = 0; same && i < a.Length; i++)
                {
                    if (double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                        same = Math.Abs(x - y) <= 1e-15 * Math.Max(Math.Abs(x), Math.Abs(y)) + 1e-300;
                    else same = a[i] == b[i];
                }
                if (!same) { bad++; first = first ?? kv.Key + ": pinned " + kv.Value + ", now " + v; }
            }
            Assert.True(bad == 0, bad + " pinned values differ; first: " + first);
        }
    }
}
