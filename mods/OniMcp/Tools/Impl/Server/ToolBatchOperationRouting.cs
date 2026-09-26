namespace OniMcp.Tools
{
    public static partial class ToolBatchTools
    {
        internal static bool TryGetBatchOperation(string name, out McpTool tool)
        {
            // Internal aggregate operations retain validation/confirmation and cannot
            // use the coordinate gateway to bypass semantic targeting.
            if (OniToolRegistry.TryGetOperation(name, out tool)
                && tool.Name != "coordinate_control")
                return true;
            tool = null;
            return false;
        }
    }
}
