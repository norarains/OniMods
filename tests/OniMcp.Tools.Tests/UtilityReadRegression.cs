using System;
using System.Collections.Generic;
using OniMcp.Tools;
using UnityEngine;

internal static class UtilityReadRegression
{
    internal static void Run()
    {
        var layers = new[] { ObjectLayer.LiquidConduit };
        int cell = Grid.XYToCell(10, 6), right = cell + 1;
        var network = new TestUtilityNetwork();
        var pipe = Pipe(network, true); var neighbor = Pipe(network, true);
        Grid.Objects[cell, 0] = pipe;
        network.Physical[cell] = network.Planned[cell] = UtilityConnections.Left | UtilityConnections.Right;
        Check(UtilityConnectionRead.Read(cell, layers) == 0, "dangling native bits do not link empty cells");
        Check(UtilityConnectionRead.Read(right, layers) == 0, "empty cells have no links despite neighboring pipes");
        Grid.Objects[right, 0] = neighbor;
        network.Physical[right] = UtilityConnections.Right;
        network.Planned[right] = UtilityConnections.Left;
        Check(UtilityConnectionRead.Read(cell, layers) == 0, "physical links need reciprocal physical bits");
        network.Physical[right] = UtilityConnections.Left;
        Check(UtilityConnectionRead.Read(cell, layers) == UtilityConnections.Right, "endpoint has only its actual reciprocal neighbor");
        Check(UtilityConnectionRead.Read(right, layers) == UtilityConnections.Left, "endpoint relation is symmetric");
        Grid.WorldIdx[right] = 1;
        Check(UtilityConnectionRead.Read(cell, layers) == 0, "adjacent worlds cannot link");
        Grid.WorldIdx[right] = 0;
        Grid.Objects[right, 0] = Pipe(network, false);
        Check(UtilityConnectionRead.Read(cell, layers) == UtilityConnections.Right, "mixed blueprint path uses reciprocal planned network");
        Check(!UtilityConnectionRead.IsBuilt(right, layers) && UtilityConnectionRead.IsBuilt(cell, layers), "planned presence does not claim a physical pipe");
        Grid.Objects[cell, 0] = Grid.Objects[right, 0] = null;
        Console.WriteLine("Utility read regression checks passed: 8");
    }

    private static GameObject Pipe(TestUtilityNetwork network, bool built)
    {
        var result = new GameObject();
        result.Components[typeof(TestUtilityProvider)] = new TestUtilityProvider(network);
        if (built) result.Components[typeof(BuildingComplete)] = new BuildingComplete();
        return result;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

[Flags] internal enum UtilityConnections { Left = 1, Right = 2, Up = 4, Down = 8 }
internal enum ObjectLayer { LiquidConduit }
internal sealed class BuildingComplete { }
internal sealed class Building { internal BuildingDef Def { get; set; } }
internal interface IHaveUtilityNetworkMgr { TestUtilityNetwork GetNetworkManager(); }
internal sealed class TestUtilityProvider : IHaveUtilityNetworkMgr
{
    private readonly TestUtilityNetwork network;
    internal TestUtilityProvider(TestUtilityNetwork network) { this.network = network; }
    public TestUtilityNetwork GetNetworkManager() => network;
}
internal sealed class TestUtilityNetwork
{
    internal readonly Dictionary<int, UtilityConnections> Physical = new Dictionary<int, UtilityConnections>();
    internal readonly Dictionary<int, UtilityConnections> Planned = new Dictionary<int, UtilityConnections>();
    internal UtilityConnections GetConnections(int cell, bool physical)
        => (physical ? Physical : Planned).TryGetValue(cell, out var value) ? value : 0;
}
