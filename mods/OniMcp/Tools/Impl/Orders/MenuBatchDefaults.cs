using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class MenuBatchDefaults
    {
        internal static JObject From(JObject args)
        {
            var source = args["defaults"] as JObject ?? args["defaultArguments"] as JObject;
            var defaults = source == null ? new JObject() : (JObject)source.DeepClone();
            if (defaults["priority"] == null && args["priority"] != null)
                defaults["priority"] = args["priority"].DeepClone();
            return defaults;
        }
    }
}
