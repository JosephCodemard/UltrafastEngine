// ZosSystemAccess.cs -- ZosOpticStudio, part 2 of 3 (see ZosOpticStudio.cs): reading and writing the optical system over
// ZOS-API -- system data (lens units, environment, wavelengths) and the Lens Data Editor (surfaces, global matrices, indices,
// SetGradient1 write-back). Every TODO(zemax-hw) is an assumption listed in plugins/zemax/HARDWARE-CHECKLIST.md.
//   double LensUnitToMeters; double TemperatureC, PressureAtm; int NumberOfSurfaces; SurfaceRow GetSurface(int)
//   GetWavelengths(out int primary); int SetWavelengths(IReadOnlyList<double> um); RestoreWavelengths(...)
//   double[] GlobalMatrix(int surface); void GetIndex(int surface, int waves, double[] dest); void SetGradient1(...)
using System;
using System.Collections.Generic;
using System.Linq;

namespace UltrafastEngine.Zemax
{
    public sealed partial class ZosOpticStudio
    {
        public void SetGradient1(int surface, double n0, double nr2, double deltaT)
        {
            var row = _s.LDE.GetSurfaceAt(surface);
            var settings = row.GetSurfaceTypeSettings(ZOSAPI.Editors.LDE.SurfaceType.Gradient1);
            if (!row.ChangeType(settings)) throw new InvalidOperationException("OpticStudio refused to change surface " + surface + " to Gradient 1");
            var gr = row.SurfaceData as ZOSAPI.Editors.LDE.ISurfaceGradient1 ?? throw new InvalidOperationException("surface " + surface + " has no Gradient 1 data after the change");
            gr.DeltaT = deltaT; gr.n0 = n0; gr.Nr1 = 0; gr.Nr2 = nr2;
        }

        public double LensUnitToMeters
        {
            get
            {
                // A wrong lens unit scales every length of the export (up to 1000x), so a failure here is an error, not a default.
                // TODO(zemax-hw): confirm the ZemaxUnits member names on a real OpticStudio (Millimeters, Centimeters, Meters, Inches).
                string unit;
                try { unit = _s.SystemData.Units.LensUnits.ToString(); }
                catch (Exception ex) { throw new InvalidOperationException("Could not read the lens units from OpticStudio: " + ex.Message, ex); }
                switch (unit)
                {
                    case "Millimeters": return 1e-3;
                    case "Centimeters": return 1e-2;
                    case "Meters": return 1.0;
                    case "Inches": return 0.0254;
                    default: throw new NotSupportedException("Unknown OpticStudio lens unit '" + unit + "' (expected Millimeters, Centimeters, Meters or Inches).");
                }
            }
        }

        public double TemperatureC
        {
            // TODO(zemax-hw): the fallback hides a failed read (it only changes the air index slightly); report it once the interface has a warning channel
            get { try { return _s.SystemData.Environment.Temperature; } catch { return 20.0; } }
        }

        public double PressureAtm
        {
            get { try { return _s.SystemData.Environment.Pressure; } catch { return 1.0; } }
        }

        public int NumberOfSurfaces => _s.LDE.NumberOfSurfaces;

        public SurfaceRow GetSurface(int index)
        {
            var row = _s.LDE.GetSurfaceAt(index);
            var r = new SurfaceRow { Index = index };
            try { r.Material = row.Material ?? ""; } catch { }
            try { r.Comment = row.Comment ?? ""; } catch { }
            try { r.TypeName = row.Type.ToString(); } catch { r.TypeName = "(unknown)"; }
            try { r.Thickness = row.Thickness; } catch { }
            try { r.IsIgnored = row.TypeData.IgnoreSurface; } catch { }
            try { r.SemiDiameter = row.SemiDiameter; } catch { }
            try { r.Coating = row.Coating ?? ""; } catch { }
            return r;
        }

        public IReadOnlyList<(double Um, double Weight)> GetWavelengths(out int primary)
        {
            var wl = _s.SystemData.Wavelengths;
            var list = new List<(double, double)>();
            primary = 1;
            for (int i = 1; i <= wl.NumberOfWavelengths; i++)
            {
                var w = wl.GetWavelength(i);
                list.Add((w.Wavelength, w.Weight));
                try { if (w.IsPrimary) primary = i; } catch { }
            }
            return list;
        }

        public int SetWavelengths(IReadOnlyList<double> um)
        {
            var wl = _s.SystemData.Wavelengths;
            int target = um.Count;
            while (wl.NumberOfWavelengths < target)
            {
                int before = wl.NumberOfWavelengths;
                try { wl.AddWavelength(um[Math.Min(before, target - 1)], 1.0); } catch { break; }
                if (wl.NumberOfWavelengths == before) break; // OpticStudio's wavelength limit
            }
            while (wl.NumberOfWavelengths > target) wl.RemoveWavelength(wl.NumberOfWavelengths);
            int n = Math.Min(target, wl.NumberOfWavelengths);
            for (int i = 0; i < n; i++) { var w = wl.GetWavelength(i + 1); w.Wavelength = um[i]; w.Weight = 1.0; }
            try { wl.GetWavelength(1).MakePrimary(); } catch { }
            return n;
        }

        public void RestoreWavelengths(IReadOnlyList<(double Um, double Weight)> waves, int primary)
        {
            SetWavelengths(waves.Select(w => w.Um).ToList());
            var wl = _s.SystemData.Wavelengths;
            for (int i = 0; i < waves.Count && i < wl.NumberOfWavelengths; i++) wl.GetWavelength(i + 1).Weight = waves[i].Weight;
            try { if (primary <= wl.NumberOfWavelengths) wl.GetWavelength(primary).MakePrimary(); } catch { }
        }

        public double[] GlobalMatrix(int surface)
        {
            // TODO(zemax-hw): GetGlobalMatrix returns the rotation R (row-major r11..r33) and the surface origin (x0, y0, z0) in lens units: confirm on a tilted system.
            _s.LDE.GetGlobalMatrix(surface, out double r11, out double r12, out double r13, out double r21, out double r22, out double r23,
                out double r31, out double r32, out double r33, out double x0, out double y0, out double z0);
            return new[] { r11, r12, r13, r21, r22, r23, r31, r32, r33, x0, y0, z0 };
        }

        public void GetIndex(int surface, int numberOfWaves, double[] dest) => _s.LDE.GetIndex(surface, numberOfWaves, dest);
    }
}
