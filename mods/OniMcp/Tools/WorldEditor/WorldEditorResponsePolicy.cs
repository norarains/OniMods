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
            if (output["materialSelection"] != null && JToken.DeepEquals(output["materialSelection"], output["materials"]))
                output.Remove("materials");
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
            // Keep anchor-specific evidence; publish equal material reports once per list.
            foreach (string listName in new[] { "results", "previews", "errors" })
            {
                if (!(output[listName] is JArray rows) || rows.Count < 2 || rows.Any(row => !(row is JObject)))
                    continue;
                var common = rows[0]["materialSelection"];
                if (common == null || !rows.All(row => JToken.DeepEquals(common, row["materialSelection"])))
                    continue;
                var shared = output["shared"] as JObject ?? new JObject();
                shared[listName] = new JObject { ["materialSelection"] = common.DeepClone() };
                output["shared"] = shared;
                foreach (JObject row in rows)
                    row.Remove("materialSelection");
            }
        }

        private static void CompactSuccessfulGeometry(JObject output)
        {
            if (output["footprint"] is JArray footprint && footprint.Count > 0
                && footprint.All(cell => cell["valid"]?.Value<bool>() == true
                    && cell["visible"]?.Value<bool>() == true && cell["inWorld"]?.Value<bool>() == true))
            {
                output["footprintCount"] = footprint.Count;
                output["footprintBounds"] = new JArray(footprint.Min(c => (int)c["x"]), footprint.Min(c => (int)c["y"]),
                    footprint.Max(c => (int)c["x"]), footprint.Max(c => (int)c["y"]));
                output.Remove("footprint");
            }
            if (output["support"] is JObject support && support["valid"]?.Value<bool>() == true)
            {
                foreach (string field in new[] { "cells", "supportCells" })
                    if (support[field] is JArray cells)
                    {
                        support[field + "Count"] = cells.Count;
                        support.Remove(field);
                    }
            }
            if (output["placementCheck"] is JObject check && check["valid"]?.Value<bool>() == true
                && output["actualPlacement"] is JObject actual)
            {
                actual.Remove("occupiedCells"); // exact registered-cell verification is retained in placementCheck
                actual.Remove("note");
            }
            // Successful auto-wiring retains endpoints, full path segments, counts and digging side effects.
            // Failure responses retain every child diagnostic for targeted recovery.
            if (output["connectResult"] is JObject connection && output["segments"] is JArray
                && output["failed"]?.Value<int>() == 0 && connection["failed"]?.Value<int>() == 0
                && !connection.Descendants().OfType<JProperty>().Any(property =>
                    (property.Name == "risks" || property.Name == "warnings" || property.Name == "obstructions") && property.Value.HasValues
                    || property.Name == "partial" && property.Value.Type == JTokenType.Boolean && property.Value.Value<bool>()))
            {
                output.Remove("connectResult");
                output.Remove("path");
            }
        }

        private static bool HasWarningEvidence(JToken token) => token is JContainer container
            && container.Descendants().OfType<JProperty>().Any(property =>
                new[] { "risks", "warnings", "obstructions", "missingSupportCells", "error" }.Contains(property.Name)
                && property.Value.Type != JTokenType.Null
                && (property.Value.HasValues || property.Value.Type == JTokenType.String && !string.IsNullOrEmpty(property.Value.ToString())));

        private static void CompactPlacementReceipt(JObject output)
        {
            if (output["valid"]?.Type != JTokenType.Boolean || output["valid"].Value<bool>() != true
                || output["prefabId"] == null || output["anchor"] == null) return;
            // Keep every warning, side effect, material budget and unsuccessful check.
            var check = output["placementCheck"] as JObject;
            if (check != null && check["valid"]?.Value<bool>() == true)
            {
                output["placementVerified"] = true;
                output["occupiedBounds"] = output["actualPlacement"]?["occupiedBounds"]?.DeepClone();
                output.Remove("placementCheck");
                output.Remove("actualPlacement");
                output.Remove("actualAnchor");
            }
            else if (check != null && check["pending"]?.Value<bool>() == true)
            {
                output["placementVerified"] = false;
                output["registrationPending"] = true;
            }
            if (output["placement"] is JObject placement && !HasWarningEvidence(placement))
            {
                if (output["footprintBounds"] == null && placement["footprintBounds"] != null)
                    output["footprintBounds"] = placement["footprintBounds"].DeepClone();
                output.Remove("placement");
            }
            if (output["support"] is JObject support && support["valid"]?.Value<bool>() == true && !HasWarningEvidence(support))
            {
                output["supportValidated"] = true;
                output.Remove("support");
            }
            foreach (string field in new[] { "name", "x", "y", "worldId" }) output.Remove(field);
            foreach (var property in output.Properties().Where(property => property.Value.Type == JTokenType.Null).ToList())
                property.Remove();
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
                {
                    CompactRepeatedDiagnostics(output);
                    CompactSuccessfulGeometry(output);
                    CompactPlacementReceipt(output);
                }
                return output;
            }
            if (token is JArray array)
                return new JArray(array.Select(value => Normalize(value, compact, depth + 1)));
            return token.DeepClone();
        }
    }
}
