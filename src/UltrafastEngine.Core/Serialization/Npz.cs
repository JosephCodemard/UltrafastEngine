using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace UltrafastEngine.Serialization
{
    /// <summary>
    /// A named n-dimensional float64 array (row-major), the unit of numerical output. Everything the engine
    /// produces as an array -- spectra, temporal profiles, instrument traces, per-element tables -- is one of
    /// these, so it can be written to NumPy .npz, read back through the C API, or flattened to CSV.
    /// </summary>
    public sealed class NdArray
    {
        public string Name { get; }
        public int[] Shape { get; }
        public double[] Data { get; }
        /// <summary>Optional unit string (e.g. "s", "rad/s", "W"), exported in the result manifest.</summary>
        public string Unit { get; set; }
        /// <summary>Optional one-line description, exported in the result manifest.</summary>
        public string Description { get; set; }

        public NdArray(string name, double[] data, params int[] shape)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Shape = (shape == null || shape.Length == 0) ? new[] { data.Length } : shape;
            long n = 1; foreach (var s in Shape) n *= s;
            if (n != data.Length) throw new ArgumentException("Array '" + name + "': shape [" + string.Join(",", Shape) + "] does not match length " + data.Length + ".");
        }

        public static NdArray FromMatrix(string name, double[,] m)
        {
            int r = m.GetLength(0), c = m.GetLength(1);
            var d = new double[r * c];
            for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) d[i * c + j] = m[i, j];
            return new NdArray(name, d, r, c);
        }

        public int Rows => Shape.Length >= 1 ? Shape[0] : 1;
        public int Cols => Shape.Length >= 2 ? Shape[1] : 1;
    }

    /// <summary>Writes NumPy .npy / .npz files (uncompressed or deflated) with no dependencies.</summary>
    public static class NpzWriter
    {
        /// <summary>The bytes of a .npy (format 1.0) file holding <paramref name="a"/> as little-endian float64.</summary>
        public static byte[] ToNpy(NdArray a)
        {
            string shape = a.Shape.Length == 1 ? "(" + a.Shape[0] + ",)" : "(" + string.Join(", ", a.Shape.Select(s => s.ToString(CultureInfo.InvariantCulture))) + ")";
            string header = "{'descr': '<f8', 'fortran_order': False, 'shape': " + shape + ", }";
            // magic(6) + version(2) + headerlen(2) + header + padding + \n must be a multiple of 64.
            int total = 10 + header.Length + 1;
            int pad = (64 - total % 64) % 64;
            header = header + new string(' ', pad) + "\n";
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write((byte)0x93); w.Write(Encoding.ASCII.GetBytes("NUMPY"));
                w.Write((byte)1); w.Write((byte)0);
                w.Write((ushort)header.Length);
                w.Write(Encoding.ASCII.GetBytes(header));
                var bytes = new byte[a.Data.Length * 8];
                Buffer.BlockCopy(a.Data, 0, bytes, 0, bytes.Length);
                if (!BitConverter.IsLittleEndian)
                    for (int i = 0; i < a.Data.Length; i++) Array.Reverse(bytes, i * 8, 8);
                w.Write(bytes);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Write all arrays to one .npz (a zip of name.npy entries), loadable with numpy.load().</summary>
        public static void Write(string path, IEnumerable<NdArray> arrays, bool compress = true)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var a in arrays)
                {
                    var entry = zip.CreateEntry(a.Name + ".npy", compress ? CompressionLevel.Fastest : CompressionLevel.NoCompression);
                    using (var es = entry.Open())
                    {
                        var b = ToNpy(a);
                        es.Write(b, 0, b.Length);
                    }
                }
            }
        }

        /// <summary>Read back a float64 .npz written by <see cref="Write"/> (used by tests and the replay tools).</summary>
        public static List<NdArray> Read(string path)
        {
            var result = new List<NdArray>();
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var e in zip.Entries)
                {
                    using (var es = e.Open())
                    using (var ms = new MemoryStream())
                    {
                        es.CopyTo(ms);
                        var b = ms.ToArray();
                        // .npy v1 (2-byte header length) or v2/v3 (4-byte); little-endian float64/float32/int64/int32/uint8/bool
                        int major = b[6];
                        int hlen = major >= 2 ? (int)BitConverter.ToUInt32(b, 8) : BitConverter.ToUInt16(b, 8);
                        int hstart = major >= 2 ? 12 : 10;
                        string header = Encoding.ASCII.GetString(b, hstart, hlen);
                        int d0 = header.IndexOf("'descr':", StringComparison.Ordinal);
                        string descr = d0 >= 0 ? header.Substring(header.IndexOf('\'', d0 + 8) + 1, 3) : "<f8";
                        bool fortran = header.Contains("'fortran_order': True");
                        int s0 = header.IndexOf("'shape': (", StringComparison.Ordinal) + 10;
                        int s1 = header.IndexOf(')', s0);
                        var shape = header.Substring(s0, s1 - s0).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => int.Parse(x.Trim().TrimEnd('L'), CultureInfo.InvariantCulture)).ToArray();
                        int off = hstart + hlen;
                        double[] data;
                        switch (descr)
                        {
                            case "<f8": data = new double[(b.Length - off) / 8]; Buffer.BlockCopy(b, off, data, 0, data.Length * 8); break;
                            case "<f4": data = new double[(b.Length - off) / 4]; for (int i = 0; i < data.Length; i++) data[i] = BitConverter.ToSingle(b, off + 4 * i); break;
                            case "<i8": data = new double[(b.Length - off) / 8]; for (int i = 0; i < data.Length; i++) data[i] = BitConverter.ToInt64(b, off + 8 * i); break;
                            case "<i4": data = new double[(b.Length - off) / 4]; for (int i = 0; i < data.Length; i++) data[i] = BitConverter.ToInt32(b, off + 4 * i); break;
                            case "|u1": case "|b1": data = new double[b.Length - off]; for (int i = 0; i < data.Length; i++) data[i] = b[off + i]; break;
                            default: throw new NotSupportedException("Unsupported .npy dtype " + descr + " (" + e.Name + "): save float64, float32, int64 or int32 arrays.");
                        }
                        long count = 1; foreach (var sv in shape) count *= sv;
                        if (shape.Length > 0 && data.Length > count) Array.Resize(ref data, (int)count);
                        if (fortran && shape.Length == 2)
                        {
                            int r = shape[0], c = shape[1]; var t = new double[data.Length];
                            for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) t[i * c + j] = data[j * r + i];
                            data = t;
                        }
                        string name = e.Name.EndsWith(".npy", StringComparison.Ordinal) ? e.Name.Substring(0, e.Name.Length - 4) : e.Name;
                        result.Add(new NdArray(name, data, shape.Length == 0 ? new[] { 1 } : shape));
                    }
                }
            }
            return result;
        }
    }
}
