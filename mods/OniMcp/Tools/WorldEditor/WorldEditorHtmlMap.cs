using System.Net;
using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string RenderDiscoveredHtmlCells(int xMin, int xMax, int yMin, int yMax, HashedString mode)
        {
            var cells = new StringBuilder();
            for (int y = yMax; y >= yMin; y--)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    string color = "#1a202c";
                    string tooltip = $"Cell: ({x}, {y})\nUnknown (unrevealed)";
                    if (PlayerVisibility.Cell(cell))
                    {
                        var element = Grid.Element[cell];
                        float temperature = Grid.Temperature[cell];
                        var building = CellBuildingObject(cell);
                        string buildingName = building != null ? ToolUtil.CleanName(building.GetProperName()) : "";
                        color = GetHtmlCellColor(cell, element, building, mode, temperature, buildingName);
                        tooltip = $"Cell: {cell} ({x}, {y})\nElement: {element?.id.ToString() ?? "Vacuum"}\nTemp: {temperature - 273.15f:F1}°C";
                        if (building != null) tooltip += "\nBuilding: " + buildingName;
                    }
                    cells.AppendFormat("<div class=\"cell\" style=\"background:{0};\" title=\"{1}\"></div>",
                        color, WebUtility.HtmlEncode(tooltip));
                }
            }
            return cells.ToString();
        }
    }
}
