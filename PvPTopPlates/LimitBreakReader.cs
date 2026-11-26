using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PvPTopPlates;

/// <summary>
/// Reads every player's PvP Limit Break gauge from the Crystalline Conflict team lists (PLAYER TEAM / ENEMY TEAM,
/// addons PvPMKSPartyList1..3). Each member row is a component whose text node #21 is the name and whose
/// nine-grid #8 (inside res node #7) is the LB fill, scaled on X by the charge (verified against the own LB
/// from UIState: 1500/3000 -> 0.5, 2400 -> 0.8, 3000 -> 1, 400 -> 0.13). Read-only.
/// </summary>
internal sealed unsafe class LimitBreakReader
{
    private const uint NameNodeId = 21;
    private const uint GaugeRootNodeId = 7;
    private const uint GaugeFillNodeId = 8;
    private static readonly string[] AddonNames = { "PvPMKSPartyList1", "PvPMKSPartyList2", "PvPMKSPartyList3" };

    private readonly Dictionary<string, float> charges = new(16, StringComparer.Ordinal);

    public void Refresh()
    {
        charges.Clear();
        foreach (var addonName in AddonNames)
        {
            try
            {
                var unit = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(addonName, 1).Address;
                if (unit == null || !unit->IsVisible)
                    continue;

                ReadRows(&unit->UldManager);
            }
            catch (Exception exception)
            {
                Plugin.Log.Debug(exception, $"Could not read the Limit Break gauges of {addonName}.");
            }
        }
    }

    /// <summary>Players whose gauge was read this frame (Diagnostics).</summary>
    public int Count => charges.Count;

    public bool TryGet(string name, out float charge)
    {
        return charges.TryGetValue(name, out charge);
    }

    private void ReadRows(AtkUldManager* manager)
    {
        if (manager->NodeList == null)
            return;

        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || (int)node->Type < 1000 || !node->IsVisible())
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component == null)
                continue;

            AtkTextNode* nameNode = null;
            AtkResNode* gaugeRoot = null;
            AtkResNode* gaugeFill = null;
            var rows = &component->UldManager;
            if (rows->NodeList == null)
                continue;

            for (var j = 0; j < rows->NodeListCount; j++)
            {
                var child = rows->NodeList[j];
                if (child == null)
                    continue;

                if (child->NodeId == NameNodeId && child->Type == NodeType.Text)
                    nameNode = (AtkTextNode*)child;
                else if (child->NodeId == GaugeRootNodeId && child->Type == NodeType.Res)
                    gaugeRoot = child;
                else if (child->NodeId == GaugeFillNodeId && child->Type == NodeType.NineGrid)
                    gaugeFill = child;
            }

            if (nameNode == null || gaugeFill == null || (gaugeRoot != null && !gaugeRoot->IsVisible()))
                continue;

            var name = nameNode->NodeText.ToString().Trim();
            if (name.Length == 0)
                continue;

            var charge = gaugeFill->IsVisible() ? gaugeFill->ScaleX : 0f;
            charges[name] = float.IsFinite(charge) ? Math.Clamp(charge, 0f, 1f) : 0f;
        }
    }
}
