using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Tools;

internal static class DigDiscoveryRegression
{
    internal static void Run()
    {
        int checks = 0;
        Action<bool, string> check = (ok, message) => {
            checks++;
            if (!ok) throw new Exception("Dig discovery: " + message);
        };
        int target = Grid.XYToCell(20, 4), neighbor = target + 1;
        int[] cells = { target, neighbor, target - 1, target + Grid.WidthInCells, target - Grid.WidthInCells };
        foreach (int cell in cells)
        {
            Grid.Element[cell] = new Element { id = "Rock" };
            Grid.Mass[cell] = 100;
            Grid.Temperature[cell] = 300;
        }
        Grid.Element[neighbor] = new Element { id = "HotWater", IsLiquid = true };
        Grid.Mass[neighbor] = 1234;
        Grid.Temperature[neighbor] = 450;
        Grid.Hidden.Add(neighbor);
        try
        {
            var risks = OrdersTools.DigRisksFixture(target);
            check(risks.Count == 1 && (string)risks[0]["type"] == "unexplored_neighbor",
                "fog gives only an unknown risk, never hidden liquid or heat");
            var sample = ((List<Dictionary<string, object>>)risks[0]["samples"]).Single();
            check(sample["element"] == null && sample["massKg"] == null, "unknown sample hides element and mass");
            Grid.Mass[neighbor] = 0;
            Grid.Element[neighbor] = null;
            check(OrdersTools.DigRisksFixture(target).Count == 1, "hidden vacuum has the same observation as hidden liquid");
            Grid.Hidden.Remove(neighbor);
            check(OrdersTools.DigRisksFixture(target).Any(r => (string)r["type"] == "adjacent_vacuum"),
                "exploration makes vacuum observable");
            Grid.Element[neighbor] = new Element { id = "HotWater", IsLiquid = true };
            Grid.Mass[neighbor] = 1234;
            risks = OrdersTools.DigRisksFixture(target);
            check(risks.Any(r => (string)r["type"] == "adjacent_liquid"), "known liquid hazard remains visible");
            check(risks.Any(r => (string)r["type"] == "adjacent_hot_cell"), "known heat hazard remains visible");
            Grid.Hidden.Add(target);
            check(OrdersTools.DigRisksFixture(target).Count == 0, "unexplored target cannot disclose neighbors");
        }
        finally
        {
            Grid.Hidden.Remove(target);
            Grid.Hidden.Remove(neighbor);
            foreach (int cell in cells)
            {
                Grid.Element[cell] = null;
                Grid.Mass[cell] = Grid.Temperature[cell] = 0;
            }
        }
        Console.WriteLine("Dig discovery regression: " + checks + " checks passed");
    }
}

internal sealed class Element
{
    internal string id;
    internal bool IsLiquid;
}

internal static partial class Grid
{
    internal static readonly Element[] Element = new Element[WidthInCells * HeightInCells];
    internal static readonly float[] Mass = new float[WidthInCells * HeightInCells];
    internal static readonly float[] Temperature = new float[WidthInCells * HeightInCells];
}

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        internal static List<Dictionary<string, object>> DigRisksFixture(int cell)
        {
            var risks = new DigRiskBuilder();
            risks.ScanTarget(cell, Grid.CellColumn(cell), Grid.CellRow(cell), 0);
            return risks.ToList();
        }
    }
}
