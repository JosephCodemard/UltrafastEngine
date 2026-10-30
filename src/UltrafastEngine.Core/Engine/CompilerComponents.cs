using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Materials;
using UltrafastEngine.Models;
using UltrafastEngine.Numerics;
using UltrafastEngine.Optics;
using UltrafastEngine.Physics;
using UltrafastEngine.Serialization;
using UltrafastEngine.Systems;

namespace UltrafastEngine.Engine
{
    /// <summary>
    /// Compilation of the optical components of Stage 1.2 (docs/reference/components.md): plates and windows with ghost
    /// replicas, neutral-density filters, thin-film polarisers, Faraday rotators, thick waveplates, aberrated surfaces
    /// and the GDD ripple of chirped mirrors. Each is built from the same parts as the rest of the engine (interface
    /// terms, a bulk element that can be nonlinear and spatial, Jones terms), so overrides, provenance, the loss
    /// budget and the transverse models all apply.
    /// </summary>
    public static partial class Compiler
    {
        private const double Cl = PhysicalConstants.SpeedOfLightMPerS;

        /// <summary>Keys a plate passes on to its internal bulk element.</summary>
        private static readonly string[] BulkPassKeys = { "beam", "transverse", "nonlinear", "n2", "timeResponse", "replaceKerr", "raman", "fR", "selfSteepening",
            "indexProfile", "absorptionProfile", "n2Scale", "wavelengthsM", "dispersionSource" };

        /// <summary>A bulk element standing for the inside of a component (same id, so element overrides apply).</summary>
        private static SystemElement InnerBulk(SystemElement e, string material, double lengthM, JsonValue transmission = null)
        {
            var j = new JsonObject().Set("id", e.Id).Set("type", "bulk").Set("material", material).Set("lengthM", lengthM);
            foreach (var k in BulkPassKeys) if (e.Json.Has(k)) j.Set(k, e.Json[k]);
            if (transmission != null) j.Set("transmission", transmission);
            // plates are linear unless they have a beam (or a tracked one) -- the bulk compiler decides
            return new SystemElement { Id = e.Id, Type = "bulk", Label = e.Label, Json = j, Surfaces = e.Surfaces };
        }

        /// <summary>The inside of a component is nonlinear only when the user asks or when a beam is available
        /// (its own, the tracked beam or a transverse model); otherwise it is linear without a warning.</summary>
        private static SystemElement Inner(Ctx c, SystemElement e, string material, double lengthM, JsonValue transmission = null)
        {
            var se = InnerBulk(e, material, lengthM, transmission);
            bool beam = e.Json.Has("beam") || c.Track != null || e.Json.Has("transverse") || (c.Run.Physics.Transverse != null && !c.Run.Physics.Transverse.IsNull);
            if (!e.Json.Has("nonlinear") && !beam) se.Json.Set("nonlinear", false);
            return se;
        }

        /// <summary>A coating from a key (name defined in the file's coatings, or an inline object), or null.</summary>
        private static ICoating CoatingFrom(Ctx c, SystemElement e, string key)
        {
            var cr = Find(c, e, key);
            JsonValue cv = cr != null ? cr.RawValue : e.Json[key];
            if (cv == null || cv.IsNull) return null;
            JsonObject co;
            if (cv.Kind == JsonKind.String)
            {
                string name = cv.AsString();
                co = null;
                foreach (var kv in c.Sys.Coatings) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) co = kv.Value.AsObject();
                if (co == null && (name.Equals("none", StringComparison.OrdinalIgnoreCase) || name.Equals("bare", StringComparison.OrdinalIgnoreCase) || name.Length == 0)) co = new JsonObject().Set("kind", "none");
                if (co == null) throw new JsonException("coating \"" + name + "\" is not defined in the system's \"coatings\" section.");
                if (!co.Has("name")) co.Set("name", name);
            }
            else co = cv.AsObject();
            var coating = CoatingFactory.Create(co, n => GetMaterial(c, n), c.Omega0, "element " + e.Id + "." + key);
            c.Out.Provenance.Record("element:" + e.Id, key, coating.Description, cr?.Source ?? ModelSource.SystemData, cr?.Origin ?? "system file");
            return coating;
        }

        private static double AoiOf(Ctx c, SystemElement e, double fallbackRad = 0)
        {
            if (Find(c, e, "aoiDeg") != null || e.Json.Has("aoiDeg")) return Num(c, e, "aoiDeg", 0) * Math.PI / 180;
            return Num(c, e, "aoiRad", fallbackRad);
        }

        /// <summary>Internal angle in a plate of material m entered from medium m0 at angle theta (per omega).</summary>
        private static Func<double, double> InternalAngle(Material m0, Material m, double theta)
            => w => { double s = m0.IndexAt(w) * Math.Sin(theta) / m.IndexAt(w); return Math.Abs(s) < 1 ? Math.Asin(s) : double.NaN; };

        // ================================================================================== plate / window / ND filter

