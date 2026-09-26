using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    internal static class WorldEditorResponsePolicy
    {
        // These are repeated planning explanations, not failure or mutation evidence.
        private static readonly HashSet<string> VerboseFields = new HashSet<string>
        {
            "planResolution", "anchorResolution", "actionTemplate", "buildingCandidates",
            "materialCandidates", "tokenHint", "guidance", "placementPoint", "dragGuidance", "coordinateContract"
        };

        internal static bool IncludeHelp(JObject args)
        {
            return args?["includeHelp"]?.Value<bool>() == true
                || string.Equals(args?["detail"]?.ToString(), "full", StringComparison.OrdinalIgnoreCase);
        }

        internal static JToken Body(CallToolResult result)
        {
            string text = result?.Content?.FirstOrDefault()?.Text ?? "No result";
            try { return Normalize(JToken.Parse(text), false); }
            catch (JsonException) { return new JValue(text); }
        }

        internal static CallToolResult Format(CallToolResult result, JObject args)
        {
            string command = ((args?["command"] ?? args?["op"] ?? args?["action"])?.ToString() ?? "").Trim().ToLowerInvariant();
            if (result == null || !new[] { "edit", "replace", "batch", "plan" }.Contains(command))
                return result;
            bool compact = !string.Equals(args?["responseMode"]?.ToString(), "full", StringComparison.OrdinalIgnoreCase);
            foreach (var content in result.Content ?? new List<ToolContent>())
            {
                if (content.Type != "text" || string.IsNullOrWhiteSpace(content.Text))
                    continue;
                try { content.Text = Normalize(JToken.Parse(content.Text), compact).ToString(Formatting.None); }
                catch (JsonException) { /* Plain text errors remain plain text. */ }
            }
            return result;
        }

        private static void CompactRepeatedDiagnostics(JObject output)
        {
            // Planning failures expose the same evidence at several compatibility paths.
            // Remove a copy only when an equal sibling retains that evidence.
            foreach (string name in new[] { "details", "diagnostics" })
            {
                if (!(output[name] is JObject details))
                    continue;
                foreach (var property in details.Properties().ToList())
                {
                    string sibling = property.Name == "message" ? "error" : property.Name;
                    if (output[sibling] != null && JToken.DeepEquals(output[sibling], property.Value))
                        property.Remove();
                }
                if (!details.HasValues)
                    output.Remove(name);
            }
            if (output["reasonCode"] != null && JToken.DeepEquals(output["reasonCode"], output["failureReason"]))
                output.Remove("failureReason");
            if (output["coordRole"]?.ToString() == "lowerLeftCell")
                output.Remove("note");
            if (output["errors"] is JArray errors && output["previews"] is JArray previews)
            {
                var retained = new JArray(previews.Where(preview => !errors.Any(error => JToken.DeepEquals(error, preview))));
                int omitted = previews.Count - retained.Count;
                if (omitted > 0)
                {
                    output["duplicatePreviewsOmitted"] = omitted;
                    if (retained.Count > 0) output["previews"] = retained;
                    else output.Remove("previews");
                }
            }
        }

        internal static JToken Normalize(JToken token, bool compact, int depth = 0)
        {
            if (token == null || depth > 40)
                return token?.DeepClone();
            if (token is JObject obj)
            {
                var output = new JObject();
                foreach (var property in obj.Properties())
                {
                    if (compact && VerboseFields.Contains(property.Name))
                        continue;
                    JToken value = property.Value;
                    if ((property.Name == "result" || property.Name == "error") && value.Type == JTokenType.String)
                    {
                        string text = value.Value<string>()?.Trim();
                        if (!string.IsNullOrEmpty(text) && (text[0] == '{' || text[0] == '['))
                        {
                            try { value = JToken.Parse(text); }
                            catch (JsonException) { }
                        }
                    }
                    output[property.Name] = Normalize(value, compact, depth + 1);
                }
                if (compact && output["error"] != null && output["result"] != null
                    && JToken.DeepEquals(output["error"], output["result"]))
                    output.Remove("error");
                if (compact)
                    CompactRepeatedDiagnostics(output);
                return output;
            }
            if (token is JArray array)
                return new JArray(array.Select(value => Normalize(value, compact, depth + 1)));
            return token.DeepClone();
        }
    }
}
