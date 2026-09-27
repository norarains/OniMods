using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class InfrastructurePortReadTools
    {
        private static IEnumerable<Dictionary<string, object>> LogicPorts(GameObject go)
        {
            var ports = go.GetComponent<LogicPorts>();
            foreach (var port in GatePorts(go)) yield return port;
            if (ports == null)
                ports = ResolveBuildingDef(go)?.BuildingComplete?.GetComponent<LogicPorts>();
            if (ports == null) yield break;
            if (LogicPortReadSemantics.TryBridgeRoute(go, out int from, out int to))
            {
                string route = "from:" + Grid.CellColumn(from) + "," + Grid.CellRow(from)
                    + " via:" + Grid.CellColumn(Grid.PosToCell(go)) + "," + Grid.CellRow(Grid.PosToCell(go))
                    + " to:" + Grid.CellColumn(to) + "," + Grid.CellRow(to);
                yield return Port("logic", "input", "信号输入", from, LogicLayers,
                    new Dictionary<string, object> { ["semanticRole"] = "bridge_from",
                        ["connected"] = LogicPortReadSemantics.ConnectedAtCell(from), ["bridgeRoute"] = route });
                yield return Port("logic", "output", "信号输出", to, LogicLayers,
                    new Dictionary<string, object> { ["semanticRole"] = "bridge_to",
                        ["connected"] = LogicPortReadSemantics.ConnectedAtCell(to), ["bridgeRoute"] = route });
                yield break;
            }
            for (int i = 0; i < ports.inputPortInfo.Length; i++)
            {
                var port = ports.inputPortInfo[i];
                int cell = PortOffsetCell(go, port.cellOffset);
                yield return Port("logic", "input", "信号输入", cell, LogicLayers, new Dictionary<string, object>
                {
                    ["id"] = port.id.ToString(),
                    ["connected"] = LogicPortReadSemantics.ConnectedAtCell(cell),
                    ["value"] = LogicPortReadSemantics.InputValue(ports, i),
                    ["required"] = port.requiresConnection
                });
            }
            for (int i = 0; i < ports.outputPortInfo.Length; i++)
            {
                var port = ports.outputPortInfo[i];
                int cell = PortOffsetCell(go, port.cellOffset);
                yield return Port("logic", "output", "信号输出", cell, LogicLayers, new Dictionary<string, object>
                {
                    ["id"] = port.id.ToString(),
                    ["connected"] = LogicPortReadSemantics.ConnectedAtCell(cell),
                    ["value"] = LogicPortReadSemantics.OutputValue(ports, i),
                    ["required"] = port.requiresConnection
                });
            }
        }

        private static IEnumerable<Dictionary<string, object>> GatePorts(GameObject go)
        {
            var gate = go.GetComponent<LogicGateBase>() ?? ResolveBuildingDef(go)?.BuildingComplete?.GetComponent<LogicGateBase>();
            if (gate == null) yield break;
            int inputs = gate.RequiresFourInputs ? 4 : gate.RequiresTwoInputs ? 2 : 1;
            int outputs = gate.RequiresFourOutputs ? 4 : 1;
            foreach (var port in GatePortGroup(go, gate.inputPortOffsets, inputs, "input")) yield return port;
            foreach (var port in GatePortGroup(go, gate.outputPortOffsets, outputs, "output")) yield return port;
            if (gate.RequiresControlInputs)
                foreach (var port in GatePortGroup(go, gate.controlPortOffsets, 2, "control")) yield return port;
        }

        private static IEnumerable<Dictionary<string, object>> GatePortGroup(GameObject go, CellOffset[] offsets, int count, string role)
        {
            for (int i = 0; offsets != null && i < count && i < offsets.Length; i++)
            {
                int cell = PortOffsetCell(go, offsets[i]);
                yield return Port("logic", role == "control" ? "input" : role, role, cell, LogicLayers,
                    new Dictionary<string, object> { ["id"] = role + (i + 1),
                        ["connected"] = LogicPortReadSemantics.ConnectedAtCell(cell) });
            }
        }

        private static int PortOffsetCell(GameObject go, CellOffset offset)
        {
            var orientation = go.GetComponent<Rotatable>()?.GetOrientation() ?? Orientation.Neutral;
            return Grid.OffsetCell(Grid.PosToCell(go), Rotatable.GetRotatedCellOffset(offset, orientation));
        }

        private static IEnumerable<Dictionary<string, object>> PowerBridgePorts(GameObject go, BuildingDef def)
        {
            var link = go.GetComponent<UtilityNetworkLink>() ?? def.BuildingComplete?.GetComponent<UtilityNetworkLink>();
            if (link == null || def.BuildLocationRule != BuildLocationRule.WireBridge) yield break;
            link.GetCells(Grid.PosToCell(go), go.GetComponent<Rotatable>()?.GetOrientation() ?? Orientation.Neutral, out int a, out int b);
            yield return DeclaredPort("power", "bridge_end", a, PowerLayers);
            yield return DeclaredPort("power", "bridge_end", b, PowerLayers);
        }
    }
}
