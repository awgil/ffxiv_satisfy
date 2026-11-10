using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace Satisfy;

public static unsafe class AgentSatisfactionSupplyExtensions
{
    extension(AgentSatisfactionSupply)
    {
        public static AtkUnitBase* Addon
        {
            get
            {
                var agent = AgentSatisfactionSupply.Instance();
                return agent->IsAgentActive() ? RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agent->AddonId) : null;
            }
        }

        public static bool IsVisible => AgentSatisfactionSupply.Addon is var addon && addon != null && addon->IsVisible;

        public static bool IsOpenFor(NPCInfo npc)
        {
            var agent = AgentSatisfactionSupply.Instance();
            if (!agent->IsAgentActive() || agent->NpcInfo.Id != npc.Index + 1 || !agent->NpcInfo.Valid || !agent->NpcInfo.Initialized)
                return false;
            if (RaptureAtkUnitManager.GetFocusedAddonById(agent->AddonId) is var focused && focused != null && focused->IsVisible)
                return true;
            var addon = AgentSatisfactionSupply.Addon;
            return addon != null && addon->IsVisible && addon->IsReady && !IsBlocked();
        }

        public static void Deliver(int slot)
        {
            var res = new AtkValue();
            Span<AtkValue> values = stackalloc AtkValue[2];
            values[0].SetInt(1);
            values[1].SetInt(slot);
            AgentSatisfactionSupply.Instance()->ReceiveEvent(&res, values.GetPointer(0), 2, 0);
        }

        public static void Close()
        {
            var addon = AgentSatisfactionSupply.Addon;
            if (addon != null && addon->IsVisible)
                addon->Close(true);
        }
    }

    private static bool IsBlocked()
    {
        if (Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent] || AgentNpcTrade.IsActive)
            return true;
        var manager = RaptureAtkUnitManager.Instance();
        foreach (var name in (ReadOnlySpan<string>)["Talk", "Request", "SelectString", "SelectYesno"])
        {
            var blocker = manager->GetAddonByName(name);
            if (blocker != null && blocker->IsVisible)
                return true;
        }
        return false;
    }
}
