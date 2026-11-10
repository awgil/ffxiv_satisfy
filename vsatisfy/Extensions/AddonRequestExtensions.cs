using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Satisfy;

public static unsafe class AddonRequestExtensions
{
    private const int SlotCount = 5;

    extension(AddonRequest)
    {
        public static AddonRequest* Addon => (AddonRequest*)RaptureAtkUnitManager.Instance()->GetAddonByName("Request");

        public static bool IsReadyToHandOver(int index)
        {
            var addon = AddonRequest.Addon;
            return addon != null && addon->AtkUnitBase.IsVisible && addon->AtkUnitBase.IsReady && addon->HandOverButton != null && addon->HandOverButton->IsEnabled && addon->IsSlotFilled(index);
        }

        public static string? HandOver()
        {
            var addon = AddonRequest.Addon;
            if (addon == null || !addon->AtkUnitBase.IsVisible || !addon->AtkUnitBase.IsReady)
                return "addon not ready";
            var button = addon->HandOverButton;
            if (button == null || button->OwnerNode == null)
                return "no hand over button";
            if (!button->IsEnabled)
                return "hand over button disabled";
            var evt = button->OwnerNode->AtkResNode.AtkEventManager.Event;
            if (evt == null)
                return "hand over button has no event";
            var data = new AtkEventData();
            addon->AtkUnitBase.ReceiveEvent(evt->State.EventType, (int)evt->Param, evt, &data);
            return null;
        }

        public static string GetState()
        {
            var agent = AgentNpcTrade.Instance();
            var addon = AddonRequest.Addon;
            if (addon == null)
                return $"agent={agent->IsAgentActive()}, sel={agent->SelectedTurnInSlot}, no addon";
            var icons = new uint[SlotCount];
            for (var i = 0; i < SlotCount; ++i)
                icons[i] = addon->SlotIcon(i);
            var button = addon->HandOverButton;
            return $"agent={agent->IsAgentActive()}, sel={agent->SelectedTurnInSlot}, visible={addon->AtkUnitBase.IsVisible}, ready={addon->AtkUnitBase.IsReady}, entries={addon->EntryCount}, handover={(button != null ? button->IsEnabled : null)}, slots=[{string.Join(",", icons)}]";
        }
    }

    extension(ref AddonRequest addon)
    {
        public uint SlotIcon(int index)
        {
            if (index is < 0 or >= SlotCount)
                return 0;
            fixed (AtkComponentDragDrop** slots = &addon.AtkComponentDragDrop250)
            {
                var dragDrop = slots[index];
                return dragDrop != null && dragDrop->AtkComponentIcon != null ? dragDrop->AtkComponentIcon->IconId : 0;
            }
        }

        public bool IsSlotFilled(int index) => addon.SlotIcon(index) is not (0 or uint.MaxValue); // empty is -1
    }
}
