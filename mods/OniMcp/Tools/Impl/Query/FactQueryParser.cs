using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    // A bounded SELECT grammar, never an SQL/C# execution bridge.
    internal sealed class FactQueryParser
    {
        private static readonly Regex Lex = new Regex(@"\G\s*(?:('(?:[^']|'')*')|([A-Za-z_][A-Za-z_0-9]*)|(-?\d+(?:\.\d+)?)|(<=|>=|!=|<>|[=<>(),*;]))", RegexOptions.CultureInvariant);
        private readonly List<string> tokens = new List<string>();
        private int index, depth;
        private readonly Func<string, FactPredicate> area;
        private FactQueryParser(string sql, Func<string, FactPredicate> area)
        {
            if (string.IsNullOrWhiteSpace(sql) || sql.Length > 8192) throw new ArgumentException("query must contain 1..8192 characters");
            this.area = area;
            int offset = 0;
            while (offset < sql.Length)
            {
                if (string.IsNullOrWhiteSpace(sql.Substring(offset))) break;
                var match = Lex.Match(sql, offset);
                if (!match.Success) throw new ArgumentException("Invalid query token at character " + offset);
                tokens.Add(match.Value.Trim()); offset = match.Index + match.Length;
                if (tokens.Count > 512) throw new ArgumentException("Query exceeds 512 tokens");
            }
        }
        internal static FactQueryPlan Parse(string sql, Func<string, FactPredicate> area = null) => new FactQueryParser(sql, area).Read();
        private FactQueryPlan Read()
        {
            var plan = new FactQueryPlan();
            Need("SELECT");
            do
            {
                string name = Identifier();
                var projection = new FactProjection { Field = name };
                if (Take("("))
                {
                    string aggregate = name.ToUpperInvariant();
                    if (aggregate != "COUNT" && aggregate != "SUM") throw new ArgumentException("Only COUNT and SUM aggregates are supported");
                    projection.Aggregate = aggregate;
                    projection.Field = Take("*") ? "*" : Identifier();
                    if (projection.Field == "*" && aggregate != "COUNT") throw new ArgumentException("Only COUNT accepts *");
                    Need(")");
                }
                if (Take("AS")) projection.Alias = Identifier();
                plan.Select.Add(projection);
                if (plan.Select.Count > 24) throw new ArgumentException("Select at most 24 columns");
            } while (Take(","));
            Need("FROM"); plan.Dataset = Identifier().ToLowerInvariant();
            if (Take("WHERE")) plan.Where = Or();
            if (Take("GROUP")) { Need("BY"); plan.GroupBy = Identifier(); }
            if (Take("ORDER")) { Need("BY"); plan.OrderBy = Identifier(); plan.Descending = Take("DESC"); if (!plan.Descending) Take("ASC"); }
            if (Take("LIMIT")) plan.Limit = Integer(1, 200);
            if (Take("OFFSET")) plan.Offset = Integer(0, 50000);
            Take(";");
            if (index != tokens.Count) throw new ArgumentException("Unexpected query token: " + tokens[index]);
            return plan;
        }
        private FactPredicate Or()
        {
            var left = And();
            while (Take("OR")) left = new FactPredicate { Operator = "OR", Left = left, Right = And() };
            return left;
        }
        private FactPredicate And()
        {
            var left = Predicate();
            while (Take("AND")) left = new FactPredicate { Operator = "AND", Left = left, Right = Predicate() };
            return left;
        }
        private FactPredicate Predicate()
        {
            if (++depth > 16) throw new ArgumentException("Predicate nesting exceeds 16");
            try
            {
                if (Take("(")) { var nested = Or(); Need(")"); return nested; }
                string field = Identifier();
                if (Take("("))
                {
                    FactPredicate result;
                    switch (field.ToLowerInvariant())
                    {
                        case "has_status": result = new FactPredicate { Field = "statuses", Operator = "has_status", Value = StringLiteral() }; break;
                        case "in_area":
                            string id = StringLiteral().Value<string>();
                            if (area == null) throw new ArgumentException("in_area is unavailable");
                            result = area(id); break;
                        case "near":
                            double x = Number(); Need(","); double y = Number(); Need(","); double radius = Number();
                            if (radius < 0 || radius > 500) throw new ArgumentException("near radius must be 0..500 cells");
                            result = new FactPredicate { Operator = "near", X = x, Y = y, Radius = radius }; break;
                        default: throw new ArgumentException("Unknown predicate: " + field);
                    }
                    Need(")"); return result;
                }
                var predicate = new FactPredicate { Field = field };
                if (Take("IS")) { predicate.Operator = Take("NOT") ? "IS NOT NULL" : "IS NULL"; Need("NULL"); return predicate; }
                string op = Next().ToUpperInvariant();
                if (op != "=" && op != "!=" && op != "<>" && op != "<" && op != ">" && op != "<=" && op != ">=" && op != "CONTAINS")
                    throw new ArgumentException("Unsupported comparison: " + op);
                predicate.Operator = op;
                if (Peek().StartsWith("'", StringComparison.Ordinal)) predicate.Value = StringLiteral();
                else if (Take("TRUE")) predicate.Value = new JValue(true);
                else if (Take("FALSE")) predicate.Value = new JValue(false);
                else predicate.Value = new JValue(Number());
                return predicate;
            }
            finally { depth--; }
        }
        private string Peek() => index < tokens.Count ? tokens[index] : "";
        private string Next() { if (index >= tokens.Count) throw new ArgumentException("Unexpected end of query"); return tokens[index++]; }
        private bool Take(string token) { if (!string.Equals(Peek(), token, StringComparison.OrdinalIgnoreCase)) return false; index++; return true; }
        private void Need(string token) { if (!Take(token)) throw new ArgumentException("Expected " + token + ", got " + Peek()); }
        private string Identifier()
        {
            string token = Next();
            if (!Regex.IsMatch(token, @"^[A-Za-z_][A-Za-z_0-9]*$")) throw new ArgumentException("Expected field/dataset name, got " + token);
            return token;
        }
        private JValue StringLiteral()
        {
            string token = Next();
            if (!token.StartsWith("'", StringComparison.Ordinal)) throw new ArgumentException("Expected single-quoted string");
            return new JValue(token.Substring(1, token.Length - 2).Replace("''", "'"));
        }
        private double Number()
        {
            if (!double.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsInfinity(number) || double.IsNaN(number))
                throw new ArgumentException("Expected a finite numeric literal");
            return number;
        }
        private int Integer(int min, int max)
        {
            double value = Number();
            if (value < min || value > max || value != Math.Floor(value)) throw new ArgumentException("Integer must be " + min + ".." + max);
            return (int)value;
        }
    }
}
