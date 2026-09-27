using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        private sealed class DigRiskBuilder
        {
            private readonly Dictionary<string, RiskBucket> buckets = new Dictionary<string, RiskBucket>();

            public void ScanTarget(int cell, int x, int y, int worldId)
            {
                if (!PlayerVisibility.Cell(cell))
                    return;

                float tempC = ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f;
                if (tempC >= 75f)
                    Add("hot_target", "warning", cell, x, y, $"Target solid is hot ({Math.Round(tempC, 1)}C).");

                foreach (int neighbor in Neighbors(cell))
                {
                    if (!Grid.IsValidCell(neighbor) || !ToolUtil.CellMatchesWorld(neighbor, worldId))
                        continue;
                    int nx = Grid.CellColumn(neighbor);
                    int ny = Grid.CellRow(neighbor);
                    if (!PlayerVisibility.Cell(neighbor))
                    {
                        Add("unexplored_neighbor", "warning", neighbor, nx, ny, "Adjacent terrain is unexplored; liquid and heat risks are unknown.");
                        continue;
                    }
                    var element = Grid.Element[neighbor];
                    if (element != null && element.IsLiquid)
                        Add("adjacent_liquid", "danger", neighbor, nx, ny, "Digging may open into adjacent liquid.");
                    else if (Grid.Mass[neighbor] <= 0.001f)
                        Add("adjacent_vacuum", "warning", neighbor, nx, ny, "Digging may open into vacuum.");

                    float neighborTempC = ToolUtil.SafeFloat(Grid.Temperature[neighbor]) - 273.15f;
                    if (neighborTempC >= 75f)
                        Add("adjacent_hot_cell", "warning", neighbor, nx, ny, $"Adjacent cell is hot ({Math.Round(neighborTempC, 1)}C).");
                }
            }

            public List<Dictionary<string, object>> ToList()
            {
                return buckets.Values
                    .OrderByDescending(item => item.Severity == "danger" ? 2 : item.Severity == "warning" ? 1 : 0)
                    .ThenBy(item => item.Type)
                    .Select(item => item.ToDictionary())
                    .ToList();
            }

            private void Add(string type, string severity, int cell, int x, int y, string message)
            {
                RiskBucket bucket;
                if (!buckets.TryGetValue(type, out bucket))
                {
                    bucket = new RiskBucket { Type = type, Severity = severity, Message = message };
                    buckets[type] = bucket;
                }
                bucket.Count++;
                if (bucket.Samples.Count < 12)
                    bucket.Samples.Add(CellResult(cell, type));
            }

            private static IEnumerable<int> Neighbors(int cell)
            {
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                yield return Grid.XYToCell(x, y + 1);
                yield return Grid.XYToCell(x, y - 1);
                yield return Grid.XYToCell(x - 1, y);
                yield return Grid.XYToCell(x + 1, y);
            }
        }

        private sealed class RiskBucket
        {
            public string Type;
            public string Severity;
            public string Message;
            public int Count;
            public readonly List<Dictionary<string, object>> Samples = new List<Dictionary<string, object>>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["type"] = Type,
                    ["severity"] = Severity,
                    ["count"] = Count,
                    ["message"] = Message,
                    ["samples"] = Samples
                };
            }
        }

        private static Dictionary<string, object> CellResult(int cell, string status)
        {
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["element"] = PlayerVisibility.Cell(cell) ? Grid.Element[cell]?.id.ToString() : null,
                ["massKg"] = PlayerVisibility.Cell(cell) ? (object)Math.Round(Grid.Mass[cell], 3) : null
            };
        }
    }
}
