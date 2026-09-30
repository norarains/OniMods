using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal sealed class FactField
    {
        internal readonly string Name, Type, Unit, Cost;
        internal FactField(string name, string type, string unit = null, string cost = "cheap")
        { Name = name; Type = type; Unit = unit; Cost = cost; }
    }

    internal sealed class FactRow
    {
        private readonly Func<string, object> read;
        private readonly Dictionary<string, JToken> cache = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
        internal FactRow(Func<string, object> read) { this.read = read; }
        internal JToken Get(string field)
        {
            if (!cache.TryGetValue(field, out var value))
            {
                object raw = read(field);
                value = raw == null ? JValue.CreateNull() : raw as JToken ?? JToken.FromObject(raw);
                cache[field] = value;
            }
            return value;
        }
    }

    internal sealed class FactDataset
    {
        internal readonly string Name;
        internal readonly Dictionary<string, FactField> Fields;
        internal readonly Func<IEnumerable<FactRow>> Rows;
        internal Func<FactRow, bool> Required;
        internal Func<FactPredicate, IEnumerable<FactRow>> IndexedRows;
        internal FactDataset(string name, IEnumerable<FactField> fields, Func<IEnumerable<FactRow>> rows)
        { Name = name; Fields = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase); Rows = rows; }
        internal FactField Field(string name)
        {
            if (!Fields.TryGetValue(name, out var field)) throw new ArgumentException("Unknown field '" + name + "' in " + Name + "; request its schema.");
            return field;
        }
    }

    internal sealed class FactProjection
    {
        internal string Field, Aggregate, Alias;
        internal string Label => Alias ?? (Aggregate == null ? Field : Aggregate.ToLowerInvariant() + "(" + Field + ")");
    }

    internal sealed class FactPredicate
    {
        internal string Field, Operator;
        internal JToken Value;
        internal FactPredicate Left, Right;
        internal double X, Y, Radius;
        private bool containsMembers;
        internal void Validate(FactDataset dataset)
        {
            if (Left != null) { Left.Validate(dataset); Right.Validate(dataset); return; }
            if (Operator == "near")
            { dataset.Field("x"); dataset.Field("y"); return; }
            var field = dataset.Field(Field);
            Field = field.Name;
            if (Operator == "has_status")
            {
                if (field.Type != "array" && field.Type != "string[]") throw new ArgumentException("has_status requires native statuses");
                if (dataset.Fields.TryGetValue("statusIds", out var ids) && ids.Type == "string[]") Field = ids.Name;
                return;
            }
            if (Operator == "IS NULL" || Operator == "IS NOT NULL") return;
            if (Operator == "CONTAINS")
            {
                if (field.Type == "array" && field.Name == "statuses")
                    throw new ArgumentException("statuses contains native objects; filter with statusIds CONTAINS 'Flooded' or has_status('Flooded')");
                if (field.Type != "string" && field.Type != "string[]")
                    throw new ArgumentException("CONTAINS requires a string or string-array field");
                containsMembers = field.Type == "string[]";
            }
            bool number = Value.Type == JTokenType.Integer || Value.Type == JTokenType.Float;
            bool compatible = field.Type == "number" ? number : field.Type == "string" || field.Type == "string[]" && Operator == "CONTAINS" ? Value.Type == JTokenType.String
                : field.Type == "boolean" && Value.Type == JTokenType.Boolean;
            if (!compatible) throw new ArgumentException("Literal type does not match " + Field + " (" + field.Type + ")");
            if (field.Type == "boolean" && Operator != "=" && Operator != "!=" && Operator != "<>")
                throw new ArgumentException("Boolean fields support = and != only");
        }
        internal bool Matches(FactRow row)
        {
            if (Operator == "AND") return Left.Matches(row) && Right.Matches(row);
            if (Operator == "OR") return Left.Matches(row) || Right.Matches(row);
            if (Operator == "near")
            {
                var x = row.Get("x"); var y = row.Get("y");
                return !IsNull(x) && !IsNull(y) && Math.Pow(x.Value<double>() - X, 2) + Math.Pow(y.Value<double>() - Y, 2) <= Radius * Radius;
            }
            var actual = row.Get(Field);
            if (Operator == "IS NULL") return IsNull(actual);
            if (Operator == "IS NOT NULL") return !IsNull(actual);
            if (IsNull(actual)) return false;
            if (Operator == "has_status") return actual is JArray statuses && statuses.Any(s =>
                s.Type == JTokenType.String ? string.Equals(s.Value<string>(), Value.Value<string>(), StringComparison.Ordinal)
                : s is JObject status && status["id"]?.ToString() == Value.ToString());
            if (Operator == "CONTAINS") return containsMembers
                ? actual is JArray array && array.Any(v => v.Type == JTokenType.String
                    && string.Equals(v.Value<string>(), Value.Value<string>(), StringComparison.OrdinalIgnoreCase))
                : actual.Type == JTokenType.String && actual.Value<string>().IndexOf(Value.Value<string>(), StringComparison.OrdinalIgnoreCase) >= 0;
            int comparison = Compare(actual, Value);
            switch (Operator)
            {
                case "=": return comparison == 0;
                case "!=": case "<>": return comparison != 0;
                case "<": return comparison < 0;
                case ">": return comparison > 0;
                case "<=": return comparison <= 0;
                case ">=": return comparison >= 0;
                default: throw new ArgumentException("Unsupported predicate: " + Operator);
            }
        }
        internal int Cost(FactDataset dataset) => Left != null ? Left.Cost(dataset) + Right.Cost(dataset)
            : Operator == "near" || dataset.Field(Field).Cost == "cheap" ? 0 : 1;
        internal void OrderCheapFirst(FactDataset dataset)
        {
            if (Left == null) return;
            Left.OrderCheapFirst(dataset); Right.OrderCheapFirst(dataset);
            if (Operator == "AND" && Left.Cost(dataset) > Right.Cost(dataset))
            { var swap = Left; Left = Right; Right = swap; }
        }
        internal static bool IsNull(JToken value) => value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined;
        internal static int Compare(JToken a, JToken b)
        {
            if (IsNull(a)) return IsNull(b) ? 0 : 1; // Unknown values sort last ascending.
            if (IsNull(b)) return -1;
            if ((a.Type == JTokenType.Integer || a.Type == JTokenType.Float) && (b.Type == JTokenType.Integer || b.Type == JTokenType.Float))
                return a.Value<double>().CompareTo(b.Value<double>());
            return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }
    }

    internal sealed class FactQueryPlan
    {
        internal string Dataset, OrderBy, GroupBy;
        internal bool Descending;
        internal int Limit = 20, Offset;
        internal FactPredicate Where;
        internal readonly List<FactProjection> Select = new List<FactProjection>();
        internal bool Aggregates => Select.Any(p => p.Aggregate != null);
        internal void Validate(FactDataset dataset)
        {
            if (Select.Select(p => p.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Select.Count)
                throw new ArgumentException("Duplicate output column; use AS aliases");
            foreach (var p in Select)
            {
                if (p.Field != "*") p.Field = dataset.Field(p.Field).Name;
                if (p.Aggregate == "SUM" && dataset.Field(p.Field).Type != "number") throw new ArgumentException("SUM requires a number field");
                if (Aggregates && p.Aggregate == null && !string.Equals(p.Field, GroupBy, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Non-aggregate fields must match GROUP BY");
            }
            if (GroupBy != null)
            {
                GroupBy = dataset.Field(GroupBy).Name;
                if (!Aggregates) throw new ArgumentException("GROUP BY requires COUNT or SUM");
                if (dataset.Field(GroupBy).Type == "array" || dataset.Field(GroupBy).Type == "string[]" || dataset.Field(GroupBy).Type == "object") throw new ArgumentException("GROUP BY requires a scalar field");
            }
            if (OrderBy != null)
            {
                if (Aggregates && !Select.Any(p => string.Equals(p.Label, OrderBy, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Aggregate ORDER BY must name an output column");
                if (!Aggregates) OrderBy = dataset.Field(OrderBy).Name;
            }
            Where?.Validate(dataset);
            Where?.OrderCheapFirst(dataset);
        }
    }
}
