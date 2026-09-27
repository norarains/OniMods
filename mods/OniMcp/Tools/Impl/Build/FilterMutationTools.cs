using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FilterTools
    {
        private static CallToolResult SetSingleFilter(JObject args)
        {
            var go = FindTarget(args);
            if (go == null)
                return CallToolResult.Error("Target not found");
            var filterable = go.GetComponent<Filterable>();
            if (filterable == null)
                return CallToolResult.Error("Target does not expose Filterable");

            bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
            Tag before = filterable.SelectedTag;
            Tag next;
            if (ToolUtil.GetBool(args, "clear", false))
            {
                next = GameTags.Void;
            }
            else
            {
                string tagName = args["tag"]?.ToString();
                if (string.IsNullOrWhiteSpace(tagName))
                    return CallToolResult.Error("tag is required unless clear=true");
                var tag = new Tag(tagName.Trim());
                if (!SingleFilterOptions(filterable).Contains(tag))
                    return CallToolResult.Error("tag is not currently valid for this Filterable; inspect building_control domain=filter action=list includeOptions=true");
                next = tag;
            }

            if (!dryRun) filterable.SelectedTag = next;
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = "single",
                ["dryRun"] = dryRun,
                ["committed"] = !dryRun,
                ["proposed"] = TagInfo(next),
                ["wouldChange"] = before != next,
                ["before"] = TagInfo(before),
                ["selected"] = TagInfo(filterable.SelectedTag),
                ["changed"] = before != filterable.SelectedTag
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult SetTreeFilter(JObject args)
        {
            var go = FindTarget(args);
            if (go == null)
                return CallToolResult.Error("Target not found");
            var tree = go.GetComponent<TreeFilterable>();
            if (tree == null)
                return CallToolResult.Error("Target does not expose TreeFilterable");

            bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
            string mode = (args["mode"]?.ToString() ?? "replace").Trim().ToLowerInvariant();
            if (ToolUtil.GetBool(args, "clear", false) && args["mode"] == null) mode = "clear";
            if (mode != "replace" && mode != "add" && mode != "remove" && mode != "clear")
                return CallToolResult.Error("mode must be replace, add, remove or clear");
            var before = tree.GetTags().Select(TagInfo).ToList();
            var next = new HashSet<Tag>(tree.GetTags());
            var requested = ParseTags(args["tags"]);

            if (mode == "clear" || mode == "replace")
                next.Clear();
            if (mode != "clear" && requested.Count == 0)
                return CallToolResult.Error("tags must contain at least one tag unless mode=clear");

            foreach (var tag in mode == "clear" ? new List<Tag>() : requested)
            {
                if (mode == "remove")
                    next.Remove(tag);
                else
                    next.Add(tag);
            }

            var flat = go.GetComponent<FlatTagFilterable>();
            if (flat != null && next.Any(tag => !flat.tagOptions.Contains(tag)))
                return CallToolResult.Error("tag is not currently valid for this flat filter");
            if (!dryRun)
            {
                if (flat != null) ApplyFlatTags(flat, next);
                else tree.UpdateFilters(next);
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = flat != null ? "flat" : "tree",
                ["mode"] = mode,
                ["dryRun"] = dryRun,
                ["committed"] = !dryRun,
                ["proposed"] = next.Select(TagInfo).OrderBy(item => item["tag"].ToString()).ToList(),
                ["before"] = before,
                ["selected"] = tree.GetTags().Select(TagInfo).OrderBy(item => item["tag"].ToString()).ToList()
            }, McpJsonUtil.Settings));
        }

    }
}
