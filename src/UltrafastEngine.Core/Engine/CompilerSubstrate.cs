using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Optics;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    public static partial class Compiler
    {
        /// <summary>A coating from a name (the file's coatings) or an inline object.</summary>
        private static ICoating CoatingFromValue(Ctx c, JsonValue cv, string origin)
        {
            if (cv == null || cv.IsNull) return null;
            JsonObject co = null;
            if (cv.Kind == JsonKind.String)
            {
                string name = cv.AsString();
                foreach (var kv in c.Sys.Coatings) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) co = kv.Value.AsObject();
                if (co == null && (name.Equals("none", StringComparison.OrdinalIgnoreCase) || name.Equals("bare", StringComparison.OrdinalIgnoreCase) || name.Length == 0)) co = new JsonObject().Set("kind", "none");
                if (co == null) throw new JsonException(origin + ": coating \"" + name + "\" is not defined in the system's \"coatings\" section.");
                if (!co.Has("name")) co.Set("name", name);
            }
            else co = cv.AsObject();
            return CoatingFactory.Create(co, n => GetMaterial(c, n), c.Omega0, origin);
        }

        /// <summary>The mirror's "substrate" resolved to an object (name from the file's substrates or the built-in catalogue, or inline); null if none.</summary>
        private static JsonObject ResolveSubstrate(Ctx c, SystemElement e)
        {
            var r = Find(c, e, "substrate");
            JsonValue v = r != null ? r.RawValue : e.Json["substrate"];
            if (v == null || v.IsNull) return null;
            if (v.Kind == JsonKind.Object) return v.AsObject();
            if (v.Kind != JsonKind.String) throw new JsonException("mirror " + e.Id + ": substrate is a name or an object {material, thicknessM, ...}.");
            string name = v.AsString();
            foreach (var kv in c.Sys.Substrates) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value.AsObject();
            foreach (var b in SubstrateCatalog.BuiltIn)
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return new JsonObject().Set("material", b.Material).Set("thicknessM", b.ThicknessM);
            throw new JsonException("mirror " + e.Id + ": substrate \"" + name + "\" is not in the file's \"substrates\" or the catalogue (" + string.Join(", ", SubstrateCatalog.BuiltIn.Select(b => b.Name)) + ").");
        }

        /// <summary>
        /// A mirror with a substrate: the front reflection plus the echoes through the coating (MirrorSubstrateTerm). Returns the
        /// term (to be used instead of the plain InterfaceTerm) and registers the echoes as satellites for the grid planner.
        /// </summary>
        private static MirrorSubstrateTerm CompileMirrorSubstrate(Ctx c, SystemElement e, CompiledElement ce, JsonObject sub, Material before, ICoating front,
            Func<double, double> aoi, double[] basis, out Material substrate)
        {
            string matName = sub.GetString("material", "FusedSilica");
            substrate = GetMaterial(c, matName);
            double d = sub.GetDouble("thicknessM", double.NaN);
            if (!(d > 0)) throw new JsonException("mirror " + e.Id + ": the substrate needs thicknessM > 0.");
            ICoating back = null;
            if (sub.Has("backCoating")) back = CoatingFromValue(c, sub["backCoating"], "mirror " + e.Id + ".substrate.backCoating");
            else if (sub.Has("backReflectance"))
            {
                double rb = sub.GetDouble("backReflectance", 0);
                if (rb < 0 || rb > 1) throw new JsonException("mirror " + e.Id + ": substrate.backReflectance is a power reflectance, 0..1.");
                back = CoatingFactory.Create(new JsonObject().Set("kind", "ideal").Set("R", rb).Set("T", 1 - rb), n => GetMaterial(c, n), c.Omega0, "mirror " + e.Id + ".substrate.backReflectance");
            }
            int orders = Math.Max(1, sub.GetInt("orders", 1));
            double w0 = c.Omega0, lam0 = 2 * Math.PI * Cl / w0, aoi0 = aoi(w0);
            double ns = substrate.IndexAt(w0), sinT = before.IndexAt(w0) * Math.Sin(aoi0) / ns;
            double thT = Math.Asin(Math.Min(1.0, Math.Abs(sinT)));
            double ngd = substrate.GroupIndexAt(w0);
            double delay = 2 * ngd * d * Math.Cos(thT) / Cl;
            double wedge = sub.GetDouble("wedgeDeg", 0) * Math.PI / 180;
            double wBeam = sub.GetDouble("beamRadiusM", Num(c, e, "beamRadiusM", double.NaN));
            if (double.IsNaN(wBeam) && c.Track != null) wBeam = c.Track.RadiusX;
            var ov = new double[orders];
            for (int k = 1; k <= orders; k++)
            {
                double shift = 2 * k * d * Math.Tan(thT) * Math.Cos(aoi0);
                double tilt = 2 * k * ns * wedge;
                ov[k - 1] = sub.Has("overlap") ? sub.GetDouble("overlap", 1.0) : GhostTerm.Overlap(shift, tilt, wBeam, lam0);
            }
            var term = new MirrorSubstrateTerm(ce.Label, before, substrate, front, back, aoi, d, ov, basis);
            ce.SatelliteDelayS = Math.Max(ce.SatelliteDelayS, orders * delay);
            var r0 = term.Reflection(w0, 0); var r1 = term.Reflection(w0, 1);
            double pm = Math.Max(r0.S.Magnitude * r0.S.Magnitude, r0.P.Magnitude * r0.P.Magnitude);
            double lvlBase = pm > 0 ? Math.Max((r1.S - r0.S).Magnitude * (r1.S - r0.S).Magnitude, (r1.P - r0.P).Magnitude * (r1.P - r0.P).Magnitude) / pm : 0;
            // k-th echo relative intensity: (first echo) x (g^(k-1))^2 x overlap^2; g from two orders
            var r2 = new MirrorSubstrateTerm(ce.Label, before, substrate, front, back, aoi, d, new double[] { 1, 1 }, basis).Reflection(w0, 2);
            double eA = (r1.S - r0.S).Magnitude, eB = (r2.S - r1.S).Magnitude;
            double gAbs = eA > 0 ? eB / eA : 0;
            for (int k = 1; k <= orders; k++) ce.Satellites.Add((k * delay, lvlBase * Math.Pow(gAbs, 2 * (k - 1)) * ov[k - 1] * ov[k - 1]));
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "substrate echo: {0} {1:G5} mm; +{2:G5} ps per round trip (2 n_g d cos(theta_t) / c), first echo {3:G3} of the mirror's reflected intensity at the carrier x overlap {4:G3}",
                substrate.Name, d * 1e3, delay * 1e12, lvlBase, ov[0] * ov[0]));
            c.Out.Provenance.Record("element:" + e.Id, "substrate", term.Description, ModelSource.SystemData, "system file");
            return term;
        }
    
        private static DelayKernel KernelFrom(JsonObject d, string id, double defaultMaxS, int count)
        {
            string kind = d.GetString("kind", "uniform").ToLowerInvariant();
            if (kind == "uniform")
            {
                double max = d.GetDouble("maxS", defaultMaxS);
                if (!(max > 0)) throw new JsonException("element " + id + ": delayedScatter.delay.maxS is needed (no default delay for this element).");
                return DelayKernel.Uniform(d.GetDouble("minS", 0), max, count);
            }
            if (kind == "exponential")
            {
                double tau = d.GetDouble("tauS", double.NaN);
                if (!(tau > 0)) throw new JsonException("element " + id + ": delayedScatter.delay.tauS is needed for an exponential delay.");
                return DelayKernel.Exponential(tau, d.GetDouble("maxS", 8 * tau), count);
            }
            if (kind == "table") return DelayKernel.Table(d.GetDoubleArray("delaysS") ?? new double[0], d.GetDoubleArray("weights") ?? new double[0]);
            throw new JsonException("element " + id + ": delayedScatter.delay.kind is uniform, exponential or table.");
        }

        /// <summary>The analytic surface model: roughness and correlation length against the mode radius (SurfaceScatterTerm).</summary>
        private static void AddSurfaceScatter(Ctx c, SystemElement e, CompiledElement ce, JsonObject o, double defaultMaxDelayS, List<LinearTerm> into, SurfaceGeometry geo)
        {
            if (geo == null) throw new JsonException("element " + e.Id + ": delayedScatter.model \"surface\" is for mirrors, interfaces and plates (it needs a surface).");
            double sigma = o.GetDouble("roughnessRmsM", double.NaN), ell = o.GetDouble("correlationLengthM", double.NaN);
            if (!(sigma > 0) || !(ell > 0)) throw new JsonException("element " + e.Id + ": delayedScatter.model \"surface\" needs roughnessRmsM and correlationLengthM (the share inside the mode is 1 - exp(-(l/w)^2)).");
            double w = o.GetDouble("beamRadiusM", Num(c, e, "beamRadiusM", double.NaN));
            if (double.IsNaN(w) && c.Track != null) w = c.Track.RadiusX;
            if (!(w > 0)) throw new JsonException("element " + e.Id + ": delayedScatter.model \"surface\" needs the beam radius (delayedScatter.beamRadiusM, beamRadiusM, or an inputBeam to track).");
            int surfaces = Math.Max(1, o.GetInt("surfaces", geo.Surfaces));
            // without a delay the in-mode scatter of a surface returns at once (random phase); give a delay for light that goes elsewhere first
            var kernel = o.GetObject("delay") != null ? KernelFrom(o.GetObject("delay"), e.Id, defaultMaxDelayS, Math.Max(16, o.GetInt("components", 256))) : DelayKernel.Table(new[] { 0.0 }, new[] { 1.0 });
            Func<double, double> tis = om =>
            {
                double lam = 2 * Math.PI * Cl / om;
                return surfaces * ScatterTerm.Tis(sigma, lam, geo.Before.IndexAt(om), geo.Aoi(om), geo.After.IndexAt(om), geo.Mode);
            };
            var t = new SurfaceScatterTerm(tis, ell, w, kernel, o.GetInt("seed", StableSeed(e.Id, 2)));
            into.Add(t);
            if (kernel.MaxDelayS > 0) { ce.Satellites.Add((kernel.MaxDelayS, tis(c.Omega0) * t.InModeShare)); ce.SatelliteDelayS = Math.Max(ce.SatelliteDelayS, kernel.MaxDelayS); }
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}; TIS {1:G3} at the carrier ({2} surface{3}, sigma {4:G3} nm, l {5:G3} um, w {6:G3} mm): {7:G3} lost out of the mode, {8:G3} returned",
                t.Description, tis(c.Omega0), surfaces, surfaces == 1 ? "" : "s", sigma * 1e9, ell * 1e6, w * 1e3, tis(c.Omega0) * (1 - t.InModeShare), tis(c.Omega0) * t.InModeShare));
            c.Out.Provenance.Record("element:" + e.Id, "delayedScatter", t.Description, ModelSource.SystemData, "system file");
        }

        private static int StableSeed(string id, int offset)
        {
            unchecked { uint h = 2166136261; foreach (char ch in id ?? "") { h ^= ch; h *= 16777619; } return (int)(h & 0x3fffffff) + offset; }
        }

        /// <summary>The element's "ripple" (random phase / amplitude ripple of the coating): one more term after the surface.</summary>
        private static void AddCoatingRipple(Ctx c, SystemElement e, CompiledElement ce)
        {
            var rv = Find(c, e, "ripple")?.RawValue ?? e.Json["ripple"];
            if (rv == null || rv.IsNull || rv.Kind != JsonKind.Object) return;
            var o = rv.AsObject();
            double ph = o.GetDouble("phaseRmsRad", 0), am = o.GetDouble("amplitudeRms", 0), maxD = o.GetDouble("maxDelayS", 5e-12);
            if (ph < 0 || am < 0 || !(maxD > 0)) throw new JsonException("element " + e.Id + ": ripple needs phaseRmsRad >= 0, amplitudeRms >= 0 and maxDelayS > 0.");
            if (ph == 0 && am == 0) return;
            var t = new CoatingRippleTerm(ph, am, maxD, o.GetInt("seed", StableSeed(e.Id, 1)), Math.Max(8, o.GetInt("components", 64)), c.Omega0);
            ce.Pre.Add(t);
            ce.Satellites.Add((maxD, Math.Max(ph * ph, am * am)));
            ce.SatelliteDelayS = Math.Max(ce.SatelliteDelayS, maxD);
            ce.Notes.Add(t.Description + string.Format(CultureInfo.InvariantCulture, "; first-order pedestal {0:G3} of the energy within +-{1:G3} ps", ph * ph, maxD * 1e12));
            c.Out.Provenance.Record("element:" + e.Id, "ripple", t.Description, ModelSource.SystemData, "system file");
        }

        /// <summary>The element's "delayedScatter": a fraction of the power returns incoherently after a distribution of delays.</summary>
        /// <summary>Where a surface-scatter model finds its geometry: the media on both sides, the angle of incidence and how many surfaces.</summary>
        private sealed class SurfaceGeometry
        {
            public Material Before, After; public Func<double, double> Aoi; public string Mode; public int Surfaces = 1;
        }

        private static void AddDelayedScatter(Ctx c, SystemElement e, CompiledElement ce, double defaultMaxDelayS, List<LinearTerm> into, SurfaceGeometry geo = null)
        {
            var sv = Find(c, e, "delayedScatter")?.RawValue ?? e.Json["delayedScatter"];
            if (sv == null || sv.IsNull || sv.Kind != JsonKind.Object) return;
            var o = sv.AsObject();
            if (o.GetString("model", "fraction").Equals("surface", StringComparison.OrdinalIgnoreCase))
            {
                AddSurfaceScatter(c, e, ce, o, defaultMaxDelayS, into, geo);
                return;
            }
            double f = o.GetDouble("fraction", double.NaN);
            if (!(f > 0) || f >= 1) throw new JsonException("element " + e.Id + ": delayedScatter.fraction is the fraction of the power returned as delayed light, 0 < fraction < 1.");
            var d = o.GetObject("delay") ?? new JsonObject();
            string kind = d.GetString("kind", "uniform").ToLowerInvariant();
            int count = Math.Max(16, o.GetInt("components", 256));
            DelayKernel k;
            if (kind == "uniform")
            {
                double max = d.GetDouble("maxS", defaultMaxDelayS);
                if (!(max > 0)) throw new JsonException("element " + e.Id + ": delayedScatter.delay.maxS is needed (no default delay for this element).");
                k = DelayKernel.Uniform(d.GetDouble("minS", 0), max, count);
            }
            else if (kind == "exponential")
            {
                double tau = d.GetDouble("tauS", double.NaN);
                if (!(tau > 0)) throw new JsonException("element " + e.Id + ": delayedScatter.delay.tauS is needed for an exponential delay.");
                k = DelayKernel.Exponential(tau, d.GetDouble("maxS", 8 * tau), count);
            }
            else if (kind == "table") k = DelayKernel.Table(d.GetDoubleArray("delaysS") ?? new double[0], d.GetDoubleArray("weights") ?? new double[0]);
            else throw new JsonException("element " + e.Id + ": delayedScatter.delay.kind is uniform, exponential or table.");
            var t = new DelayedScatterTerm(f, k, o.GetDouble("spectralExponent", 0), c.Omega0, o.GetInt("seed", StableSeed(e.Id, 2)));
            into.Add(t);
            ce.Satellites.Add((k.MaxDelayS, f));
            ce.SatelliteDelayS = Math.Max(ce.SatelliteDelayS, k.MaxDelayS);
            ce.Notes.Add(t.Description);
            c.Out.Provenance.Record("element:" + e.Id, "delayedScatter", t.Description, ModelSource.SystemData, "system file");
        }
    }
}
