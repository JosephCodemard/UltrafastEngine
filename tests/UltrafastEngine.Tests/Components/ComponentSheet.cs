// ComponentSheet.cs -- the shared, parametrised component test sheet (docs/design/next-basics-and-catalogue.md §1).
//
// A component is listed once as a SheetCase; ComponentSheet.Run answers the sheet's questions for it at every carrier
// (515 nm, 1030 nm, 2 um) and bandwidth (1 ps, 10 fs):
//   energy      output energy / input = the analytic transmission, and the loss budget closes
//   dispersion  the reported system GDD (and the field's fitted GDD) = the analytic GDD at the grid centre
//   carrier     the carrier is the run's, at each of the three
//   bandwidth   |A(w)|^2 keeps its rms width (linear elements: no silent narrowing)
//   topology    the same element in a Branched system gives the same field, to 1e-12
// Main types:
//   SheetCase(string name, Func<double, JsonObject> element, string port, string otherPort,
//             Func<double, double, double> gddS2 /* (lambda requested, omega grid centre) */, double transmission)
//   static List<string> ComponentSheet.Run(SheetCase c)   -> the failures (empty when the case passes)
// The verification scripts (tests/components/*.py) plot the same numbers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UltrafastEngine.Pulses;
using UltrafastEngine.Results;
using UltrafastEngine.Serialization;
using UltrafastEngine.Specs;
using UltrafastEngine.Systems;
using static UltrafastEngine.Tests.U;

namespace UltrafastEngine.Tests.Components
{
    internal sealed class SheetCase
    {
        public string Name;
        /// <summary>The element for a run at the requested carrier wavelength (m); its id must be "x".</summary>
        public Func<double, JsonObject> Element;
        /// <summary>Multi-port elements: the port that continues and the other one (null for in / out elements).</summary>
        public string Port, OtherPort;
        /// <summary>Analytic GDD (s^2) for (requested carrier wavelength, grid-centre angular frequency).</summary>
        public Func<double, double, double> GddS2;
        public double Transmission = 1.0;
        public double GddRelTol = 1e-6;
        public SheetCase(string name, Func<double, JsonObject> element, Func<double, double, double> gdd, double transmission = 1.0, string port = null, string otherPort = null)
        { Name = name; Element = element; GddS2 = gdd; Transmission = transmission; Port = port; OtherPort = otherPort; }
    }

    internal static class ComponentSheet
    {
        public static readonly double[] CarriersM = { 515e-9, 1030e-9, 2e-6 };
        public static readonly double[] DurationsS = { 1e-12, 10e-15 };

        private static RunSpec LinearSpec(double lam, double tau) => Spec(Gauss(lam, tau, 1e-9), O("physics", O("nonlinear", false)));

        public static RunResult Sequential(SheetCase c, double lam, double tau)
        {
            var e = c.Element(lam);
            if (c.Port != null) e.Set("output", c.Port);
            return Go(Sys(e), LinearSpec(lam, tau));
        }

        public static RunResult Branched(SheetCase c, double lam, double tau)
        {
            var e = c.Element(lam);
            if (c.Port != null) e.Set("next", O(c.Port, "end", c.OtherPort, "virtual"));
            else e.Set("next", "end");
            var s = Sys(e); s.Topology = SystemGraph.Branched;
            return Go(s, LinearSpec(lam, tau));
        }

