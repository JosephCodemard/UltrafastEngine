using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltrafastEngine.Pulses
{
    /// <summary>A measured input spectrum after cleaning: ascending finite wavelengths without duplicates, background
    /// removed, with the notes and warnings that describe what was done.</summary>
    public sealed class PreparedSpectrum
    {
        /// <summary>Wavelengths (m), strictly ascending.</summary>
        public double[] WavelengthsM { get; set; }
        /// <summary>Intensity per wavelength, background removed, never negative.</summary>
        public double[] Intensity { get; set; }
        /// <summary>Spectral phase (rad), or null for a flat phase.</summary>
        public double[] PhaseRad { get; set; }
        /// <summary>Number of rows that were not in wavelength order (sorted).</summary>
        public int OutOfOrder { get; set; }
        /// <summary>Number of rows merged into a neighbour because their wavelength repeated.</summary>
        public int Merged { get; set; }
        /// <summary>Number of rows dropped because a value was not a finite number (or the wavelength was not positive).</summary>
        public int Dropped { get; set; }
        /// <summary>The background level subtracted (same unit as the intensity), 0 if none.</summary>
        public double Background { get; set; }
        /// <summary>The background level relative to the peak of the raw data.</summary>
        public double BackgroundRelative { get; set; }
        /// <summary>The floor found in the data (lower of the two edge medians) relative to the raw peak.</summary>
        public double DetectedFloorRelative { get; set; }
        public List<string> Notes { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Cleaning of a measured (often digitised) input spectrum before it is used:
    /// <list type="number">
    /// <item>rows with a non-finite value or a non-positive wavelength are dropped;</item>
    /// <item>rows are sorted by wavelength (digitised curves often have a few points out of order);</item>
    /// <item>rows whose wavelengths repeat (spacing below 1e-3 of the median spacing) are merged by averaging -- a
    /// repeated wavelength would otherwise make the interpolation divide by zero and every result NaN;</item>
    /// <item>a background is subtracted: <c>"background": number | "auto" | "min" | "none"</c>. "auto" is the median of
    /// the lowest 5 % of the points at each end, taking the lower of the two ends, so a spectrum cut off on one side
    /// (by the spectrometer range) still gives its true floor;</item>
    /// <item>values below <c>threshold</c> x peak (after subtraction) are set to zero, negative values are clipped.</item>
    /// </list>
    /// A flat floor matters: it adds a broad pedestal of bandwidth and shortens the transform limit (a 0.66 % floor over a
    /// wide recording can halve it), so a floor above 0.3 % of the peak that is not removed gives a warning.
    /// </summary>
    public static class MeasuredSpectrumPreparation
    {
        /// <summary>Relative floor above which an un-subtracted background is reported.</summary>
        public const double FloorWarningLevel = 3e-3;

        /// <summary>Clean a measured spectrum. <paramref name="backgroundMode"/>: "none", "auto", "min" or "value"
        /// (then <paramref name="backgroundValue"/> is subtracted). <paramref name="threshold"/>: fraction of the peak
        /// below which the intensity is set to zero after subtraction.</summary>
        public static PreparedSpectrum Prepare(double[] wavelengthsM, double[] intensity, double[] phaseRad,
            string backgroundMode = "none", double backgroundValue = 0, double threshold = 0)
        {
            if (wavelengthsM == null || intensity == null) throw new ArgumentException("measured spectrum: wavelengths and intensity are required.");
            if (wavelengthsM.Length != intensity.Length) throw new ArgumentException("measured spectrum: wavelengths and intensity differ in length.");
            if (phaseRad != null && phaseRad.Length != wavelengthsM.Length) throw new ArgumentException("measured spectrum: phase and wavelengths differ in length.");
            var ci = CultureInfo.InvariantCulture;
            var r = new PreparedSpectrum();

            // 1. finite rows
            var keep = new List<int>();
            for (int i = 0; i < wavelengthsM.Length; i++)
            {
                bool ok = IsFinite(wavelengthsM[i]) && wavelengthsM[i] > 0 && IsFinite(intensity[i]) && (phaseRad == null || IsFinite(phaseRad[i]));
                if (ok) keep.Add(i); else r.Dropped++;
            }
            if (keep.Count < 2) throw new ArgumentException("measured spectrum: fewer than two usable rows (" + r.Dropped + " rows had a non-finite value or a non-positive wavelength).");

            // 2. order: count the steps against the dominant direction
            int up = 0, down = 0;
            for (int k = 1; k < keep.Count; k++) { double d = wavelengthsM[keep[k]] - wavelengthsM[keep[k - 1]]; if (d > 0) up++; else if (d < 0) down++; }
            r.OutOfOrder = Math.Min(up, down);
            var ord = keep.OrderBy(i => wavelengthsM[i]).ToArray();

            // 3. merge repeated wavelengths
            var gaps = new List<double>();
            for (int k = 1; k < ord.Length; k++) { double d = wavelengthsM[ord[k]] - wavelengthsM[ord[k - 1]]; if (d > 0) gaps.Add(d); }
            double median = gaps.Count > 0 ? Median(gaps) : 0;
            double tol = 1e-3 * median;
            var wl = new List<double>(); var iv = new List<double>(); var ph = phaseRad != null ? new List<double>() : null;
            int start = 0;
            while (start < ord.Length)
            {
                int end = start + 1;
                while (end < ord.Length && wavelengthsM[ord[end]] - wavelengthsM[ord[start]] <= tol) end++;
                int cnt = end - start;
                double sw = 0, si = 0, sp = 0;
                for (int k = start; k < end; k++) { sw += wavelengthsM[ord[k]]; si += intensity[ord[k]]; if (ph != null) sp += phaseRad[ord[k]]; }
                wl.Add(sw / cnt); iv.Add(si / cnt); ph?.Add(sp / cnt);
                r.Merged += cnt - 1;
                start = end;
            }
            if (wl.Count < 2) throw new ArgumentException("measured spectrum: all rows have the same wavelength.");

            // 4. background
            double peak = iv.Max();
            if (!(peak > 0)) throw new ArgumentException("measured spectrum: the intensity is nowhere positive.");
            int edge = Math.Max(3, (int)Math.Ceiling(0.05 * wl.Count));
            edge = Math.Min(edge, Math.Max(1, wl.Count / 4));
            double lo = Median(iv.Take(edge)), hi = Median(iv.Skip(iv.Count - edge));
            double floor = Math.Min(lo, hi);
            r.DetectedFloorRelative = floor / peak;
            string mode = (backgroundMode ?? "none").ToLowerInvariant();
            double bg;
            switch (mode)
            {
                case "none": case "": bg = 0; break;
                case "auto": bg = floor; break;
                case "min": bg = iv.Min(); break;
                case "value": bg = backgroundValue; break;
                default: throw new ArgumentException("measured spectrum: background must be a number, \"auto\", \"min\" or \"none\" (got \"" + backgroundMode + "\").");
            }
            r.Background = bg; r.BackgroundRelative = bg / peak;
            double peakAfter = peak - bg;
            if (!(peakAfter > 0)) throw new ArgumentException(string.Format(ci, "measured spectrum: the background ({0:G4}) is not below the peak ({1:G4}).", bg, peak));
            double cut = Math.Max(0, threshold) * peakAfter;
            int zeroed = 0;
            for (int k = 0; k < iv.Count; k++)
            {
                double v = iv[k] - bg;
                if (v < cut || v < 0) { if (iv[k] - bg > 0) zeroed++; v = 0; }
                iv[k] = v;
            }

            r.WavelengthsM = wl.ToArray(); r.Intensity = iv.ToArray(); r.PhaseRad = ph?.ToArray();
            if (r.Dropped > 0) r.Warnings.Add(r.Dropped + " row(s) of the measured spectrum had a non-finite value or a non-positive wavelength and were dropped.");
            if (r.OutOfOrder > 0) r.Notes.Add("Measured spectrum: " + r.OutOfOrder + " row(s) were out of wavelength order (typical of digitised curves) and were sorted.");
            if (r.Merged > 0) r.Notes.Add("Measured spectrum: " + r.Merged + " row(s) repeated a wavelength and were merged with their neighbour (intensity and phase averaged).");
            if (bg != 0) r.Notes.Add(string.Format(ci, "Measured spectrum: background {0:G4} subtracted ({1:G3} % of the peak, {2}){3}.", bg, 100 * bg / peak,
                mode == "auto" ? "auto: median of the darker edge" : mode == "min" ? "the minimum" : "given",
                cut > 0 ? string.Format(ci, "; values below {0:G3} % of the peak set to zero", 100 * threshold) : ""));
            else if (cut > 0) r.Notes.Add(string.Format(ci, "Measured spectrum: values below {0:G3} % of the peak set to zero.", 100 * threshold));
            if (mode == "none" && r.DetectedFloorRelative > FloorWarningLevel)
                r.Warnings.Add(string.Format(ci, "The measured spectrum has a floor of {0:G3} % of its peak at both ends; a flat floor spreads light over the whole recorded band and shortens the transform limit. If it is a dark level or stray light, subtract it with \"background\": \"auto\" (or a number) in pulse.spectrum.", 100 * r.DetectedFloorRelative));
            return r;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static double Median(IEnumerable<double> v)
        {
            var a = v.OrderBy(x => x).ToArray();
            if (a.Length == 0) return 0;
            return a.Length % 2 == 1 ? a[a.Length / 2] : 0.5 * (a[a.Length / 2 - 1] + a[a.Length / 2]);
        }
    }
}
