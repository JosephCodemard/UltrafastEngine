using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UltrafastEngine.Tests
{
    /// <summary>
    /// The validation matrix (docs/reference/validation-matrix.md), written from the test attributes: every test, its area,
    /// and -- for tests checked against something outside the engine -- the reference and the tolerance. Regenerate with
    /// <c>dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md</c>;
    /// <c>ValidationMatrixTests</c> fails while the committed file is out of date.
    /// </summary>
    public static class ValidationMatrix
    {
        private static bool IsInternal(string reference) => reference != null && reference.StartsWith("cross-", StringComparison.Ordinal);
        private static string Cell(string s) => string.IsNullOrEmpty(s) ? "--" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        public static string Markdown()
        {
            var tests = typeof(ValidationMatrix).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                .Select(m => (m, a: m.GetCustomAttribute<TestAttribute>()))
                .Where(x => x.a != null)
                .GroupBy(x => x.m.DeclaringType)
                .Select(g => (type: g.Key, area: g.Key.GetCustomAttribute<TestAreaAttribute>()?.Name ?? g.Key.Name, tests: g.OrderBy(x => x.m.Name, StringComparer.Ordinal).ToList()))
                .OrderBy(g => g.type.Name, StringComparer.Ordinal)
                .ToList();
            int total = tests.Sum(g => g.tests.Count), referenced = tests.Sum(g => g.tests.Count(x => !string.IsNullOrEmpty(x.a.Reference)));
            int internalChecks = tests.Sum(g => g.tests.Count(x => IsInternal(x.a.Reference))), external = referenced - internalChecks;
            var sb = new StringBuilder();
            sb.AppendLine("# Validation matrix");
            sb.AppendLine();
            sb.AppendLine("<!-- Generated from the test attributes: dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md -- do not edit. -->");
            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Every C# test of the engine: **{0} tests** in {1} classes. **{2}** of them check the engine against something outside it -- an analytic result, a published value or another code -- named in the *reference* column with the agreement required. **{3}** are cross-checks between two parts of the engine (a reference starting with \"cross-\"): they catch inconsistency, not a shared error. The others test behaviour and bookkeeping. The narrative overview is [Testing and validation](../testing.md); a test declares its reference with `[Test(Reference = \"...\", Tolerance = \"...\")]`.",
                total, tests.Count, external, internalChecks));
            sb.AppendLine();
            sb.AppendLine("| area | class | tests | with an external reference |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var g in tests)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "| [{0}](#{1}) | `{2}` | {3} | {4} |", Cell(g.area), g.type.Name.ToLowerInvariant(), g.type.Name, g.tests.Count, g.tests.Count(x => !string.IsNullOrEmpty(x.a.Reference))));
            foreach (var g in tests)
            {
                sb.AppendLine();
                sb.AppendLine("## " + g.type.Name);
                sb.AppendLine();
                sb.AppendLine(Cell(g.area) + ".");
                sb.AppendLine();
                sb.AppendLine("| test | reference | tolerance |");
                sb.AppendLine("|---|---|---|");
                foreach (var (m, a) in g.tests)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "| `{0}`{1} | {2} | {3} |", m.Name, a.Slow ? " (slow)" : "", Cell(a.Reference), Cell(a.Tolerance)));
            }
            return sb.ToString();
        }
    }

    [TestArea("The test framework and the generated validation matrix")]
    public class ValidationMatrixTests
    {
        [Test(Category = "schema")]
        public void ValidationMatrix_IsUpToDate()
        {
            var root = SchemaTests.RepoRoot();
            if (root == null) Assert.Skip("repository not found");
            string Norm(string t) => t.Replace("\r\n", "\n").Trim();
            Assert.Equal(Norm(ValidationMatrix.Markdown()), Norm(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "docs", "reference", "validation-matrix.md"))),
                "docs/reference/validation-matrix.md is current (UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md)");
        }

        [Test(Category = "schema")]
        public void EveryTestClass_NamesItsArea()
        {
            var missing = typeof(ValidationMatrix).Assembly.GetTypes()
                .Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<TestAttribute>() != null) && t.GetCustomAttribute<TestAreaAttribute>() == null)
                .Select(t => t.Name).ToList();
            Assert.True(missing.Count == 0, "test classes without [TestArea]: " + string.Join(", ", missing));
        }
    }
}
