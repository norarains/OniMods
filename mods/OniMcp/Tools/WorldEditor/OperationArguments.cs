using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    // Shared by typed operation files and management commands. JSON remains typed;
    // whitespace inside arrays, objects and escaped quoted strings is significant.
    internal static class OperationArguments
    {
        internal static JObject Parse(string line)
        {
            var result = new JObject();
            foreach (string token in Tokens(line).Skip(1))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0) continue;
                string key = token.Substring(0, eq);
                string value = token.Substring(eq + 1);
                if (result.Property(key) != null) throw new ArgumentException("Duplicate argument: " + key);
                try
                {
                    if (value.StartsWith("[") || value.StartsWith("{") || value.StartsWith("\""))
                        result[key] = JToken.Parse(value);
                    else if (bool.TryParse(value, out bool flag)) result[key] = flag;
                    else if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) result[key] = integer;
                    else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        && !double.IsNaN(number) && !double.IsInfinity(number)) result[key] = number;
                    else result[key] = value;
                }
                catch (JsonException) { throw new ArgumentException("Invalid JSON argument: " + key); }
            }
            return result;
        }

        internal static IEnumerable<string> Tokens(string line)
        {
            var current = new StringBuilder();
            var closing = new Stack<char>();
            bool quoted = false, escaped = false;
            foreach (char c in line ?? string.Empty)
            {
                if (quoted)
                {
                    current.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '[') closing.Push(']');
                else if (c == '{') closing.Push('}');
                else if (c == ']' || c == '}')
                {
                    if (closing.Count == 0 || closing.Pop() != c)
                        throw new ArgumentException("Unbalanced JSON argument");
                }
                if (char.IsWhiteSpace(c) && closing.Count == 0 && !quoted)
                {
                    if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                }
                else current.Append(c);
            }
            if (quoted || closing.Count != 0) throw new ArgumentException("Unterminated quoted or JSON argument");
            if (current.Length > 0) yield return current.ToString();
        }
    }
}