        /// <summary>
        /// A plane-parallel plate (window, substrate, ND filter): front surface, bulk (dispersion, absorption, optional
        /// nonlinearity and spatial model over the internal path d / cos θ_t), back surface, optional ghost replicas.
        /// </summary>
        private static void CompilePlate(Ctx c, SystemElement e, CompiledElement ce, string type)
        {
            bool nd = type == "ndfilter";
            string ndKind = nd ? Str(c, e, "kind", "absorptive").ToLowerInvariant() : null;
            if (nd && ndKind != "absorptive" && ndKind != "reflective") throw new JsonException("ndFilter kind must be \"absorptive\" or \"reflective\".");
            string matName = Find(c, e, "material")?.RawValue?.AsString() ?? e.Json.GetString("material", nd ? (ndKind == "reflective" ? "FusedSilica" : "N-BK7") : null);
            if (string.IsNullOrEmpty(matName)) throw new JsonException(type + " needs a \"material\".");
            var mat = GetMaterial(c, matName);
            var medium = GetMaterial(c, Str(c, e, "medium", "Air"));
            double d = Num(c, e, "thicknessM", nd ? (ndKind == "reflective" ? 1e-3 : 2e-3) : double.NaN);
            if (double.IsNaN(d) || d < 0) throw new JsonException(type + " needs a non-negative \"thicknessM\".");
            if (nd && !e.Json.Has("material")) ce.Notes.Add("ndFilter substrate: " + matName + " (default" + (ndKind == "absorptive" ? "; a stand-in for the dispersion of absorbing ND glass -- give \"material\" for yours" : "") + ")");
            double aoi = AoiOf(c, e);
            var thetaT = InternalAngle(medium, mat, aoi);
            double tt0 = thetaT(c.Omega0);
            double L = d / Math.Cos(tt0);
            var basis = BasisTracker.FromPsi(Angle(c, e, "planeOfIncidence", Math.PI / 2));
            var front = CoatingFrom(c, e, "coatingFront") ?? CoatingFrom(c, e, "coating");
            var back = CoatingFrom(c, e, "coatingBack") ?? CoatingFrom(c, e, "coating");
            ce.Surface = new SurfaceInfo { AoiRad = aoi, Coating = front?.Description ?? "bare" };
            AddClip(c, e, ce);
            ce.Pre.Add(new InterfaceTerm(ce.Label + " front", medium, mat, front, "refract", _ => aoi, basis));

            // OD: absorptive = distributed in the bulk; reflective = at the front coating (reflected light leaves the beam)
            JsonValue bulkT = null; double odAt0 = 0; double[] denseWl = null;
            Func<double, double> odFn = null;
            if (nd)
            {
                var od = c.Sys.PerWavelength(e, "od");
                var ovr = Find(c, e, "od");
                if (ovr != null) od = (new[] { 1.0 }, new[] { ovr.RawValue.AsDouble() });
                if (od == null) throw new JsonException("ndFilter needs \"od\" (optical density: a number, or one value per wavelength with \"wavelengthsM\").");
                var (ow, ov) = od.Value;
                if (ow.Length == 1) { double v = ov[0]; odFn = _ => v; }
                else
                {
                    var o = Enumerable.Range(0, ow.Length).OrderBy(i => ow[i]).ToArray();
                    var wsrt = o.Select(i => ow[i]).ToArray(); var vsrt = o.Select(i => ov[i]).ToArray();
                    var li = new LinearInterpolator(wsrt, vsrt);
                    odFn = w => w <= wsrt[0] ? vsrt[0] : w >= wsrt[wsrt.Length - 1] ? vsrt[vsrt.Length - 1] : li.Evaluate(w);
                }
                odAt0 = odFn(c.Omega0);
                if (ndKind == "absorptive")
                {
                    if (ow.Length == 1) bulkT = Math.Pow(10, -ov[0]);
                    else
                    {
                        // dense sampling so the OD (absorbance), not the transmission, is interpolated
                        var dense = Enumerable.Range(0, 256).Select(k => c.Omega0 * (0.5 + k / 255.0)).ToArray();
                        bulkT = JsonValue.FromArray(dense.Select(w => Math.Pow(10, -odFn(w))).ToArray());
                        denseWl = dense.Select(w => 2 * Math.PI * Cl / w).ToArray();
                    }
                }
                else
                {
                    var wsamp = Enumerable.Range(0, 64).Select(k => c.Omega0 * (0.6 + 0.8 * k / 63.0)).ToArray();
                    var odf = odFn;
                    ce.Pre.Add(ow.Length == 1 ? (LinearTerm)new FlatLossTerm(Math.Pow(10, -ov[0]), "ndFilter (reflected)", "reflective ND coating OD " + ov[0].ToString("G4", CultureInfo.InvariantCulture))
                                              : new SampledLossTerm(wsamp, wsamp.Select(w => Math.Pow(10, -odf(w))).ToArray(), "ndFilter (reflected)", "reflective ND coating (OD per wavelength)"));
                }
                c.Out.Provenance.Record("element:" + e.Id, "od", odAt0.ToString("G4", CultureInfo.InvariantCulture) + " at the carrier (" + ndKind + ")", ovr?.Source ?? ModelSource.SystemData, ovr?.Origin ?? "system file");
            }

            // the inside: a bulk element over the internal path (nonlinear / spatial when it has a beam)
            int preCount = ce.Pre.Count;
            var inner = Inner(c, e, matName, L, bulkT);
            if (denseWl != null) inner.Json.Set("wavelengthsM", JsonValue.FromArray(denseWl));
            CompileBulk(c, inner, ce, false);
            ce.LengthM = L;
            if (nd && ndKind == "absorptive" && ce.Nonlinear == null && ce.Active == null)
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "absorptive ND: OD {0:G4} distributed over the {1:G4} mm path", odAt0, L * 1e3));
            ce.Post.Add(new InterfaceTerm(ce.Label + " back", mat, medium, back, "refract", thetaT, basis));
            ce.TrackingIndex = mat.IndexAt(c.Omega0);

            AddDelayedScatter(c, e, ce, 2 * mat.GroupIndexAt(c.Omega0) * d * Math.Cos(tt0) / Cl, ce.Post,
                new SurfaceGeometry { Before = medium, After = mat, Aoi = _ => aoi, Mode = "refract", Surfaces = 2 });

            // ghosts
            var gj = e.Json["ghosts"];
            bool ghosts = gj != null && (gj.Kind == JsonKind.Object || (gj.Kind == JsonKind.Bool && gj.AsBool()));
            var gr = Find(c, e, "ghosts"); if (gr != null) ghosts = gr.RawValue.Kind == JsonKind.Object || gr.RawValue.AsBool();
            if (ghosts)
            {
                var go = gj?.Kind == JsonKind.Object ? gj.AsObject() : new JsonObject();
                int orders = Math.Max(1, go.GetInt("orders", 1));
                var frontIn = front ?? new StackCoating(null, "bare (Fresnel)");
                var backIn = back ?? new StackCoating(null, "bare (Fresnel)");
                double rFrontOverride = nd && ndKind == "reflective" ? go.GetDouble("frontReflectance", 0.5) : go.GetDouble("frontReflectance", double.NaN);
                double rBackOverride = go.GetDouble("backReflectance", double.NaN);
                // extra absorption of each round trip (material attenuation and the absorptive OD)
                var att = mat.Attenuation;
                var odf = nd && ndKind == "absorptive" ? odFn : null;
                Func<double, (Complex, Complex)> rt = w =>
                {
                    double th = thetaT(w);
                    if (double.IsNaN(th)) return (Complex.Zero, Complex.Zero);
                    var n0 = ThinFilm.ComplexIndex(mat, w); var ns = ThinFilm.ComplexIndex(medium, w);
                    var cb = backIn.Evaluate(w, th, n0, ns); var cf = frontIn.Evaluate(w, th, n0, ns);
                    Complex rbs = double.IsNaN(rBackOverride) ? cb.Rs : Math.Sqrt(rBackOverride), rbp = double.IsNaN(rBackOverride) ? cb.Rp : Math.Sqrt(rBackOverride);
                    Complex rfs = double.IsNaN(rFrontOverride) ? cf.Rs : Math.Sqrt(rFrontOverride), rfp = double.IsNaN(rFrontOverride) ? cf.Rp : Math.Sqrt(rFrontOverride);
                    double psi = 2 * mat.IndexAt(w) * w * d * Math.Cos(th) / Cl;
                    double a = 1;
                    if (att != null) { var al = new double[1]; att.Evaluate(new[] { w }, al); a *= Math.Exp(-al[0] * d / Math.Cos(th)); }   // amplitude over 2 passes = exp(-alpha L)
                    if (odf != null) a *= Math.Pow(10, -odf(w));
                    var ph = Complex.FromPolarCoordinates(a, psi);
                    return (rbs * rfs * ph, rbp * rfp * ph);
                };
                // delay and overlap
                double ngd = mat.GroupIndexAt(c.Omega0);
                double delay = 2 * ngd * d * Math.Cos(tt0) / Cl;
                double lam0 = 2 * Math.PI * Cl / c.Omega0;
                double wedge = Num(c, e, "wedgeDeg", 0) * Math.PI / 180;
                double wBeam = go.GetDouble("beamRadiusM", Num(c, e, "beamRadiusM", double.NaN));
                bool tx = BeamState.TangentialIsX(Angle(c, e, "planeOfIncidence", Math.PI / 2), out _);
                if (double.IsNaN(wBeam) && c.Track != null) wBeam = tx ? c.Track.RadiusX : c.Track.RadiusY;
                var ov = new double[orders];
                for (int k = 1; k <= orders; k++)
                {
                    double shift = 2 * k * d * Math.Tan(tt0) * Math.Cos(aoi);
                    double tilt = 2 * k * mat.IndexAt(c.Omega0) * wedge;
                    ov[k - 1] = go.Has("overlap") ? go.GetDouble("overlap", 1.0) : GhostTerm.Overlap(shift, tilt, wBeam, lam0);
                    if (!go.Has("overlap") && double.IsNaN(wBeam) && (shift > 0 || tilt > 0) && k == 1)
                        ce.Notes.Add("ghosts: the replicas are displaced/tilted but no beam radius is known -- full overlap assumed (give ghosts.beamRadiusM, beamRadiusM or an inputBeam)");
                }
                var g = new GhostTerm(rt, ov, basis, delay);
                ce.Post.Add(g);
                ce.SatelliteDelayS = Math.Max(ce.SatelliteDelayS, orders * delay);
                var (g0s, g0p) = rt(c.Omega0);
                double lv = Math.Max(g0s.Magnitude * g0s.Magnitude, g0p.Magnitude * g0p.Magnitude);
                for (int k = 1; k <= orders; k++) ce.Satellites.Add((k * delay, Math.Pow(lv, k) * ov[k - 1] * ov[k - 1]));
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "ghost: +{0:G4} ps per round trip, relative intensity {1:G3} (s) / {2:G3} (p) x overlap {3:G3}",
                    delay * 1e12, g0s.Magnitude * g0s.Magnitude, g0p.Magnitude * g0p.Magnitude, ov[0] * ov[0]));
                c.Out.Provenance.Record("element:" + e.Id, "ghosts", g.Description, gr?.Source ?? ModelSource.SystemData, gr?.Origin ?? "system file");
            }
        }

        /// <summary>
        /// Ghost replicas of the two end faces of a bulk element, crystal, gain medium or thick lens (normal incidence):
        /// the double reflection (back face, then front face) follows the main pulse by 2 n_g L / c with relative field
        /// r_back r_front exp(i 2 n(w) w L / c). Reflectances default to Fresnel against air; give the coatings' values
        /// as ghosts.frontReflectance / backReflectance (an AR coating of 0.2 % gives 4e-6 = -54 dB per round trip).
        /// </summary>
        private static void AddElementGhosts(Ctx c, SystemElement e, CompiledElement ce, string type)
        {
            var gj = e.Json["ghosts"];
            bool on = gj.Kind == JsonKind.Object || (gj.Kind == JsonKind.Bool && gj.AsBool());
            var gr = Find(c, e, "ghosts"); if (gr != null) on = gr.RawValue.Kind == JsonKind.Object || gr.RawValue.AsBool();
            if (!on) return;
            var go = gj.Kind == JsonKind.Object ? gj.AsObject() : new JsonObject();
            int orders = Math.Max(1, go.GetInt("orders", 1));
            // the index of the inside
            Func<double, double> n;
            string what;
            if (go.Has("material")) { var m = GetMaterial(c, go.GetString("material")); n = m.IndexAt; what = m.Name; }
            else if (type == "bulk" || type == "lens")
            {
                string mn = Str(c, e, "material", null) ?? throw new JsonException("ghosts on a " + type + " need its material (or ghosts.material)");
                var m = GetMaterial(c, mn); n = m.IndexAt; what = m.Name;
            }
            else if (type == "crystal")
            {
                var cv = e.Json["crystal"];
                if (cv == null || cv.Kind != JsonKind.String) throw new JsonException("ghosts on a crystal defined inline need ghosts.material");
                var cm = Crystals.CrystalLibrary.Get(cv.AsString(), e.Json.GetDouble("temperatureC", double.NaN));
                n = w => { var o = new double[1]; cm.Nx.Evaluate(new[] { w }, o); return o[0]; }; what = cm.Name + " (x / ordinary index)";
            }
            else
            {
                string host = e.Json.GetString("host");
                if (host == null && e.Json.Has("medium")) { var gm = Gain.GainMedium.FromJson(e.Json["medium"], c.Reg, "element " + e.Id + ".medium"); host = gm.Host; }
                var m = GetMaterial(c, host ?? throw new JsonException("ghosts on a gain element need its host (or ghosts.material)")); n = m.IndexAt; what = m.Name;
            }
            double L = type == "lens" ? Num(c, e, "thicknessM", 0) : ce.LengthM > 0 ? ce.LengthM : Num(c, e, "lengthM", 0);
            if (!(L > 0)) throw new JsonException("ghosts on a " + type + " need its length (thicknessM for a lens)");
            double n0 = n(c.Omega0);
            double Rf(double nn) => Math.Pow((nn - 1) / (nn + 1), 2);
            double rFront = go.GetDouble("frontReflectance", double.NaN), rBack = go.GetDouble("backReflectance", double.NaN);
            Func<double, (Complex, Complex)> rt = w =>
            {
                double nw = n(w);
                if (double.IsNaN(nw)) return (Complex.Zero, Complex.Zero);
                double rf = Math.Sqrt(double.IsNaN(rFront) ? Rf(nw) : rFront), rb = Math.Sqrt(double.IsNaN(rBack) ? Rf(nw) : rBack);
                var g = Complex.FromPolarCoordinates(rf * rb, 2 * nw * w * L / Cl);
                return (g, g);
            };
            double h = c.Omega0 * 1e-4;
            double ng = n0 + c.Omega0 * (n(c.Omega0 + h) - n(c.Omega0 - h)) / (2 * h);
            double delay = 2 * ng * L / Cl;
            double wedge = go.GetDouble("wedgeDeg", 0) * Math.PI / 180, lam0 = 2 * Math.PI * Cl / c.Omega0;
            double wBeam = go.GetDouble("beamRadiusM", c.Track != null ? Math.Max(c.Track.RadiusX, c.Track.RadiusY) : double.NaN);
            var ov = new double[orders];
            for (int k = 1; k <= orders; k++) ov[k - 1] = go.Has("overlap") ? go.GetDouble("overlap", 1) : GhostTerm.Overlap(0, 2 * k * n0 * wedge, wBeam, lam0);
            var gt = new GhostTerm(rt, ov, new double[] { 1, 0, 0, 1 }, delay);
            ce.Post.Add(gt);
            ce.SatelliteDelayS = Math.Max(ce.SatelliteDelayS, orders * delay);
            var (g0, _) = rt(c.Omega0);
            for (int k = 1; k <= orders; k++) ce.Satellites.Add((k * delay, Math.Pow(g0.Magnitude * g0.Magnitude, k) * ov[k - 1] * ov[k - 1]));
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "ghost of the end faces ({0}): +{1:G4} ps per round trip, relative intensity {2:G3} x overlap {3:G3}",
                what, delay * 1e12, g0.Magnitude * g0.Magnitude, ov[0] * ov[0]));
            c.Out.Provenance.Record("element:" + e.Id, "ghosts", gt.Description, gr?.Source ?? ModelSource.SystemData, gr?.Origin ?? "system file");
        }

        // ================================================================================== thin-film polariser

        /// <summary>
        /// Thin-film polariser at its design angle: a physical quarter-wave stack (angle-corrected thicknesses) evaluated
        /// with the transfer-matrix method at the actual angle and wavelength -- s is reflected, p transmitted -- on a
        /// substrate of finite thickness; or an "ideal" model with given Tp / Ts. "output": "transmitted" (p port,
        /// through the substrate) or "reflected" (s port).
        /// </summary>
        private static void CompileTfp(Ctx c, SystemElement e, CompiledElement ce)
        {
            string model = Str(c, e, "model", "stack").ToLowerInvariant();
            string output = Str(c, e, "output", "transmitted").ToLowerInvariant();
            if (output != "transmitted" && output != "reflected") throw new JsonException("thinFilmPolarizer output must be \"transmitted\" or \"reflected\".");
            double aoi = e.Json.Has("aoiDeg") || e.Json.Has("aoiRad") || Find(c, e, "aoiDeg") != null ? AoiOf(c, e) : 56.0 * Math.PI / 180;
            double axis = Angle(c, e, "axis", 0);          // p (transmission) axis in the lab
            var basis = BasisTracker.FromPsi(axis + Math.PI / 2); // s along axis + 90 deg -> p along axis
            var medium = GetMaterial(c, Str(c, e, "medium", "Air"));
            string subName = Str(c, e, "substrate", "FusedSilica");
            var sub = GetMaterial(c, subName);
            ce.Surface = new SurfaceInfo { AoiRad = aoi };
            AddClip(c, e, ce);
            if (model == "ideal")
            {
                double tp = Num(c, e, "transmissionP", 0.98), ts = Num(c, e, "transmissionS", 1e-3);
                double rs = Num(c, e, "reflectanceS", 0.998), rp = Num(c, e, "reflectanceP", Math.Max(0, 1 - tp));
                if (output == "transmitted") ce.Pre.Add(new PolarizerTerm(axis, tp / Math.Max(ts, 1e-300), tp));
                else ce.Pre.Add(new PolarizerTerm(axis + Math.PI / 2, rs / Math.Max(rp, 1e-300), rs));
                ce.Surface.Coating = string.Format(CultureInfo.InvariantCulture, "ideal TFP Tp {0:G4} Ts {1:G3}", tp, ts);
            }
            else if (model == "stack")
            {
                double lamD = Num(c, e, "designWavelengthM", 2 * Math.PI * Cl / c.Omega0);
                int pairs = (int)Num(c, e, "pairs", 12);
                var H = GetMaterial(c, Str(c, e, "high", "HfO2")); var Lo = GetMaterial(c, Str(c, e, "low", "SiO2_film"));
                double wd = 2 * Math.PI * Cl / lamD, n0 = medium.IndexAt(wd) * Math.Sin(aoi);
                // a quarter-wave stack reflects s over a wide band and p over a narrower one (tilted admittances
                // eta_s = n cos(t), eta_p = n / cos(t)); the operating wavelength is put between the two band edges
                // (short-wavelength side), where s is reflected and p transmitted
                double Cos(Material m) => Math.Sqrt(1 - Math.Pow(n0 / m.IndexAt(wd), 2));
                double hS = H.IndexAt(wd) * Cos(H), lS = Lo.IndexAt(wd) * Cos(Lo), hP = H.IndexAt(wd) / Cos(H), lP = Lo.IndexAt(wd) / Cos(Lo);
                double dgS = 2 / Math.PI * Math.Asin(Math.Abs(hS - lS) / (hS + lS)), dgP = 2 / Math.PI * Math.Asin(Math.Abs(hP - lP) / (hP + lP));
                double g0 = Num(c, e, "bandPosition", 0.5);
                double lamC = lamD * (1 + dgP + g0 * (dgS - dgP));      // stack centre wavelength
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "TFP stack centred at {0:G5} nm so that {1:G5} nm lies between the p band edge and the s band edge (relative half-widths {2:G3} / {3:G3})", lamC * 1e9, lamD * 1e9, dgP, dgS));
                double QW(Material m) { double n = m.IndexAt(2 * Math.PI * Cl / lamC); double cs = Math.Sqrt(1 - Math.Pow(n0 / n, 2)); return lamC / (4 * n * cs); }
                var layers = new List<FilmLayer>();
                for (int k = 0; k < pairs; k++) { layers.Add(new FilmLayer { Material = H, ThicknessM = QW(H) }); layers.Add(new FilmLayer { Material = Lo, ThicknessM = QW(Lo) }); }
                layers.Add(new FilmLayer { Material = H, ThicknessM = QW(H) });
                var stack = new StackCoating(layers, string.Format(CultureInfo.InvariantCulture, "TFP stack (HL)^{0}H at {1:G4} deg, {2:G4} nm", pairs, aoi * 180 / Math.PI, lamD * 1e9));
                ce.Surface.Coating = stack.Description;
                if (output == "reflected")
                    ce.Pre.Add(new InterfaceTerm(ce.Label, medium, sub, stack, "reflect", _ => aoi, basis));
                else
                {
                    ce.Pre.Add(new InterfaceTerm(ce.Label + " coating", medium, sub, stack, "refract", _ => aoi, basis));
                    double d = Num(c, e, "thicknessM", 3e-3);
                    var thetaT = InternalAngle(medium, sub, aoi);
                    double L = d / Math.Cos(thetaT(c.Omega0));
                    CompileBulk(c, Inner(c, e, subName, L), ce, false);
                    ce.LengthM = L;
                    ce.Post.Add(new InterfaceTerm(ce.Label + " back", sub, medium, CoatingFrom(c, e, "coatingBack"), "refract", thetaT, basis));
                    ce.TrackingIndex = sub.IndexAt(c.Omega0);
                }
                // design check at the carrier
                var cc = stack.Evaluate(c.Omega0, aoi, ThinFilm.ComplexIndex(medium, c.Omega0), ThinFilm.ComplexIndex(sub, c.Omega0));
                ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "TFP coating at the carrier: Tp = {0:G4}, Ts = {1:G3}, Rs = {2:G5} (extinction Tp/Ts = {3:G3})",
                    cc.Tp.Magnitude * cc.Tp.Magnitude, cc.Ts.Magnitude * cc.Ts.Magnitude, cc.Rs.Magnitude * cc.Rs.Magnitude, cc.Tp.Magnitude * cc.Tp.Magnitude / Math.Max(1e-300, cc.Ts.Magnitude * cc.Ts.Magnitude)));
            }
            else throw new JsonException("thinFilmPolarizer model must be \"stack\" or \"ideal\".");
            c.Out.Provenance.Record("element:" + e.Id, "polarizer", ce.Surface.Coating + ", " + output + " port, p axis " + (axis * 180 / Math.PI).ToString("G4", CultureInfo.InvariantCulture) + " deg", ModelSource.SystemData, "system file");
        }

        // ================================================================================== Faraday rotator

        private static void CompileFaraday(Ctx c, SystemElement e, CompiledElement ce)
        {
            double lamD = Num(c, e, "designWavelengthM", 2 * Math.PI * Cl / c.Omega0);
            double L = Num(c, e, "lengthM", 0);
            double angle;
            if (e.Json.Has("rotationDeg") || Find(c, e, "rotationDeg") != null) angle = Num(c, e, "rotationDeg", 45) * Math.PI / 180;
            else if (e.Json.Has("verdetRadPerTM") || Find(c, e, "verdetRadPerTM") != null)
            {
                double V = Num(c, e, "verdetRadPerTM", double.NaN), B = Num(c, e, "fieldT", double.NaN);
                if (double.IsNaN(B) || !(L > 0)) throw new JsonException("faradayRotator with verdetRadPerTM needs fieldT and lengthM (rotation = V B L).");
                angle = V * B * L;
            }
            else angle = 45 * Math.PI / 180;
            double dir = Num(c, e, "direction", 1);
            angle *= Math.Sign(dir == 0 ? 1 : dir);
            string disp = Str(c, e, "verdetDispersion", "inverseSquare").ToLowerInvariant();
            if (disp != "inversesquare" && disp != "none") throw new JsonException("verdetDispersion must be \"inverseSquare\" or \"none\".");
            double pole = Num(c, e, "verdetPoleWavelengthM", 0.258e-6);
            if (e.Json.Has("material") || Find(c, e, "material") != null)
            {
                string matName = Find(c, e, "material")?.RawValue?.AsString() ?? e.Json.GetString("material");
                if (!(L > 0)) throw new JsonException("faradayRotator with a material needs lengthM (the crystal length).");
                CompileBulk(c, Inner(c, e, matName, L), ce, false);
                ce.LengthM = L;
                ce.TrackingIndex = GetMaterial(c, matName).IndexAt(c.Omega0);
            }
            var t = new FaradayTerm(angle, 2 * Math.PI * Cl / lamD, pole, disp == "inversesquare");
            ce.Post.Add(t);
            double tr = Num(c, e, "transmission", 1.0);
            if (tr < 1) ce.Post.Add(new FlatLossTerm(tr, "absorption", "Faraday rotator transmission"));
            c.Out.Provenance.Record("element:" + e.Id, "faraday", t.Description, ModelSource.SystemData, "system file");
        }

        // ================================================================================== thick waveplates

        /// <summary>Waveplate with a birefringent material: multi-order, true zero-order or compound zero-order, with
        /// full dispersion. Returns false when the waveplate has no material (the dispersionless design model is used).</summary>
        private static bool CompileThickWaveplate(Ctx c, SystemElement e, CompiledElement ce)
        {
            bool custom = e.Json.Has("indexOrdinary") && e.Json.Has("indexExtraordinary");
            string matName = Find(c, e, "material")?.RawValue?.AsString() ?? e.Json.GetString("material");
            if (matName == null && !custom) return false;
            Material mo, me;
            if (custom)
            {
                mo = new Material { Name = (matName ?? "user") + "-o", Index = c.Reg.Create<IRefractiveIndex>("index", ModelSpec.FromJson(e.Json["indexOrdinary"], ModelSource.SystemData, "element " + e.Id + ".indexOrdinary")) };
                me = new Material { Name = (matName ?? "user") + "-e", Index = c.Reg.Create<IRefractiveIndex>("index", ModelSpec.FromJson(e.Json["indexExtraordinary"], ModelSource.SystemData, "element " + e.Id + ".indexExtraordinary")) };
            }
            else
            {
                var pair = BirefringentMaterials.Get(matName) ?? throw new JsonException("waveplate material \"" + matName + "\" is not a known birefringent crystal (" + string.Join(", ", BirefringentMaterials.Names) + "); or give indexOrdinary and indexExtraordinary.");
                mo = GetMaterial(c, pair.O); me = GetMaterial(c, pair.E);
            }
            double lamD = Num(c, e, "designWavelengthM", 2 * Math.PI * Cl / c.Omega0);
            double wd = 2 * Math.PI * Cl / lamD;
            double dn = Math.Abs(me.IndexAt(wd) - mo.IndexAt(wd));
            double waves = e.Json.Has("retardanceWaves") || Find(c, e, "retardanceWaves") != null ? Num(c, e, "retardanceWaves", 0.5) :
                (e.Json.GetString("plate", "half").ToLowerInvariant().StartsWith("q") ? 0.25 : 0.5);
            string construction = Str(c, e, "construction", "single").ToLowerInvariant();
            double d1, d2 = 0;
            if (construction == "compound")
            {
                double dret = waves * lamD / dn;
                d1 = Num(c, e, "plateThicknessM", 1e-3);
                if (d1 <= dret) throw new JsonException("compound waveplate: plateThicknessM must exceed the retardation thickness " + (dret * 1e6).ToString("G4", CultureInfo.InvariantCulture) + " um.");
                d2 = d1 - dret;
            }
            else if (construction == "single")
            {
                if (e.Json.Has("thicknessM") || Find(c, e, "thicknessM") != null) d1 = Num(c, e, "thicknessM", 0);
                else { int order = (int)Num(c, e, "order", 0); d1 = (order + waves) * lamD / dn; }
            }
            else throw new JsonException("waveplate construction must be \"single\" or \"compound\".");
            double axis = Angle(c, e, "axis", 0);
            var t = new BirefringentPlateTerm(ce.Label, mo, me, d1, d2, axis);
            ce.Pre.Add(t);
            ce.LengthM = d1 + d2;
            ce.TrackingIndex = 0.5 * (mo.IndexAt(c.Omega0) + me.IndexAt(c.Omega0));
            double g0 = t.RetardanceRad(c.Omega0) / (2 * Math.PI);
            double dtau = Math.Abs(me.GroupIndexAt(c.Omega0) - mo.GroupIndexAt(c.Omega0)) * (d1 - d2) / Cl;
            ce.Notes.Add(string.Format(CultureInfo.InvariantCulture, "retardance at the carrier {0:G6} waves ({1:G4} waves mod 1); group delay difference of the axes {2:G4} fs; d = {3:G5} um{4}",
                g0, g0 - Math.Floor(g0), dtau * 1e15, d1 * 1e6, d2 > 0 ? " - " + (d2 * 1e6).ToString("G5", CultureInfo.InvariantCulture) + " um" : ""));
            c.Out.Provenance.Record("element:" + e.Id, "waveplate", t.Description, ModelSource.SystemData, "system file");
            return true;
        }

        // ================================================================================== aberrations

        /// <summary>An aberration object: {"zernikeNollWaves": [...], "radiusM", "referenceWavelengthM", "surface": false,
        /// "randomRmsM", "correlationLengthM", "seed"}. surfaceFactor converts surface numbers to wavefront.</summary>
        private static Aberration AberrationFrom(Ctx c, SystemElement e, double surfaceFactor)
        {
            var r = Find(c, e, "aberration");
            var v = r != null ? r.RawValue : e.Json["aberration"];
            if (v == null || v.IsNull) return null;
            var o = v.AsObject();
            var a = new Aberration
            {
                RadiusM = o.GetDouble("radiusM", double.NaN),
                ReferenceWavelengthM = o.GetDouble("referenceWavelengthM", 2 * Math.PI * Cl / c.Omega0),
                RandomRmsM = o.GetDouble("randomRmsM", 0),
                CorrelationLengthM = o.GetDouble("correlationLengthM", double.NaN),
                Seed = o.GetInt("seed", 1),
                SurfaceFactor = o.GetBool("surface", false) ? surfaceFactor : 1.0
            };
            var z = o.GetDoubleArray("zernikeNollWaves");
            if (z != null) for (int i = 0; i < z.Length; i++) a.Terms.Add((i + 1, z[i]));
            var terms = o.GetObject("terms");
            if (terms != null) foreach (var kv in terms) a.Terms.Add((int.Parse(kv.Key.TrimStart('Z', 'z'), CultureInfo.InvariantCulture), kv.Value.AsDouble()));
            if (a.Terms.Count > 0 && double.IsNaN(a.RadiusM)) throw new JsonException("aberration with Zernike terms needs radiusM (the normalisation radius).");
            c.Out.Provenance.Record("element:" + e.Id, "aberration", a.Describe(), r?.Source ?? ModelSource.SystemData, r?.Origin ?? "system file");
            return a.Any ? a : null;
        }

        /// <summary>The "wavefront" element: a pure wavefront error (and optionally focal power) at one plane.</summary>
        private static void CompileWavefront(Ctx c, SystemElement e, CompiledElement ce)
        {
            var ab = AberrationFrom(c, e, 1.0);
            if (ab == null && e.Json.Has("zernikeNollWaves"))
            {
                // flat form: the aberration keys on the element itself
                var j = new JsonObject();
                foreach (var k in new[] { "zernikeNollWaves", "radiusM", "referenceWavelengthM", "randomRmsM", "correlationLengthM", "seed", "terms" }) if (e.Json.Has(k)) j.Set(k, e.Json[k]);
                var se = new SystemElement { Id = e.Id, Type = e.Type, Json = new JsonObject().Set("aberration", j) };
                ab = AberrationFrom(c, se, 1.0);
            }
            else if (ab == null && e.Json.Has("randomRmsM"))
            {
                var j = new JsonObject();
                foreach (var k in new[] { "randomRmsM", "correlationLengthM", "seed" }) if (e.Json.Has(k)) j.Set(k, e.Json[k]);
                ab = AberrationFrom(c, new SystemElement { Id = e.Id, Type = e.Type, Json = new JsonObject().Set("aberration", j) }, 1.0);
            }
            double f = Num(c, e, "focalLengthM", double.PositiveInfinity);
            double fx = Num(c, e, "focalLengthXM", f), fy = Num(c, e, "focalLengthYM", f);
            ce.Active = new SurfaceOpticsElement("wavefront", fx, fy, null, GetMaterial(c, "Air"), c.Omega0, ab) { BeamRadiusForStrehlM = Num(c, e, "beamRadiusM", double.NaN) };
        }

        // ================================================================================== tracking

        /// <summary>Beam tracking through the Stage 1.2 components; false for other types.</summary>
        private static bool TrackComponent(Ctx c, SystemElement e, CompiledElement ce, BeamState t, string type)
        {
            switch (type)
            {
                case "plate": case "window": case "ndfilter":
                {
                    double aoi = AoiOf(c, e);
                    var medium = GetMaterial(c, Str(c, e, "medium", "Air"));
                    double n1 = medium.IndexAt(c.Omega0), n2 = ce.TrackingIndex > 0 ? ce.TrackingIndex : 1.5;
                    bool tx = BeamState.TangentialIsX(Angle(c, e, "planeOfIncidence", Math.PI / 2), out _);
                    // a tilted plate disperses inside and recombines at the exit face: spatial chirp ~ L sin / n, and a small GDD
                    string mat = Str(c, e, "material", e.Json.GetString("material", null)), med = Str(c, e, "medium", "Air");
                    var n1Of = IndexOf(c, med); var n2Of = IndexOf(c, mat);
                    t.TiltedInterface(n1, n2, aoi, tx, n1Of, n2Of);
                    t.Propagate(ce.LengthM, n2);
                    double th2 = Math.Asin(n1 * Math.Sin(aoi) / n2);
                    t.TiltedInterface(n2, n1, th2, tx, n2Of, n1Of);
                    return true;
                }
                case "prism":
                {
                    double aoi = Num(c, e, "aoiDeg", 0) * Math.PI / 180, apex = Num(c, e, "apexDeg", 0) * Math.PI / 180;
                    string med = Str(c, e, "medium", "Air"), mat = Str(c, e, "material", null);
                    double n1 = IndexOrOne(c, med), n2 = IndexOrOne(c, mat), th2 = Math.Asin(n1 * Math.Sin(aoi) / n2);
                    bool tx = BeamState.TangentialIsX(Angle(c, e, "planeOfIncidence", Math.PI / 2), out _);
                    t.TiltedInterface(n1, n2, aoi, tx, IndexOf(c, med), IndexOf(c, mat));
                    t.Propagate(ce.LengthM, n2);
                    t.TiltedInterface(n2, n1, th2 - Math.Sign(aoi == 0 ? 1 : aoi) * apex, tx, IndexOf(c, mat), IndexOf(c, med));
                    return true;
                }
                case "thinfilmpolarizer": case "tfp":
                {
                    if (Str(c, e, "output", "transmitted").ToLowerInvariant() == "reflected") return true; // flat mirror
                    double aoi = e.Json.Has("aoiDeg") || e.Json.Has("aoiRad") ? AoiOf(c, e) : 56.0 * Math.PI / 180;
                    double n2 = ce.TrackingIndex > 0 ? ce.TrackingIndex : 1.45;
                    bool tx = BeamState.TangentialIsX(Angle(c, e, "axis", 0), out _);   // plane of incidence contains the p axis
                    t.TiltedInterface(1.0, n2, aoi, tx);
                    t.Propagate(ce.LengthM, n2);
                    t.TiltedInterface(n2, 1.0, Math.Asin(Math.Sin(aoi) / n2), tx);
                    return true;
                }
                case "faradayrotator": case "faraday": case "waveplate":
                    if (ce.LengthM > 0) t.Propagate(ce.LengthM, ce.TrackingIndex > 0 ? ce.TrackingIndex : 1.0);
                    return true;
                case "wavefront": case "aberration":
                {
                    double f = Num(c, e, "focalLengthM", double.PositiveInfinity);
                    t.Lens(Num(c, e, "focalLengthXM", f), Num(c, e, "focalLengthYM", f));
                    return true;
                }
            }
            return false;
        }
    }
}
