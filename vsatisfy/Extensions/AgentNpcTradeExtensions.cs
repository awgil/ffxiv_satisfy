using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace Satisfy;

public static unsafe class AgentNpcTradeExtensions
{
    extension(AgentNpcTrade)
    {
        public static bool IsActive => AgentNpcTrade.Instance()->IsAgentActive();

        public static bool IsSelecting
        {
            get
            {
                if (AgentNpcTrade.Instance()->SelectedTurnInSlot >= 0)
                    return true;
                var menu = RaptureAtkUnitManager.Instance()->GetAddonByName("ContextIconMenu");
                return menu != null && menu->IsVisible;
            }
        }

        public static int IndexOfRequest(uint itemId)
        {
            if (!AgentNpcTrade.IsActive)
                return -1;
            ref var requests = ref UIState.Instance()->NpcTrade.Requests;
            for (var i = 0; i < requests.Count; ++i)
                if (requests.Items[i].ItemId == itemId)
                    return i;
            return -1;
        }

        public static bool FillRequest(int index)
        {
            var agent = AgentNpcTrade.Instance();
            if (!agent->IsAgentActive() || agent->SelectedTurnInSlot >= 0)
                return false;

            var res = new AtkValue();
            Span<AtkValue> param = stackalloc AtkValue[4];
            param[0].SetInt(2); // start turnin
            param[1].SetInt(index); // slot
            param[2].SetInt(0); // ???
            param[3].SetInt(0); // ???
            agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);

            if (agent->SelectedTurnInSlot != index || agent->SelectedTurnInSlotItemOptions <= 0)
            {
                Service.Log.Error($"Failed to start turn-in: cur slot={agent->SelectedTurnInSlot}, expected={index}, count={agent->SelectedTurnInSlotItemOptions}");
                return false;
            }

            param[0].SetInt(0); // confirm
            param[1].SetInt(0); // option #0
            agent->ReceiveEvent(&res, param.GetPointer(0), 4, 1);
            return agent->SelectedTurnInSlot < 0;
        }
    }
}
