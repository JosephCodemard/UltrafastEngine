// CatalogueModels.cs -- turns catalogue quantities into engine objects.
//
// What it does: builds IRefractiveIndex objects (constant, sellmeier, vendorSellmeier, cauchy) and delayed (Raman) responses from the
// numbers of a CatalogueEntry, and holds the defaults of the named Raman models ("silica" / "blowWood", "linAgrawal", "hollenbeck",
// "orientational") that the model registry uses when a run spec gives only the kind.
// The arithmetic is exactly what the old C# tables did (squared resonance wavelengths, A - S / C for the vendor form), so the move of the
// numbers into files changed no result (CatalogueSnapshotTests).
//
//   CatalogueModels.Index(q)               -> IRefractiveIndex (relative to the medium named by q's "referenceMedium")
//   CatalogueModels.IsAirReferenced(q)     -> true when the data are relative to air
//   CatalogueModels.Raman(entry)           -> the delayed response of a "raman" entry (with its anisotropic split)
//   CatalogueModels.RamanFractionOf(entry) -> its default f_R (NaN when none)
using System;
using System.Linq;
using UltrafastEngine.Models;

namespace UltrafastEngine.Materials
{
    public static class CatalogueModels
    {
        public static bool IsAirReferenced(CatalogueQuantity q) => string.Equals(q.Text("referenceMedium", "vacuum"), "air", StringComparison.OrdinalIgnoreCase);

        /// <summary>The (relative) index model of an "index" quantity of a solid.</summary>
        public static IRefractiveIndex Index(CatalogueQuantity q)
        {
            string model = q.Text("model", "");
            switch (model)
            {
                case "constant":
                    return new ConstantIndex(q.Number("value"));
                case "sellmeier":
                {
                    var b = q.Array("b");
                    double[] c = q.Has("poleUm2") ? q.Array("poleUm2") : q.Array("resonanceWavelengthsUm").Select(x => x * x).ToArray();
                    return new SellmeierIndex(b, c, q.Number("a", 1.0), q.Text("name"));
                }
                case "vendorSellmeier":
                {
                    // n^2 = a + s / (l^2 - c) - d l^2, written in the form of SellmeierIrIndex
                    double a = q.Number("a"), s = q.Number("numeratorUm2"), c = q.Number("poleUm2"), d = q.Number("irTermPerUm2");
                    return new SellmeierIrIndex(a - s / c, new[] { s / c }, new[] { c }, d, q.Text("name"));
                }
                case "cauchy":
                    return new CauchyIndex(q.Array("coefficients"));
                default:
                    throw new ModelException("catalogue " + q.Path + ": unknown index model \"" + model + "\" (constant, sellmeier, vendorSellmeier, cauchy).");
            }
        }

        // ------------------------------------------------------------------------------------------------ Raman

        /// <summary>The anisotropic share to apply to a response: the entry's "anisotropicFraction" quantity, or 0.</summary>
        private static double Aniso(CatalogueEntry e) => e.TryQ("anisotropicFraction")?.Number("value") ?? 0.0;

        public static IDelayedResponse Raman(CatalogueEntry e)
        {
            var r = e.Q("response");
            switch (r.Text("model", ""))
            {
                case "dampedOscillator": return Split(new DampedOscillatorResponse(r.Number("tau1S"), r.Number("tau2S")), Aniso(e));
                case "hollenbeckCantrell": return Split(new HollenbeckCantrellRamanResponse(), Aniso(e));
                case "linAgrawal": return new LinAgrawalRamanResponse();
                case "orientational": return new OrientationalResponse(r.Number("riseS"), r.Number("decayS"), r.Number("anisotropicFraction", 1.5));
                default: throw new ModelException("catalogue " + r.Path + ": unknown Raman model \"" + r.Text("model", "") + "\".");
            }
        }

        private static IDelayedResponse Split(IDelayedResponse h, double b) => b != 0 ? new AnisotropicSplitResponse(h, b) : h;

        public static double RamanFractionOf(CatalogueEntry e) => e.TryQ("ramanFraction")?.Number("value") ?? double.NaN;

        /// <summary>Catalogue entry that holds the defaults of a registry kind ("silica" and "blowWood" are the same model).</summary>
        public static CatalogueEntry RamanEntryForKind(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "silica": case "blowwood": return Catalogue.Get("blowWood", "raman");
                case "linagrawal": return Catalogue.Get("linAgrawal", "raman");
                case "hollenbeck": return Catalogue.Get("hollenbeck", "raman");
                case "orientational": return Catalogue.Get("orientational", "raman");
                default: return null;
            }
        }

        /// <summary>The defaults of the model registry's Raman kinds, from the catalogue (tau1, tau2, anisotropic fraction, ...).</summary>
        public static class RamanDefaults
        {
            public static double BlowWoodTau1S => Catalogue.Get("blowWood", "raman").Q("response").Number("tau1S");
            public static double BlowWoodTau2S => Catalogue.Get("blowWood", "raman").Q("response").Number("tau2S");
            public static double BlowWoodAnisotropicFraction => Aniso(Catalogue.Get("blowWood", "raman"));
            public static double HollenbeckAnisotropicFraction => Aniso(Catalogue.Get("hollenbeck", "raman"));
            public static double OrientationalRiseS => Catalogue.Get("orientational", "raman").Q("response").Number("riseS");
            public static double OrientationalDecayS => Catalogue.Get("orientational", "raman").Q("response").Number("decayS");
            public static double OrientationalAnisotropicFraction => Catalogue.Get("orientational", "raman").Q("response").Number("anisotropicFraction");
        }
    }
}
