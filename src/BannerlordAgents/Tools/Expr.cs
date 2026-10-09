using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BannerlordAgents.Tools
{
    /// <summary>
    /// What an expression of the behavior language can ask of the world and of its tool: names (settings, the order's
    /// targets, the tool's memory, built-in values such as strength) and functions (distance, besieged, ...).
    /// </summary>
    internal interface IScope
    {
        object Get(string name);
        object Call(string fn, object[] args);
    }

    /// <summary>
    /// An expression of the behavior language (docs/TOOLS.md): numbers, true/false/none, names, function calls,
    /// + - * /, comparisons (== != &lt; &lt;= &gt; &gt;=), and/or/not, and brackets. Values are numbers (double), true or
    /// false (bool), ids of parties and holdings (string), or none (null). Game-free, so tools can be checked outside
    /// the game.
    /// </summary>
    internal abstract class Expr
    {
        public abstract object Eval(IScope s);

        /// <summary>Every name and every call in the expression, for the checker.</summary>
        public abstract void Walk(Action<string> name, Action<string, int> call);

        public static Expr Parse(string text) => new Parser(text ?? "").ParseAll();

        // ---------- values ----------

        public static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool b) return b;
            if (v is double d) return !double.IsNaN(d) && d != 0;
            if (v is string s) return s.Length > 0;
            return true;
        }

        public static double Number(object v)
        {
            if (v is double d) return d;
            if (v is bool b) return b ? 1 : 0;
            if (v is int i) return i;
            if (v is float f) return f;
            return double.NaN;
        }

        public static string Show(object v)
        {
            if (v == null) return "none";
            if (v is bool b) return b ? "true" : "false";
            if (v is double d) return Math.Abs(d - Math.Round(d)) < 1e-9 ? Math.Round(d).ToString("0", CultureInfo.InvariantCulture) : d.ToString("0.##", CultureInfo.InvariantCulture);
            return v.ToString();
        }

        // ---------- the tree ----------

        private sealed class Lit : Expr
        {
            public object V;
            public override object Eval(IScope s) => V;
            public override void Walk(Action<string> name, Action<string, int> call) { }
        }

        private sealed class Name : Expr
        {
            public string Id;
            public override object Eval(IScope s) => s.Get(Id);
            public override void Walk(Action<string> name, Action<string, int> call) => name(Id);
        }

        private sealed class Call : Expr
        {
            public string Fn;
            public List<Expr> Args = new List<Expr>();
            public override object Eval(IScope s)
            {
                var a = new object[Args.Count];
                for (int i = 0; i < a.Length; i++) a[i] = Args[i].Eval(s);
                return s.Call(Fn, a);
            }
            public override void Walk(Action<string> name, Action<string, int> call)
            {
                call(Fn, Args.Count);
                foreach (Expr e in Args) e.Walk(name, call);
            }
        }

        private sealed class Unary : Expr
        {
            public string Op;
            public Expr E;
            public override object Eval(IScope s)
            {
                object v = E.Eval(s);
                if (Op == "not") return !Truthy(v);
                double d = Number(v);
                return double.IsNaN(d) ? (object)null : -d;
            }
            public override void Walk(Action<string> name, Action<string, int> call) => E.Walk(name, call);
        }

        private sealed class Binary : Expr
        {
            public string Op;
            public Expr L, R;

            public override object Eval(IScope s)
            {
                switch (Op)
                {
                    case "and": return Truthy(L.Eval(s)) && Truthy(R.Eval(s));
                    case "or": return Truthy(L.Eval(s)) || Truthy(R.Eval(s));
                }
                object a = L.Eval(s), b = R.Eval(s);
                switch (Op)
                {
                    case "==": return Same(a, b);
                    case "!=": return !Same(a, b);
                }
                double x = Number(a), y = Number(b);
                bool known = !double.IsNaN(x) && !double.IsNaN(y);
                switch (Op)
                {
                    // a comparison with something unknown (none) is false, so a rule never fires on missing facts
                    case "<": return known && x < y;
                    case "<=": return known && x <= y;
                    case ">": return known && x > y;
                    case ">=": return known && x >= y;
                    case "+": return known ? x + y : (object)null;
                    case "-": return known ? x - y : (object)null;
                    case "*": return known ? x * y : (object)null;
                    case "/": return known && y != 0 ? x / y : (object)null;
                }
                throw new InvalidOperationException("unknown operator " + Op);
            }

            private static bool Same(object a, object b)
            {
                if (a == null || b == null) return a == null && b == null;
                if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
                double x = Number(a), y = Number(b);
                if (!double.IsNaN(x) && !double.IsNaN(y)) return Math.Abs(x - y) < 1e-9;
                return Equals(a, b);
            }

            public override void Walk(Action<string> name, Action<string, int> call)
            {
                L.Walk(name, call);
                R.Walk(name, call);
            }
        }

        // ---------- the parser ----------

        private sealed class Parser
        {
            private readonly List<string> _t = new List<string>();
            private int _i;
            private readonly string _text;

            public Parser(string text)
            {
                _text = text;
                int i = 0;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                    {
                        int j = i;
                        while (j < text.Length && (char.IsDigit(text[j]) || text[j] == '.')) j++;
                        _t.Add(text.Substring(i, j - i));
                        i = j;
                        continue;
                    }
                    if (char.IsLetter(c) || c == '_')
                    {
                        int j = i;
                        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                        _t.Add(text.Substring(i, j - i));
                        i = j;
                        continue;
                    }
                    if (i + 1 < text.Length)
                    {
                        string two = text.Substring(i, 2);
                        if (two == "<=" || two == ">=" || two == "==" || two == "!=") { _t.Add(two); i += 2; continue; }
                    }
                    if ("()+-*/<>,".IndexOf(c) >= 0) { _t.Add(c.ToString()); i++; continue; }
                    throw new FormatException("unexpected '" + c + "' in \"" + text + "\"");
                }
            }

            private string Peek => _i < _t.Count ? _t[_i] : null;
            private string Next() => _i < _t.Count ? _t[_i++] : null;

            private void Expect(string tok)
            {
                if (Next() != tok) throw new FormatException("expected '" + tok + "' in \"" + _text + "\"");
            }

            public Expr ParseAll()
            {
                if (_t.Count == 0) throw new FormatException("an empty expression");
                Expr e = Or();
                if (_i != _t.Count) throw new FormatException("unexpected '" + Peek + "' in \"" + _text + "\"");
                return e;
            }

            private Expr Or()
            {
                Expr e = And();
                while (Peek == "or") { Next(); e = new Binary { Op = "or", L = e, R = And() }; }
                return e;
            }

            private Expr And()
            {
                Expr e = Not();
                while (Peek == "and") { Next(); e = new Binary { Op = "and", L = e, R = Not() }; }
                return e;
            }

            private Expr Not()
            {
                if (Peek == "not") { Next(); return new Unary { Op = "not", E = Not() }; }
                return Compare();
            }

            private Expr Compare()
            {
                Expr e = Sum();
                string op = Peek;
                if (op == "==" || op == "!=" || op == "<" || op == "<=" || op == ">" || op == ">=")
                {
                    Next();
                    e = new Binary { Op = op, L = e, R = Sum() };
                }
                return e;
            }

            private Expr Sum()
            {
                Expr e = Product();
                while (Peek == "+" || Peek == "-") { string op = Next(); e = new Binary { Op = op, L = e, R = Product() }; }
                return e;
            }

            private Expr Product()
            {
                Expr e = Sign();
                while (Peek == "*" || Peek == "/") { string op = Next(); e = new Binary { Op = op, L = e, R = Sign() }; }
                return e;
            }

            private Expr Sign()
            {
                if (Peek == "-") { Next(); return new Unary { Op = "-", E = Sign() }; }
                return Atom();
            }

            private Expr Atom()
            {
                string t = Next();
                if (t == null) throw new FormatException("the expression \"" + _text + "\" ends too soon");
                if (t == "(")
                {
                    Expr e = Or();
                    Expect(")");
                    return e;
                }
                if (char.IsDigit(t[0]) || t[0] == '.')
                {
                    if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw new FormatException("bad number '" + t + "'");
                    return new Lit { V = d };
                }
                if (t == "true") return new Lit { V = true };
                if (t == "false") return new Lit { V = false };
                if (t == "none") return new Lit { V = null };
                if (t == "and" || t == "or" || t == "not") throw new FormatException("'" + t + "' needs something on its side in \"" + _text + "\"");
                if (!(char.IsLetter(t[0]) || t[0] == '_')) throw new FormatException("unexpected '" + t + "' in \"" + _text + "\"");
                if (Peek == "(")
                {
                    Next();
                    var c = new Call { Fn = t };
                    if (Peek != ")")
                    {
                        c.Args.Add(Or());
                        while (Peek == ",") { Next(); c.Args.Add(Or()); }
                    }
                    Expect(")");
                    return c;
                }
                return new Name { Id = t };
            }
        }
    }

    /// <summary>Fills "{name}" and "{expression}" in a call-back's text with values, ids shown as names.</summary>
    internal static class Template
    {
        public static string Fill(string text, IScope s, Func<object, string> show)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            var sb = new StringBuilder(text.Length + 32);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) { sb.Append(text, i, text.Length - i); break; }
                int close = text.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                string inner = text.Substring(open + 1, close - open - 1);
                string value;
                try { value = show(Expr.Parse(inner).Eval(s)); }
                catch (Exception) { value = "{" + inner + "}"; }
                sb.Append(value);
                i = close + 1;
            }
            return sb.ToString();
        }
    }
}
