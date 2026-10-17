using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltrafastEngine.Models
{
    /// <summary>
    /// A compiled, vectorised math expression, e.g. <c>"n2*I + 1e-4*sin(2*pi*t_lab/3e-15)"</c>.
    ///
    /// Used for the "expression" form of every overridable model (refractive index, time-domain response,
    /// aberrations, transmission curves...). Expressions are stored as text in the system file / run spec,
    /// so they travel with the data, and they are evaluated over whole arrays at once (one call per solver
    /// step), so they are fast enough for the inner loop. Implemented as a small parser + tree evaluator --
    /// no Reflection.Emit / LINQ-expression compilation, so it works identically under NativeAOT.
    ///
    /// Syntax: numbers (1, 2.5, 1e-3), variables, parameters, + - * / and ^ or ** (power, right-assoc),
    /// unary minus, comparisons (&lt; &lt;= &gt; &gt;= == !=, giving 1 or 0), &amp;&amp; ||, and functions:
    /// sin cos tan asin acos atan atan2 sinh cosh tanh exp log log10 sqrt abs sign floor ceil round
    /// pow min max hypot step(x) (Heaviside) if(cond, a, b) gauss(x, sigma) sech(x) clamp(x, lo, hi).
    /// Constants: pi, e, c (speed of light, m/s), eps0, mu0, h, hbar, kB, qe.
    /// </summary>
    public sealed class CompiledExpression
    {
        public string Text { get; }
        public IReadOnlyList<string> Variables { get; }
        private readonly Node _root;

        private CompiledExpression(string text, IReadOnlyList<string> variables, Node root)
        {
            Text = text;
            Variables = variables;
            _root = root;
        }

        /// <summary>Compile <paramref name="text"/>. Identifiers that are neither declared variables, parameters
        /// nor built-in constants are an error (caught at load time, not mid-run).</summary>
        public static CompiledExpression Compile(string text, IEnumerable<string> variables, IReadOnlyDictionary<string, double> parameters = null)
        {
            var vars = variables?.ToList() ?? new List<string>();
            var parser = new Parser(text, vars, parameters ?? new Dictionary<string, double>());
            var root = parser.ParseAll();
            return new CompiledExpression(text, vars, root);
        }

        /// <summary>Evaluate over <paramref name="n"/> points. <paramref name="bindings"/> maps each declared
        /// variable to either an array of length n or a 1-element array (broadcast scalar).</summary>
        public double[] Evaluate(IReadOnlyDictionary<string, double[]> bindings, int n)
        {
            var ctx = new Ctx(bindings, n, Variables);
            var v = _root.Eval(ctx);
            if (v.Length == n) return v;
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = v[0];
            return r;
        }

        /// <summary>Scalar convenience evaluation.</summary>
        public double EvaluateScalar(IReadOnlyDictionary<string, double> values)
        {
            var b = new Dictionary<string, double[]>();
            foreach (var kv in values) b[kv.Key] = new[] { kv.Value };
            return Evaluate(b, 1)[0];
        }

        public override string ToString() => Text;

        // ------------------------------------------------------------------ evaluation context
        private sealed class Ctx
        {
            public readonly int N;
            public readonly double[][] Vars;
            public Ctx(IReadOnlyDictionary<string, double[]> bindings, int n, IReadOnlyList<string> names)
            {
                N = n;
                Vars = new double[names.Count][];
                for (int i = 0; i < names.Count; i++)
                {
                    if (bindings == null || !bindings.TryGetValue(names[i], out var arr) || arr == null)
                        throw new InvalidOperationException("Expression variable '" + names[i] + "' was not supplied.");
                    if (arr.Length != n && arr.Length != 1)
                        throw new InvalidOperationException("Expression variable '" + names[i] + "' has length " + arr.Length + ", expected " + n + " or 1.");
                    Vars[i] = arr;
                }
            }
        }

        // ------------------------------------------------------------------ AST
        private abstract class Node
        {
            /// <summary>Returns an array of length N, or of length 1 for a scalar result (broadcast).</summary>
            public abstract double[] Eval(Ctx c);
        }

        private sealed class ConstNode : Node
        {
            private readonly double[] _v;
            public ConstNode(double v) { _v = new[] { v }; }
            public override double[] Eval(Ctx c) => _v;
            public double Value => _v[0];
        }

        private sealed class VarNode : Node
        {
            private readonly int _i;
            public VarNode(int i) { _i = i; }
            public override double[] Eval(Ctx c) => c.Vars[_i];
        }

        private sealed class MapNode : Node
        {
            private readonly Node[] _args;
            private readonly Func<double[], double> _f; // receives one value per argument
            public MapNode(Func<double[], double> f, params Node[] args) { _f = f; _args = args; }
            public override double[] Eval(Ctx c)
            {
                var vals = new double[_args.Length][];
                int len = 1;
                for (int k = 0; k < _args.Length; k++) { vals[k] = _args[k].Eval(c); if (vals[k].Length > 1) len = vals[k].Length; }
                var r = new double[len];
                var tmp = new double[_args.Length];
                for (int i = 0; i < len; i++)
                {
                    for (int k = 0; k < _args.Length; k++) tmp[k] = vals[k].Length == 1 ? vals[k][0] : vals[k][i];
                    r[i] = _f(tmp);
                }
                return r;
            }
        }

        // Fast paths for the binary arithmetic that dominates real expressions.
        private sealed class BinNode : Node
        {
            private readonly Node _a, _b; private readonly char _op;
            public BinNode(char op, Node a, Node b) { _op = op; _a = a; _b = b; }
            public override double[] Eval(Ctx c)
            {
                var x = _a.Eval(c); var y = _b.Eval(c);
                int len = Math.Max(x.Length, y.Length);
                var r = new double[len];
                bool xs = x.Length == 1, ys = y.Length == 1;
                switch (_op)
                {
                    case '+': for (int i = 0; i < len; i++) r[i] = (xs ? x[0] : x[i]) + (ys ? y[0] : y[i]); break;
                    case '-': for (int i = 0; i < len; i++) r[i] = (xs ? x[0] : x[i]) - (ys ? y[0] : y[i]); break;
                    case '*': for (int i = 0; i < len; i++) r[i] = (xs ? x[0] : x[i]) * (ys ? y[0] : y[i]); break;
                    case '/': for (int i = 0; i < len; i++) r[i] = (xs ? x[0] : x[i]) / (ys ? y[0] : y[i]); break;
                    case '^': for (int i = 0; i < len; i++) r[i] = Math.Pow(xs ? x[0] : x[i], ys ? y[0] : y[i]); break;
                    default: throw new InvalidOperationException("bad op");
                }
                return r;
            }
        }

        // ------------------------------------------------------------------ parser
        private sealed class Parser
        {
            private readonly string _s;
            private readonly List<string> _vars;
            private readonly IReadOnlyDictionary<string, double> _params;
            private int _p;

            private static readonly Dictionary<string, double> Constants = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                { "pi", Math.PI }, { "e", Math.E }, { "c", 299792458.0 }, { "eps0", 8.8541878128e-12 },
                { "mu0", 1.25663706212e-6 }, { "h", 6.62607015e-34 }, { "hbar", 1.054571817e-34 },
                { "kB", 1.380649e-23 }, { "qe", 1.602176634e-19 }
            };

            public Parser(string s, List<string> vars, IReadOnlyDictionary<string, double> parameters)
            {
                _s = s ?? throw new ArgumentNullException(nameof(s));
                _vars = vars;
                _params = parameters;
            }

            private Exception Error(string msg) => new FormatException("Expression error at position " + (_p + 1) + " in \"" + _s + "\": " + msg + ".");

            public Node ParseAll()
            {
                var n = ParseOr();
                SkipWs();
                if (_p < _s.Length) throw Error("unexpected '" + _s[_p] + "'");
                return n;
            }

            private void SkipWs() { while (_p < _s.Length && char.IsWhiteSpace(_s[_p])) _p++; }
            private bool Peek(string tok) { SkipWs(); return string.CompareOrdinal(_s, _p, tok, 0, tok.Length) == 0; }
            private bool Accept(string tok) { if (Peek(tok)) { _p += tok.Length; return true; } return false; }

            private Node ParseOr()
            {
                var a = ParseAnd();
                while (Accept("||")) { var b = ParseAnd(); a = new MapNode(v => (v[0] != 0 || v[1] != 0) ? 1 : 0, a, b); }
                return a;
            }

            private Node ParseAnd()
            {
                var a = ParseCompare();
                while (Accept("&&")) { var b = ParseCompare(); a = new MapNode(v => (v[0] != 0 && v[1] != 0) ? 1 : 0, a, b); }
                return a;
            }

            private Node ParseCompare()
            {
                var a = ParseAdd();
                while (true)
                {
                    if (Accept("<=")) { var b = ParseAdd(); a = new MapNode(v => v[0] <= v[1] ? 1 : 0, a, b); }
                    else if (Accept(">=")) { var b = ParseAdd(); a = new MapNode(v => v[0] >= v[1] ? 1 : 0, a, b); }
                    else if (Accept("==")) { var b = ParseAdd(); a = new MapNode(v => v[0] == v[1] ? 1 : 0, a, b); }
                    else if (Accept("!=")) { var b = ParseAdd(); a = new MapNode(v => v[0] != v[1] ? 1 : 0, a, b); }
                    else if (Accept("<")) { var b = ParseAdd(); a = new MapNode(v => v[0] < v[1] ? 1 : 0, a, b); }
                    else if (Accept(">")) { var b = ParseAdd(); a = new MapNode(v => v[0] > v[1] ? 1 : 0, a, b); }
                    else return a;
                }
            }

            private Node ParseAdd()
            {
                var a = ParseMul();
                while (true)
                {
                    if (Accept("+")) a = Fold(new BinNode('+', a, ParseMul()));
                    else if (Accept("-")) a = Fold(new BinNode('-', a, ParseMul()));
                    else return a;
                }
            }

            private Node ParseMul()
            {
                var a = ParseUnary();
                while (true)
                {
                    if (Peek("**")) return a; // handled in ParsePow
                    if (Accept("*")) a = Fold(new BinNode('*', a, ParseUnary()));
                    else if (Accept("/")) a = Fold(new BinNode('/', a, ParseUnary()));
                    else return a;
                }
            }

            private Node ParseUnary()
            {
                if (Accept("-")) return Fold(new BinNode('-', new ConstNode(0), ParseUnary()));
                if (Accept("+")) return ParseUnary();
                if (Accept("!")) { var x = ParseUnary(); return new MapNode(v => v[0] == 0 ? 1 : 0, x); }
                return ParsePow();
            }

            private Node ParsePow()
            {
                var a = ParsePrimary();
                if (Accept("^") || Accept("**")) return Fold(new BinNode('^', a, ParseUnary())); // right-assoc
                return a;
            }

            private static Node Fold(Node n)
            {
                // Constant folding keeps parameter-only sub-expressions out of the inner loop.
                try
                {
                    var ctx = new Ctx(new Dictionary<string, double[]>(), 1, new string[0]);
                    var v = n.Eval(ctx);
                    if (v.Length == 1) return new ConstNode(v[0]);
                }
                catch (InvalidOperationException) { }
                catch (IndexOutOfRangeException) { }
                return n;
            }

            private Node ParsePrimary()
            {
                SkipWs();
                if (_p >= _s.Length) throw Error("unexpected end");
                char ch = _s[_p];
                if (ch == '(')
                {
                    _p++;
                    var inner = ParseOr();
                    if (!Accept(")")) throw Error("missing ')'");
                    return inner;
                }
                if (char.IsDigit(ch) || ch == '.')
                {
                    int start = _p;
                    while (_p < _s.Length && (char.IsDigit(_s[_p]) || _s[_p] == '.')) _p++;
                    if (_p < _s.Length && (_s[_p] == 'e' || _s[_p] == 'E'))
                    {
                        int save = _p;
                        _p++;
                        if (_p < _s.Length && (_s[_p] == '+' || _s[_p] == '-')) _p++;
                        if (_p < _s.Length && char.IsDigit(_s[_p])) { while (_p < _s.Length && char.IsDigit(_s[_p])) _p++; }
                        else _p = save;
                    }
                    string tok = _s.Substring(start, _p - start);
                    if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw Error("bad number '" + tok + "'");
                    return new ConstNode(d);
                }
                if (char.IsLetter(ch) || ch == '_')
                {
                    int start = _p;
                    while (_p < _s.Length && (char.IsLetterOrDigit(_s[_p]) || _s[_p] == '_')) _p++;
                    string id = _s.Substring(start, _p - start);
                    if (Accept("("))
                    {
                        var args = new List<Node>();
                        if (!Accept(")"))
                        {
                            do { args.Add(ParseOr()); } while (Accept(","));
                            if (!Accept(")")) throw Error("missing ')' after arguments of " + id);
                        }
                        return Fold(MakeCall(id, args));
                    }
                    int vi = _vars.IndexOf(id);
                    if (vi >= 0) return new VarNode(vi);
                    if (_params.TryGetValue(id, out double pv)) return new ConstNode(pv);
                    if (Constants.TryGetValue(id, out double cv)) return new ConstNode(cv);
                    throw Error("unknown name '" + id + "' (declared variables: " + (_vars.Count == 0 ? "none" : string.Join(", ", _vars)) + ")");
                }
                throw Error("unexpected '" + ch + "'");
            }

            private Node MakeCall(string name, List<Node> a)
            {
                void Arity(int k) { if (a.Count != k) throw Error(name + "() takes " + k + " argument(s), got " + a.Count); }
                switch (name)
                {
                    case "sin": Arity(1); return new MapNode(v => Math.Sin(v[0]), a[0]);
                    case "cos": Arity(1); return new MapNode(v => Math.Cos(v[0]), a[0]);
                    case "tan": Arity(1); return new MapNode(v => Math.Tan(v[0]), a[0]);
                    case "asin": Arity(1); return new MapNode(v => Math.Asin(v[0]), a[0]);
                    case "acos": Arity(1); return new MapNode(v => Math.Acos(v[0]), a[0]);
                    case "atan": Arity(1); return new MapNode(v => Math.Atan(v[0]), a[0]);
                    case "atan2": Arity(2); return new MapNode(v => Math.Atan2(v[0], v[1]), a[0], a[1]);
                    case "sinh": Arity(1); return new MapNode(v => Math.Sinh(v[0]), a[0]);
                    case "cosh": Arity(1); return new MapNode(v => Math.Cosh(v[0]), a[0]);
                    case "tanh": Arity(1); return new MapNode(v => Math.Tanh(v[0]), a[0]);
                    case "sech": Arity(1); return new MapNode(v => 1.0 / Math.Cosh(v[0]), a[0]);
                    case "exp": Arity(1); return new MapNode(v => Math.Exp(v[0]), a[0]);
                    case "log": Arity(1); return new MapNode(v => Math.Log(v[0]), a[0]);
                    case "log10": Arity(1); return new MapNode(v => Math.Log10(v[0]), a[0]);
                    case "sqrt": Arity(1); return new MapNode(v => Math.Sqrt(v[0]), a[0]);
                    case "abs": Arity(1); return new MapNode(v => Math.Abs(v[0]), a[0]);
                    case "sign": Arity(1); return new MapNode(v => Math.Sign(v[0]), a[0]);
                    case "floor": Arity(1); return new MapNode(v => Math.Floor(v[0]), a[0]);
                    case "ceil": Arity(1); return new MapNode(v => Math.Ceiling(v[0]), a[0]);
                    case "round": Arity(1); return new MapNode(v => Math.Round(v[0]), a[0]);
                    case "step": Arity(1); return new MapNode(v => v[0] >= 0 ? 1 : 0, a[0]);
                    case "pow": Arity(2); return new BinNode('^', a[0], a[1]);
                    case "hypot": Arity(2); return new MapNode(v => Math.Sqrt(v[0] * v[0] + v[1] * v[1]), a[0], a[1]);
                    case "min": if (a.Count < 2) throw Error("min() needs at least 2 arguments"); return new MapNode(v => v.Min(), a.ToArray());
                    case "max": if (a.Count < 2) throw Error("max() needs at least 2 arguments"); return new MapNode(v => v.Max(), a.ToArray());
                    case "if": Arity(3); return new MapNode(v => v[0] != 0 ? v[1] : v[2], a[0], a[1], a[2]);
                    case "clamp": Arity(3); return new MapNode(v => Math.Max(v[1], Math.Min(v[2], v[0])), a[0], a[1], a[2]);
                    case "gauss": Arity(2); return new MapNode(v => Math.Exp(-0.5 * v[0] * v[0] / (v[1] * v[1])), a[0], a[1]);
                    default: throw Error("unknown function '" + name + "'");
                }
            }
        }
    }
}
