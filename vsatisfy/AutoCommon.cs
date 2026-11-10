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
        string? lastRequest = null;
        string? lastHandOverError = null;
        var remaining = npc.RemainingTurnins(slot);
        var interacts = 0;
        while (true)
        {
            var curRemaining = npc.RemainingTurnins(slot);
            if (curRemaining != remaining)
            {
                remaining = curRemaining;
                interacts = 0;
            }

            var next = GetTurnInState(npc, itemId, remaining);
            if (next != state)
            {
                Log($"turn-in state: {state} -> {next} ({remaining} left)");
                state = next;
                acted = false;
                lastHandOverError = null;
            }

            // just log everything at this point
            var request = Game.IsTurnInRequestActive() || AtkUnitBase.IsAddonReady("Request") ? Game.GetAgentLog() : null;
            if (request != lastRequest)
            {
                Log($"request: {request ?? "closed"}");
                lastRequest = request;
            }
            if (next == TurnInState.Finished)
                break;

            Status = $"Turning in {ItemName(itemId)} ({remaining} left): {next}";
            switch (next)
            {
                case TurnInState.Talk:
                    AddonTalk.Progress();
                    break;
                case TurnInState.YesNo:
                    AddonSelectYesno.Yes();
                    break;
                case TurnInState.NpcMenu:
                    AddonSelectString.Select(0);
                    break;
                case TurnInState.CloseMenu:
                    AtkUnitBase.CloseAddon("SelectString");
                    break;
                case TurnInState.RequestFill when !acted:
                    acted = true;
                    Log($"filling slot {Game.TurnInRequestIndex(itemId)}: {Game.GetAgentLog()}");
                    Log($"fill result: {Game.FillTurnInRequest(Game.TurnInRequestIndex(itemId))}, {Game.GetAgentLog()}");
                    break;
                case TurnInState.RequestHandOver when !acted:
                    var handOverError = Game.HandOverTurnInRequest();
                    acted = handOverError == null;
                    if (acted)
                        Log($"hand over clicked: {Game.GetAgentLog()}");
                    else if (handOverError != lastHandOverError)
                        Log($"hand over not clicked ({handOverError})");
                    lastHandOverError = handOverError;
                    break;
                case TurnInState.SupplyMenu when !acted:
                    acted = true;
                    Game.TurnInSupply(slot);
                    break;
                case TurnInState.CloseSupply when !acted:
                    acted = true;
                    Game.CloseTurnInSupply();
                    break;
                case TurnInState.Idle when !acted:
                    acted = true;
                    ErrorIf(++interacts > MaxAttempts, $"Talked to {npc.Name} {MaxAttempts} times without delivering anything");
                    ErrorIf(!Service.Objects.TryGetByGameObjectId(npc.CraftData.TurnInInstanceId, out var obj), $"Unable to find {npc.Name}#{npc.CraftData.TurnInInstanceId:X}");
                    await InteractWith(obj!);
                    break;
            }
            await NextFrame();
        }
    }

    private static TurnInState GetTurnInState(NPCInfo npc, uint itemId, int remaining)
    {
        if (AtkUnitBase.IsAddonReady("Talk"))
            return TurnInState.Talk;
        if (AtkUnitBase.IsAddonReady("SelectYesno"))
            return TurnInState.YesNo;
        if (Game.IsTurnInRequestActive())
        {
            if (Game.TurnInRequestIndex(itemId) < 0 || Game.IsTurnInRequestSelecting())
                return TurnInState.RequestWait;
            if (Game.IsTurnInRequestReadyToHandOver(Game.TurnInRequestIndex(itemId)))
                return TurnInState.RequestHandOver;
            return AtkUnitBase.IsAddonReady("Request") ? TurnInState.RequestFill : TurnInState.RequestWait;
        }

        var cutscene = Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent];
        var occupied = cutscene || Service.Conditions[ConditionFlag.OccupiedInEvent] || Service.Conditions[ConditionFlag.OccupiedInQuestEvent] || Service.Conditions[ConditionFlag.Occupied];
        if (remaining > 0)
        {
            if (Game.IsTurnInSupplyInProgress(npc))
                return TurnInState.SupplyMenu;
            if (AtkUnitBase.IsAddonReady("SelectString"))
                return TurnInState.NpcMenu;
            return occupied ? TurnInState.Busy : TurnInState.Idle;
        }

        if (AtkUnitBase.IsAddonReady("SelectString"))
            return TurnInState.CloseMenu;
        if (cutscene)
            return TurnInState.Busy;
        if (Game.IsTurnInSupplyVisible())
            return TurnInState.CloseSupply;
        return occupied ? TurnInState.Busy : TurnInState.Finished;
    }

    protected static string ItemName(uint itemId) => Service.LuminaRow<Lumina.Excel.Sheets.Item>(itemId)?.Name.ToString() ?? itemId.ToString();
}