        public static List<string> Run(SheetCase c)
        {
            var fails = new List<string>();
            var ci = CultureInfo.InvariantCulture;
            void Check(bool ok, string what) { if (!ok) fails.Add(c.Name + ": " + what); }
            foreach (double lam in CarriersM)
                foreach (double tau in DurationsS)
                {
                    string at = string.Format(ci, " at {0:G4} nm, {1:G3} fs", lam * 1e9, tau * 1e15);
                    var seq = Sequential(c, lam, tau);
                    double e0 = seq.Compiled.Input.TotalEnergyJ(), e1 = seq.Output.TotalEnergyJ();
                    // 1e-7: a 10 fs pulse at 2 um has 6e-8 of its energy at the grid's lowest frequencies, which a grating removes
                    Check(Math.Abs(e1 / e0 - c.Transmission) <= 1e-7, string.Format(ci, "energy {0:R} != {1:R}{2}", e1 / e0, c.Transmission, at));
                    double booked = seq.Records.Sum(r => r.LossJ.Values.Sum());
                    Check(Math.Abs(e0 - e1 - booked) <= 1e-9 * e0, "loss budget does not close" + at);
                    double wc = seq.Output.Grid.OmegaCenter, want = c.GddS2(lam, 2 * Math.PI * C / lam);   // reported at the requested carrier
                    Check(Math.Abs(seq.FinalSystem.GddS2 - want) <= c.GddRelTol * Math.Abs(want) + 1e-33,
                        string.Format(ci, "reported GDD {0:G8} fs^2 != {1:G8} fs^2{2}", seq.FinalSystem.GddS2 * 1e30, want * 1e30, at));
                    Check(Math.Abs(2 * Math.PI * C / wc / lam - 1) < 0.02, "grid centre is not near the carrier" + at);
                    double wIn = RmsSpectralWidth(seq.Compiled.Input), wOut = RmsSpectralWidth(seq.Output);
                    // 1e-6: fused silica's tabulated absorption towards 4 um trims a 10 fs, 2 um spectrum by 1e-8
                    Check(Math.Abs(wOut / wIn - 1) < 1e-6, string.Format(ci, "rms spectral width changed by {0:G3}{1}", wOut / wIn - 1, at));
                    var br = Branched(c, lam, tau);
                    double peak = seq.Output.Ax.Max(z => z.Magnitude), diff = 0;
                    for (int i = 0; i < seq.Output.Grid.N; i++) diff = Math.Max(diff, (seq.Output.Ax[i] - br.Output.Ax[i]).Magnitude);
                    Check(br.Output.Grid.N == seq.Output.Grid.N && diff <= 1e-12 * peak, string.Format(ci, "Branched field differs by {0:G3} of the peak{1}", diff / peak, at));
                    Check(Math.Abs(br.FinalSystem.GddS2 - seq.FinalSystem.GddS2) <= 1e-12 * Math.Abs(want) + 1e-36, "Branched reports another GDD" + at);
                }
            return fails;
        }

        /// <summary>Absolute index of a catalogue glass: Sellmeier relative to air x Ciddor air (as the catalogue converts).</summary>
        public static double GlassIndex(double[] B, double[] Cum2, double lamM)
        {
            double l2 = lamM * 1e6 * lamM * 1e6, n2 = 1;
            for (int k = 0; k < B.Length; k++) n2 += B[k] * l2 / (l2 - Cum2[k]);
            var air = new Materials.CiddorAirIndex(); var nAir = new double[1];
            air.Evaluate(new[] { 2 * Math.PI * C / lamM }, nAir);
            return Math.Sqrt(n2) * nAir[0];
        }

        /// <summary>GDD (s^2) of a length L of a medium n(lambda): second derivative of the excess phase w (n - 1) L / c, 5-point.</summary>
        public static double MediumGdd(Func<double, double> nOfLam, double L, double w0)
        {
            double h = 2e-3 * w0;
            double Phi(double w) => w * (nOfLam(2 * Math.PI * C / w) - 1) * L / C;
            return (-Phi(w0 + 2 * h) + 16 * Phi(w0 + h) - 30 * Phi(w0) + 16 * Phi(w0 - h) - Phi(w0 - 2 * h)) / (12 * h * h);
        }

        /// <summary>Treacy (IEEE JQE 5, 454 (1969)): GDD = -passes m^2 lambda^3 G / (2 pi c^2 d^2 cos^3 theta_d), sin theta_d = |m| lambda / d - sin theta_i.</summary>
        public static double TreacyGdd(double lam, double linesPerM, double aoiRad, double G, int order, int passes)
        {
            double d = 1 / linesPerM, m = Math.Abs(order), sd = m * lam / d - Math.Sin(aoiRad), cd = Math.Sqrt(1 - sd * sd);
            return -passes * m * m * lam * lam * lam * G / (2 * Math.PI * C * C * d * d * cd * cd * cd);
        }
    }
}
