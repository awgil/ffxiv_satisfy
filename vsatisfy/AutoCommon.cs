using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using System.Threading.Tasks;

namespace Satisfy;

// common automation utilities
public abstract class AutoCommon : TaskBase
{
    protected async Task TurnIn(NPCInfo npc, int slot)
    {
        using var scope = BeginScope("TurnIn");
        if (npc.CraftData is null || npc.RemainingTurnins(slot) is 0) return;

        if (!Game.IsTurnInSupplyInProgress(npc))
        {
            if (Service.Objects.TryGetByGameObjectId(npc.CraftData.TurnInInstanceId, out var obj))
                await InteractWith(obj, waitUntil: () => Game.IsTurnInSupplyInProgress(npc), selectStringIndex: 0, skip: UiSkipOptions.Talk);
            else
                Warning($"Unable to interact with {npc.Name}#{npc.CraftData.TurnInInstanceId}");
        }
        ErrorIf(!Game.IsTurnInSupplyInProgress(npc), "");
        while (npc.RemainingTurnins(slot) > 0)
        {
            Status = "Turning in";
            await WaitUntilSkipping(() => npc.RemainingTurnins(slot) <= 0 || Game.IsTurnInSupplyInProgress(npc), "WaitDialog", UiSkipOptions.Talk);
            if (npc.RemainingTurnins(slot) <= 0)
                break;

            Game.TurnInSupply(slot);
            await WaitWhile(() => npc.RemainingTurnins(slot) > 0 && !Game.IsTurnInRequestInProgress(npc.TurnInItems[slot]), "WaitHandIn");
            if (Game.IsTurnInRequestInProgress(npc.TurnInItems[slot]))
                Game.TurnInRequestCommit(slot);
        }

        await WaitForCutscene();
    }

    protected async Task WaitForCutscene()
    {
        using var scope = BeginScope(nameof(WaitForCutscene));
        Status = "Wait for CS";
        await WaitUntilSkipping(() => Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneStart", UiSkipOptions.Talk);
        await WaitUntilSkipping(() => !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneEnd", UiSkipOptions.Talk);
    }

    protected static string ItemName(uint itemId) => Service.LuminaRow<Lumina.Excel.Sheets.Item>(itemId)?.Name.ToString() ?? itemId.ToString();
}
