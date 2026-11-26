using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PvPTopPlates;

/// <summary>
/// Diagnostic for finding where the client keeps other players' PvP Limit Break gauges (shown by the PvP HUD).
/// /ptop lbscan: logs the own LB from UIState, every number array slot holding the same value, and the
/// AtkValues of every loaded PvP-looking addon. Results go to dalamud.log ("[LBSCAN]").
/// </summary>
internal static unsafe class LimitBreakScout
{
    public static void Run()
    {
        var log = new StringBuilder();
        var lb = &UIState.Instance()->LimitBreakController;
        log.AppendLine($"own LB: CurrentUnits={lb->CurrentUnits} BarUnits={lb->BarUnits} BarCount={lb->BarCount} IsPvP={lb->IsPvP}");
        var probes = new HashSet<int> { lb->CurrentUnits };
        if (lb->BarUnits > 0)
            probes.Add((int)Math.Round(100.0 * lb->CurrentUnits / Math.Max(1, lb->BarUnits * Math.Max((byte)1, lb->BarCount))));
        probes.Remove(0);

        var holder = &RaptureAtkModule.Instance()->AtkModule.AtkArrayDataHolder;
        for (var a = 0; a < holder->NumberArrayCount; a++)
        {
            var array = holder->NumberArrays[a];
            if (array == null || array->IntArray == null)
                continue;

            var size = array->AtkArrayData.Size;
            for (var i = 0; i < size; i++)
            {
                if (!probes.Contains(array->IntArray[i]))
                    continue;

                var from = Math.Max(0, i - 8);
                var to = Math.Min(size, i + 9);
                var around = string.Join(",", Enumerable.Range(from, to - from).Select(k => array->IntArray[k]));
                log.AppendLine($"numarray {a} [{i}]={array->IntArray[i]} (size {size}) around {from}..{to - 1}: {around}");
            }
        }

        var manager = RaptureAtkUnitManager.Instance();
        var list = &manager->AtkUnitManager.AllLoadedUnitsList;
        for (var u = 0; u < list->Count; u++)
        {
            var unit = list->Entries[u].Value;
            if (unit == null)
                continue;

            var name = unit->NameString;
            if (!name.Contains("PvP", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("MKS", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Party", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Enemy", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = new StringBuilder();
            for (var v = 0; v < unit->AtkValuesCount && v < 400; v++)
            {
                var value = unit->AtkValues[v];
                values.Append(value.Type switch
                {
                    AtkValueType.Int => $"{v}:i{value.Int} ",
                    AtkValueType.UInt => $"{v}:u{value.UInt} ",
                    AtkValueType.Bool => $"{v}:b{value.Byte} ",
                    AtkValueType.Float => $"{v}:f{value.Float:0.###} ",
                    _ => string.Empty,
                });
            }

            log.AppendLine($"addon {name} visible={unit->IsVisible} values={unit->AtkValuesCount}: {values}");
        }

        // v2: the number array that held the own LB percent, in full; string arrays holding the local player's
        // name; the node trees of the Crystalline Conflict team lists.
        var number65 = holder->NumberArrayCount > 65 ? holder->NumberArrays[65] : null;
        if (number65 != null && number65->IntArray != null)
        {
            var n = number65->AtkArrayData.Size;
            log.AppendLine("numarray65 full: " + string.Join(",", Enumerable.Range(0, n).Select(k => $"{k}={number65->IntArray[k]}")));
        }

        var localName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
        for (var a = 0; a < holder->StringArrayCount && localName.Length > 0; a++)
        {
            var array = holder->StringArrays[a];
            if (array == null || array->StringArray == null)
                continue;

            var size = array->AtkArrayData.Size;
            var hit = false;
            var texts = new StringBuilder();
            for (var i = 0; i < size; i++)
            {
                var pointer = array->StringArray[i];
                if (!pointer.HasValue)
                    continue;
                var text = pointer.ToString() ?? string.Empty;
                if (text.Length == 0)
                    continue;
                if (text.Contains(localName, StringComparison.Ordinal))
                    hit = true;
                texts.Append($"{i}='{Clean(text)}' ");
            }

            if (hit)
                log.AppendLine($"strarray {a} (size {size}): {texts}");
        }

        foreach (var addonName in new[] { "PvPMKSPartyList1", "PvPMKSPartyList2", "PvPMKSPartyList3" })
        {
            var unit = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(addonName, 1).Address;
            if (unit == null)
                continue;
            log.AppendLine($"tree {addonName}:");
            DumpNodes(log, &unit->UldManager, 1);
        }

        foreach (var line in log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Plugin.Log.Information("[LBSCAN] " + line.TrimEnd());
    }

    private static string Clean(string text)
    {
        var chars = text.Where(c => c >= ' ' && c < 0xE000).ToArray();
        return new string(chars);
    }

    private static void DumpNodes(StringBuilder log, AtkUldManager* manager, int depth)
    {
        if (depth > 4 || manager == null || manager->NodeList == null)
            return;

        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
                continue;

            var line = $"{new string(' ', depth * 2)}#{node->NodeId} {node->Type} vis={node->IsVisible()} " +
                       $"x={node->X:0} y={node->Y:0} w={node->Width} h={node->Height} sx={node->ScaleX:0.###}";
            if (node->Type == NodeType.Text)
                line += $" text='{Clean(((AtkTextNode*)node)->NodeText.ToString())}'";
            else if (node->Type == NodeType.Image)
                line += $" part={((AtkImageNode*)node)->PartId}";
            log.AppendLine(line);

            if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    DumpNodes(log, &component->UldManager, depth + 1);
            }
        }
    }
}
