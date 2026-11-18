// DeviceResponses.cs -- time-domain responses written as C source, so a device back-end (the gpu plugin) can evaluate them in a kernel.
//
// The envelope propagators add the index change of every extra time-domain response (ITimeDomainResponse) of an element,
// summed in list order, uRe = (0 + re_1) + re_2 + ..., to the Kerr index change. A response that is pure arithmetic of the
// response variables (ExpressionTimeResponse.Vars) can implement IDeviceTimeResponse and give its re and im parts as C
// expressions; DeviceResponses.Build sums them in the same order. Callbacks (user code), frequency-resolved (Drude) responses
// and expressions with a non-finite constant have no C form: the element then stays on the CPU.
//
// Variables in the C source are "v_" + the expression variable name (v_t, v_t_lab, v_I, v_Ix, v_F, ...); the kernel defines
// them per sample. "F" (the running fluence, a prefix sum over time) is the only one that is not local to a sample:
// DeviceResponseCode.UsesFluence says whether the kernel must compute it.
//
// Classes: IDeviceTimeResponse (TryDeviceCode), DeviceResponseCode (Re, Im, UsesFluence, Key), DeviceResponses
//   Build(responses, out why) -> code or null
using System;
using System.Collections.Generic;
using System.Linq;
using UltrafastEngine.Models;

namespace UltrafastEngine.Physics
{
    /// <summary>A time-domain response that can be evaluated in a device kernel (see <see cref="DeviceResponses"/>).</summary>
    public interface IDeviceTimeResponse
    {
        /// <summary>The index change as C expressions of the response variables (each written as <paramref name="variable"/>(name));
        /// either may be null (no real / no imaginary part). False when the response has no C form.</summary>
        bool TryDeviceCode(Func<string, string> variable, ISet<string> used, out string re, out string im);
    }

    /// <summary>The summed index change of an element's extra time-domain responses as C source.</summary>
    public sealed class DeviceResponseCode
    {
        /// <summary>C expressions for the real and imaginary index change (double), in the variables v_name.</summary>
        public string Re, Im;
        /// <summary>True when the source reads v_F, the running fluence (J/m^2) of the total intensity.</summary>
        public bool UsesFluence;
        /// <summary>A short stable key of the source (for naming and caching the compiled kernel).</summary>
        public string Key;
    }

    /// <summary>
    /// The plasma term (<see cref="PlasmaResponse"/>) in a form a device kernel evaluates: the ionisation rate of each channel tabulated
    /// as ln W against ln I (Catmull-Rom cubic between nodes; <see cref="RateFromTable"/> is the host mirror of the kernel), the constants of
    /// the rate equation and the Drude term, and for the frequency-resolved Drude index the spectral factor i (omega / c) R(omega) in the
    /// engine's centred order. The device runs the same time scan as <c>PlasmaResponse.Run</c> (envelope mode only).
    /// </summary>
    public sealed class PlasmaDeviceSpec
    {
        public const int TableN = 16384;
        /// <summary>Table range of ln I (I in W/m^2): 1e8 to 1e22 W/m^2. Below it the rate is 0; above it ln W is extrapolated linearly.</summary>
        public const double LnIMin = 18.420680743952367, LnIMax = 50.65687204586901;
        public int Channels;
        public double[] DensityM3, EnergyJ;
        /// <summary>ln W at TableN nodes per channel (channel-major).</summary>
        public double[] LnRate;
        public double Drude, OmegaTauC, K0, Sigma, UMean, RecombinationRatePerS, RecombinationBetaM3PerS, InitialDensityM3;
        public bool Avalanche, Absorption, FrequencyResolved;
        /// <summary>i (omega / c) R(omega, omega0) per bin, centred order (0 for omega &lt;= 0); null unless <see cref="FrequencyResolved"/>.</summary>
        public System.Numerics.Complex[] SpectralRate;
        /// <summary>Called by the device back-end with the peak electron density it found (the CPU path records it itself).</summary>
        public Action<double> ReportPeakDensity;

        public double DLnI => (LnIMax - LnIMin) / (TableN - 1);

        /// <summary>The rate of channel <paramref name="k"/> at intensity I from the table, exactly as the kernel computes it.</summary>
        public double RateFromTable(int k, double intensity)
        {
            if (!(intensity > 0)) return 0;
            double x = (Math.Log(intensity) - LnIMin) / DLnI;
            if (x < 0) return 0;
            int o = k * TableN;
            if (x >= TableN - 1) { double s = LnRate[o + TableN - 1] - LnRate[o + TableN - 2]; return Math.Exp(LnRate[o + TableN - 1] + s * (x - (TableN - 1))); }
            int i = (int)x; double f = x - i;
            double p1 = LnRate[o + i], p2 = LnRate[o + i + 1];
            double p0 = i > 0 ? LnRate[o + i - 1] : 2 * p1 - p2, p3 = i + 2 < TableN ? LnRate[o + i + 2] : 2 * p2 - p1;
            double v = p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
            return Math.Exp(v);
        }
    }

    public static class DeviceResponses
    {
        /// <summary>The name of a response variable in the generated C source.</summary>
        public static string VariableName(string name) => "v_" + name;

        /// <summary>The C source of the sum of <paramref name="responses"/> (same order as the CPU), or null with the reason.</summary>
        public static DeviceResponseCode Build(IList<ITimeDomainResponse> responses, out string why)
        {
            why = null;
            string re = "0.0", im = "0.0";
            var used = new HashSet<string>();
            foreach (var r in responses)
            {
                if (r.NeedsCarrierResolution) { why = "a carrier-resolved response (" + r.Description + ")"; return null; }
                if (r is IDispersiveResponse d && d.FrequencyResolved) { why = "a frequency-resolved response (" + r.Description + ")"; return null; }
                if (!(r is IDeviceTimeResponse dr) || !dr.TryDeviceCode(VariableName, used, out var a, out var b))
                {
                    why = "a response without a device form (" + r.Description + ")"; return null;
                }
                re = "(" + re + " + " + (a ?? "0.0") + ")";
                im = "(" + im + " + " + (b ?? "0.0") + ")";
            }
            var code = new DeviceResponseCode { Re = re, Im = im, UsesFluence = used.Contains("F") };
            code.Key = Hash(re + "|" + im);
            return code;
        }

        /// <summary>FNV-1a 64 of the text, as 16 hex digits.</summary>
        internal static string Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
            return h.ToString("x16");
        }
    }
}
