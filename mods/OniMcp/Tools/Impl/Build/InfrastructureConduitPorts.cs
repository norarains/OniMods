using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class InfrastructurePortReadTools
    {
        private static IEnumerable<Dictionary<string, object>> ConduitPorts(GameObject go, Building building, BuildingDef def, string kind)
        {
            if (building == null) yield break;
            var inputs = new HashSet<ConduitType>();
            var outputs = new HashSet<ConduitType>();
            foreach (var consumer in go.GetComponents<ConduitConsumer>())
            {
                inputs.Add(consumer.ConduitType);
                string layer = consumer.ConduitType == ConduitType.Gas ? "gas" : "liquid";
                if (Wants(kind, layer))
                    yield return Port(layer, "input", layer, building.GetUtilityInputCell(), LayersFor(layer),
                        new Dictionary<string, object> { ["connected"] = consumer.IsConnected });
            }
            foreach (var dispenser in go.GetComponents<ConduitDispenser>())
            {
                outputs.Add(dispenser.ConduitType);
                string layer = dispenser.ConduitType == ConduitType.Gas ? "gas" : "liquid";
                if (Wants(kind, layer))
                    yield return Port(layer, "output", layer, building.GetUtilityOutputCell(), LayersFor(layer),
                        new Dictionary<string, object> { ["connected"] = dispenser.IsConnected });
            }
            foreach (var secondary in go.GetComponents<ISecondaryOutput>())
                foreach (var type in new[] { ConduitType.Liquid, ConduitType.Gas })
                {
                    string layer = type == ConduitType.Gas ? "gas" : "liquid";
                    if (Wants(kind, layer) && secondary.HasSecondaryConduitType(type))
                        yield return DeclaredPort(layer, "secondary_output", PortOffsetCell(go, secondary.GetSecondaryConduitOffset(type)), LayersFor(layer));
                }
            foreach (var type in new[] { ConduitType.Liquid, ConduitType.Gas })
            {
                string layer = type == ConduitType.Gas ? "gas" : "liquid";
                if (!Wants(kind, layer)) continue;
                if (def.InputConduitType == type && !inputs.Contains(type))
                    yield return DeclaredPort(layer, "input", building.GetUtilityInputCell(), LayersFor(layer));
                if (def.OutputConduitType == type && !outputs.Contains(type))
                    yield return DeclaredPort(layer, "output", building.GetUtilityOutputCell(), LayersFor(layer));
            }
        }

        private static IEnumerable<Dictionary<string, object>> RailPorts(GameObject go, Building building, BuildingDef def)
        {
            if (building == null) yield break;
            var consumers = go.GetComponents<SolidConduitConsumer>();
            var dispensers = go.GetComponents<SolidConduitDispenser>();
            foreach (var consumer in consumers)
                yield return Port("rail", "input", "轨道输入", building.GetUtilityInputCell(), RailLayers,
                    new Dictionary<string, object> { ["connected"] = consumer.IsConnected });
            foreach (var dispenser in dispensers)
                yield return Port("rail", "output", "轨道输出", building.GetUtilityOutputCell(), RailLayers,
                    new Dictionary<string, object> { ["connected"] = dispenser.IsConnected });
            if (def.InputConduitType == ConduitType.Solid && !consumers.Any())
                yield return DeclaredPort("rail", "input", building.GetUtilityInputCell(), RailLayers);
            if (def.OutputConduitType == ConduitType.Solid && !dispensers.Any())
                yield return DeclaredPort("rail", "output", building.GetUtilityOutputCell(), RailLayers);
        }

        private static Dictionary<string, object> DeclaredPort(string layer, string role, int cell, ObjectLayer[] layers)
            => Port(layer, role, layer, cell, layers, new Dictionary<string, object> {
                ["connected"] = null, ["source"] = "building_definition"
            });
    }
}
