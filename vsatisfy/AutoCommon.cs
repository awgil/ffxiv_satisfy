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

        Status = "Turning in";
        if (!Game.IsTurnInSupplyInProgress((uint)npc.Index + 1))
        {
            ErrorIf(!Game.InteractWith(npc.CraftData.TurnInInstanceId), "Failed to interact with turn-in NPC");
            if (Service.Objects.TryGetByGameObjectId(npc.CraftData.TurnInInstanceId, out var obj))
                await InteractWith(obj, selectStringIndex: 0, skip: UiSkipOptions.Talk);
            else
                Warning($"Unable to interact with {npc.Name}#{npc.CraftData.TurnInInstanceId}");
        }
        while (npc.RemainingTurnins(slot) > 0)
        {
            await WaitUntilSkipTalk(() => npc.RemainingTurnins(slot) <= 0 || Game.IsTurnInSupplyInProgress((uint)npc.Index + 1), "WaitDialog");
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
        await WaitUntilSkipTalk(() => Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneStart");
        await WaitUntilSkipTalk(() => !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneEnd");
    }

    // wait until condition is satisfied, skipping all talks as they appear
    protected async Task WaitUntilSkipTalk(Func<bool> condition, string scopeName)
    {
        using var scope = BeginScope(scopeName);
        while (!condition())
        {
            if (Game.IsTalkInProgress())
            {
                Log("progressing talk...");
                Game.ProgressTalk();
            }
            Log("waiting...");
            await NextFrame();
        }
    }

    protected static string ItemName(uint itemId) => Service.LuminaRow<Lumina.Excel.Sheets.Item>(itemId)?.Name.ToString() ?? itemId.ToString();
}
