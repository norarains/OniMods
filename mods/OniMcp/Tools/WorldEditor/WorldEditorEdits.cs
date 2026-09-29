using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult Edit(JObject args)
        {
            string path = NormalizePath(Text(args, "path"), _cwd);
            if (!path.StartsWith("/active/", StringComparison.Ordinal))
            {
                return CallToolResult.Error("Cannot apply edits to historical or unloaded saves. Edits can only be performed under the '/active/' directory representing the currently active game.");
            }
            string relative = SaveRelativePath(path);
            if (args["editCells"] != null || args["editLines"] != null)
                return CallToolResult.Error("Coordinate map edits are forbidden. Read /active/map/viewport.md and submit content as a SEARCH/REPLACE patch.");

            string block = Text(args, "content");
            List<KeyValuePair<string, string>> edits;
            if (!TryParseSearchReplaceBlocks(block, out edits))
                return CallToolResult.Error("edit requires at least one <<<<<<< SEARCH / ======= / >>>>>>> REPLACE block");
            if (edits.Count > 1 && !ToolUtil.GetBool(args, "allowPartial", false))
                return CallToolResult.Error("Multiple write blocks require allowPartial=true because game mutations cannot be rolled back transactionally.");

            var preflight = new JArray();
            for (int i = 0; i < edits.Count; i++)
            {
                var result = PreflightSingleEditBlock(args, path, relative, edits[i].Key, edits[i].Value);
                if (WorldEditorResultFailed(result))
                    return CallToolResult.Error(JsonResultText(new JObject
                    {
                        ["ok"] = false,
                        ["phase"] = "preflight",
                        ["block"] = i,
                        ["error"] = result.Content?.FirstOrDefault()?.Text ?? "preflight failed",
                        ["applied"] = 0
                    }));
                preflight.Add(new JObject { ["block"] = i, ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty });
            }

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("search_replace", path, preflight);

            var results = new JArray();
            int applied = 0;
            bool partial = false;
            for (int i = 0; i < edits.Count; i++)
            {
                var result = ApplySingleEditBlock(args, path, relative, edits[i].Key, edits[i].Value);
                results.Add(new JObject
                {
                    ["block"] = i,
                    ["ok"] = !WorldEditorResultFailed(result),
                    ["result"] = result?.Content?.FirstOrDefault()?.Text ?? "edit failed"
                });
                if (WorldEditorResultFailed(result))
                    return WorldEditorExecutionFailure("search_replace", path, applied + ResultAppliedCount(result), results);
                partial = partial || ResultReportsPartial(result);
                applied++;
            }

            return JsonResult(new JObject { ["ok"] = true, ["partial"] = partial, ["applied"] = applied, ["failed"] = 0, ["results"] = results });
        }

        private static CallToolResult PreflightSingleEditBlock(JObject args, string path, string relative, string search, string replace)
        {
            if (!ValidateVirtualFileSearch(args, path, relative, search, out string searchError))
                return CallToolResult.Error(searchError);
            var routed = CopyPayload(args);
            routed["sourcePath"] = path;
            if (IsEditableMapMarkdown(relative))
                return PreflightMapEdit(routed, search, replace);
            if (IsEditableBuildCommandFile(relative))
                return PreflightBuildEdit(routed, relative, replace);
            if (IsEditableManagementMarkdown(relative))
                return PreflightManagementMarkdownEdit(relative, replace);
            if (IsBlueprintMarkdown(relative))
                return PreflightBlueprintMarkdownEdit(relative, search, replace);
            if (IsEditableOperationMarkdown(relative))
                return PreflightOperationMarkdownEdit(args, relative, replace);
            if (IsDupeDetailMarkdown(relative))
                return PreflightDupeDetailEdit(relative, replace);
            if (IsBuildingDetailMarkdown(relative))
                return PreflightBuildingDetailEdit(relative, search, replace);
            return CallToolResult.Error("file is read-only for edits: " + path);
        }

        private static CallToolResult ApplySingleEditBlock(JObject args, string path, string relative, string search, string replace)
        {
            string searchError;
            if (!ValidateVirtualFileSearch(args, path, relative, search, out searchError))
                return CallToolResult.Error(searchError);
            var routed = CopyPayload(args);
            routed.Remove("content");
            routed["sourcePath"] = path;
            routed["searchText"] = search;
            routed["replacementText"] = replace;

            if (IsEditableMapMarkdown(relative))
                return ApplyMapEdit(routed, search, replace);
            if (IsEditableBuildCommandFile(relative))
                return ApplyBuildEdit(routed, relative, replace);
            if (IsEditableManagementMarkdown(relative))
                return ApplyManagementMarkdownEdit(routed, relative, replace);
            if (IsBlueprintMarkdown(relative))
                return ApplyBlueprintMarkdownEdit(routed, relative, search, replace);
            if (IsEditableOperationMarkdown(relative))
                return ApplyOperationMarkdownEdit(routed, relative, replace);
            if (IsDupeDetailMarkdown(relative))
                return ApplyDupeDetailEdit(routed, relative, replace);
            if (IsBuildingDetailMarkdown(relative))
                return ApplyBuildingDetailEdit(routed, relative, search, replace);

            return CallToolResult.Error("file is read-only for edits: " + path);
        }
    }
}
