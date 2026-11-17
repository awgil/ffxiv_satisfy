using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;

namespace Satisfy;

// common automation utilities
public abstract class AutoCommon : TaskBase
{
    private enum TurnInState
    {
        Talk,
        YesNo,
        RequestFill, // waiting for item
        RequestHandOver, // waiting for hand over
        RequestWait, // busy
        SupplyMenu, // need to deliver
        NpcMenu, // npc selectstring
        CloseMenu, // close selectstring
        CloseSupply,
        Busy,
        Idle, // nothing open, deliveries left
        Finished,
    }

    private const int MaxAttempts = 3;

    protected async Task TurnIn(NPCInfo npc, int slot)
    {
        using var scope = BeginScope("TurnIn");
        if (npc.CraftData is null || npc.RemainingTurnins(slot) is 0) return;

        var itemId = npc.TurnInItems[slot];
        TurnInState? state = null;
        var acted = false;
        string? lastHandOverError = null;
        string? lastSceneTasks = null;
        // a delivery is queued from Deliver until the remaining count drops. without this it'd double hand in if something like scrip overcap happens
        var delivery = DeliveryPhase.None;
        var loggedDeliveryWait = false;
        var remaining = npc.RemainingTurnins(slot);
        var interacts = 0;
        while (true)
        {
            var curRemaining = npc.RemainingTurnins(slot);
            if (curRemaining != remaining)
            {
                remaining = curRemaining;
                interacts = 0;
                delivery = DeliveryPhase.None;
            }
            if (delivery == DeliveryPhase.Requested && IsRequestFilled(itemId))
                delivery = DeliveryPhase.Filled; // or like queued to be filled

            var sceneTasks = EventFramework.PendingSceneTasks;
            if (sceneTasks != lastSceneTasks)
            {
                Log($"scene tasks: {sceneTasks}");
                lastSceneTasks = sceneTasks;
            }

            var next = GetTurnInState(npc, itemId, remaining);
            if (next != state)
            {
                Log($"turn-in state: {state} -> {next} ({remaining} left)");
                state = next;
                acted = false;
                lastHandOverError = null;
            }
            if (next == TurnInState.Finished)
                break;

            var canAct = !acted && !EventFramework.HasPendingSceneTasks;
            Status = $"Turning in {ItemName(itemId)} ({remaining} left): {next}";
            switch (next)
            {
                case TurnInState.Talk:
                    AddonTalk.Progress();
                    break;
                case TurnInState.YesNo when canAct:
                    acted = true;
                    AddonSelectYesno.Yes();
                    break;
                case TurnInState.NpcMenu when canAct:
                    acted = true;
                    AddonSelectString.Select(0);
                    break;
                case TurnInState.CloseMenu when canAct:
                    acted = true;
                    AtkUnitBase.CloseAddon("SelectString");
                    break;
                case TurnInState.RequestFill when canAct:
                    acted = true;
                    var index = AgentNpcTrade.IndexOfRequest(itemId);
                    if (!AgentNpcTrade.FillRequest(index))
                        Log($"failed to fill slot {index}: {AddonRequest.GetState()}");
                    break;
                case TurnInState.RequestHandOver when canAct:
                    var handOverError = AddonRequest.HandOver();
                    acted = handOverError == null;
                    if (acted)
                        Log($"hand over clicked: {AddonRequest.GetState()}");
                    else if (handOverError != lastHandOverError)
                        Log($"hand over not clicked ({handOverError}): {AddonRequest.GetState()}");
                    lastHandOverError = handOverError;
                    break;
                case TurnInState.SupplyMenu when canAct:
                    if (delivery == DeliveryPhase.Filled)
                    {
                        if (!loggedDeliveryWait)
                            Log("supply menu open, but the previous delivery is still in flight; waiting");
                        loggedDeliveryWait = true;
                        break;
                    }
                    acted = true;
                    loggedDeliveryWait = false;
                    if (delivery == DeliveryPhase.Requested)
                        Log("previous request closed without being filled, delivering again");
                    Log($"delivering slot {slot} (scene tasks: {sceneTasks})");
                    AgentSatisfactionSupply.Deliver(slot);
                    delivery = DeliveryPhase.Requested;
                    break;
                case TurnInState.CloseSupply when canAct:
                    acted = true;
                    Log($"closing supply menu (scene tasks: {sceneTasks})");
                    AgentSatisfactionSupply.Close();
                    break;
                case TurnInState.Idle when !acted:
                    acted = true;
                    delivery = DeliveryPhase.None; // the event ended, whatever was in flight is resolved
                    ErrorIf(++interacts > MaxAttempts, $"Talked to {npc.Name} {MaxAttempts} times without delivering anything");
                    ErrorIf(!Service.Objects.TryGetByGameObjectId(npc.CraftData.TurnInInstanceId, out var obj), $"Unable to find {npc.Name}#{npc.CraftData.TurnInInstanceId:X}");
                    await InteractWith(obj!);
                    break;
            }
            await NextFrame();
        }
    }

    private enum DeliveryPhase
    {
        None,
        Requested, // Deliver sent, request window not filled yet (closing it now cancels the delivery)
        Filled, // request was filled, waiting for the remaining count to drop
    }

    private static unsafe bool IsRequestFilled(uint itemId)
    {
        var addon = AddonRequest.Addon;
        return AgentNpcTrade.IsActive && addon != null && addon->IsSlotFilled(AgentNpcTrade.IndexOfRequest(itemId));
    }

    private static TurnInState GetTurnInState(NPCInfo npc, uint itemId, int remaining)
    {
        if (AtkUnitBase.IsAddonReady("Talk"))
            return TurnInState.Talk;
        if (AtkUnitBase.IsAddonReady("SelectYesno"))
            return TurnInState.YesNo;
        if (AgentNpcTrade.IsActive)
        {
            var index = AgentNpcTrade.IndexOfRequest(itemId);
            if (index < 0 || AgentNpcTrade.IsSelecting)
                return TurnInState.RequestWait;
            if (AddonRequest.IsReadyToHandOver(index))
                return TurnInState.RequestHandOver;
            return AtkUnitBase.IsAddonReady("Request") ? TurnInState.RequestFill : TurnInState.RequestWait;
        }

        var cutscene = Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent];
        var occupied = cutscene || Service.Conditions[ConditionFlag.OccupiedInEvent] || Service.Conditions[ConditionFlag.OccupiedInQuestEvent] || Service.Conditions[ConditionFlag.Occupied];
        if (remaining > 0)
        {
            if (AgentSatisfactionSupply.IsOpenFor(npc))
                return TurnInState.SupplyMenu;
            if (AtkUnitBase.IsAddonReady("SelectString"))
                return TurnInState.NpcMenu;
            return occupied ? TurnInState.Busy : TurnInState.Idle;
        }

        if (AtkUnitBase.IsAddonReady("SelectString"))
            return TurnInState.CloseMenu;
        if (cutscene)
            return TurnInState.Busy;
        if (AgentSatisfactionSupply.IsVisible)
            return AgentSatisfactionSupply.IsInteractable ? TurnInState.CloseSupply : TurnInState.Busy;
        return occupied ? TurnInState.Busy : TurnInState.Finished;
    }

    protected static string ItemName(uint itemId) => Service.LuminaRow<Lumina.Excel.Sheets.Item>(itemId)?.Name.ToString() ?? itemId.ToString();
}
