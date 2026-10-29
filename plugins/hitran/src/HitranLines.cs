using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UltrafastEngine.Serialization;

namespace UltrafastEngine.Plugins.Hitran
{
    /// <summary>One transition of a HITRAN line list. Units as in HITRAN: wavenumbers in cm-1, S in cm-1/(molecule cm-2) at
    /// 296 K (already weighted by the isotopologue abundance), widths in cm-1/atm (HWHM), E'' in cm-1.</summary>
    public struct HitranLine
    {
        public int Molecule, Isotopologue;
        public double NuCm1, S, A, GammaAir, GammaSelf, ElowerCm1, NAir, DeltaAir;
    }

    /// <summary>Reads line lists: the 160-character HITRAN ".par" format (HITRAN2004 and later) and CSV with a header row
    /// (columns nu, sw, a, gamma_air, gamma_self, elower, n_air, delta_air, molec_id, local_iso_id -- the HAPI names).</summary>
    public static class HitranFile
    {
        public static List<HitranLine> Read(string path, double minCm1 = double.NegativeInfinity, double maxCm1 = double.PositiveInfinity)
        {
            if (!File.Exists(path)) throw new JsonException("HITRAN line file not found: " + path);
            bool csv = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase);
            var lines = new List<HitranLine>();
            using (var rd = new StreamReader(path))
            {
                string row; int no = 0; int[] col = null; char sep = ',';
                while ((row = rd.ReadLine()) != null)
                {
                    no++;
                    if (row.Trim().Length == 0 || row.TrimStart().StartsWith("#")) continue;
                    HitranLine l;
                    if (csv)
                    {
                        if (col == null)
                        {
                            sep = row.Contains("\t") ? '\t' : row.Contains(";") ? ';' : ',';
                            col = Header(row.Split(sep), path);
                            continue;
                        }
                        l = FromCsv(row.Split(sep), col, path, no);
                    }
                    else l = FromPar(row, path, no);
                    if (l.NuCm1 < minCm1 || l.NuCm1 > maxCm1) continue;
                    lines.Add(l);
                }
            }
            return lines;
        }

        private static readonly string[] Names = { "molec_id", "local_iso_id", "nu", "sw", "a", "gamma_air", "gamma_self", "elower", "n_air", "delta_air" };

        private static int[] Header(string[] h, string path)
        {
            var col = new int[Names.Length];
            for (int k = 0; k < Names.Length; k++)
            {
                col[k] = -1;
                for (int i = 0; i < h.Length; i++) if (string.Equals(h[i].Trim(), Names[k], StringComparison.OrdinalIgnoreCase) || (k == 3 && string.Equals(h[i].Trim(), "s", StringComparison.OrdinalIgnoreCase))) col[k] = i;
            }
            if (col[2] < 0 || col[3] < 0) throw new JsonException(path + ": a CSV line list needs the columns nu and sw (HAPI names); optional: molec_id, local_iso_id, a, gamma_air, gamma_self, elower, n_air, delta_air.");
            return col;
        }

        private static double D(string[] f, int[] col, int k, double fallback)
            => col[k] >= 0 && col[k] < f.Length && double.TryParse(f[col[k]].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static HitranLine FromCsv(string[] f, int[] col, string path, int no)
        {
            if (D(f, col, 2, double.NaN) is double nu && double.IsNaN(nu)) throw new JsonException(path + " line " + no + ": cannot read nu.");
            return new HitranLine
            {
                Molecule = (int)D(f, col, 0, 0), Isotopologue = (int)D(f, col, 1, 1), NuCm1 = D(f, col, 2, 0), S = D(f, col, 3, 0), A = D(f, col, 4, 0),
                GammaAir = D(f, col, 5, 0.07), GammaSelf = D(f, col, 6, 0.4), ElowerCm1 = D(f, col, 7, 0), NAir = D(f, col, 8, 0.7), DeltaAir = D(f, col, 9, 0)
            };
        }

        private static double F(string row, int start, int len, string path, int no, string what)
        {
            if (row.Length < start + len - 1 && row.Length < start) throw new JsonException(path + " line " + no + ": record too short for " + what + ".");
            var s = row.Substring(start - 1, Math.Min(len, row.Length - start + 1)).Trim();
            if (s.Length == 0) return 0;
            if (!double.TryParse(s.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new JsonException(path + " line " + no + ": cannot read " + what + " from '" + s + "' (HITRAN .par columns expected).");
            return v;
        }

        /// <summary>HITRAN160: I2 molecule, I1 isotopologue, F12.6 nu, E10.3 S, E10.3 A, F5.4 gamma_air, F5.3 gamma_self,
        /// F10.4 E'', F4.2 n_air, F8.6 delta_air (columns 1-67; the rest is not used).</summary>
        private static HitranLine FromPar(string row, string path, int no)
        {
            if (row.Length < 67) throw new JsonException(path + " line " + no + ": a HITRAN .par record has 160 characters (got " + row.Length + ").");
            int iso = row[2] == '0' ? 10 : (int)F(row, 3, 1, path, no, "isotopologue");
            return new HitranLine
            {
                Molecule = (int)F(row, 1, 2, path, no, "molecule"), Isotopologue = iso,
                NuCm1 = F(row, 4, 12, path, no, "nu"), S = F(row, 16, 10, path, no, "S"), A = F(row, 26, 10, path, no, "A"),
                GammaAir = F(row, 36, 5, path, no, "gamma_air"), GammaSelf = F(row, 41, 5, path, no, "gamma_self"),
                ElowerCm1 = F(row, 46, 10, path, no, "E''"), NAir = F(row, 56, 4, path, no, "n_air"), DeltaAir = F(row, 60, 8, path, no, "delta_air")
            };
        }

        /// <summary>One HITRAN .par record (160 characters; quanta and references blank). Used by tests and the downloader.</summary>
        public static string FormatPar(HitranLine l)
        {
            string iso = l.Isotopologue >= 10 ? "0" : l.Isotopologue.ToString(CultureInfo.InvariantCulture);
            string s = l.Molecule.ToString("00", CultureInfo.InvariantCulture) + iso
                + l.NuCm1.ToString("F6", CultureInfo.InvariantCulture).PadLeft(12)
                + FormatE(l.S).PadLeft(10) + FormatE(l.A).PadLeft(10)
                + Fw(l.GammaAir, 4, 5) + Fw(l.GammaSelf, 3, 5) + Fw(l.ElowerCm1, 4, 10) + Fw(l.NAir, 2, 4) + Fw(l.DeltaAir, 6, 8);
            return s.PadRight(160);
        }

        /// <summary>Fortran Fw.d: the leading zero is dropped when the field would overflow (0.0990 in F5.4 is ".0990").</summary>
        private static string Fw(double v, int d, int w)
        {
            string s = v.ToString("F" + d, CultureInfo.InvariantCulture);
            if (s.Length > w) s = s.Replace("0.", ".");
            return s.PadLeft(w);
        }

        private static string FormatE(double v) => v.ToString("0.000E+00", CultureInfo.InvariantCulture);
    }
}
