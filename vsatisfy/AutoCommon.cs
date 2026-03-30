using Dalamud.Game.ClientState.Conditions;
using System.Threading.Tasks;
using clib.TaskSystem;

namespace Satisfy;

// common automation utilities
public abstract class AutoCommon : TaskBase
{
    protected async Task TurnIn(NPCInfo npc, int slot)
    {
        using var scope = BeginScope("TurnIn");
        if (npc.CraftData is null || npc.RemainingTurnins(slot) is 0) return;

        if (!Game.IsTurnInSupplyInProgress((uint)npc.Index + 1))
        {
            ErrorIf(!Game.InteractWith(npc.CraftData.TurnInInstanceId), "Failed to interact with turn-in NPC");
            await WaitUntilSkipTalk(Game.IsSelectStringAddonActive, "WaitSelect");
            Game.SelectTurnIn();
        }
        for (int i = 0; i < npc.RemainingTurnins(slot); ++i)
        {
            await WaitUntilSkipTalk(() => Game.IsTurnInSupplyInProgress((uint)npc.Index + 1), "WaitDialog");
            Game.TurnInSupply(slot);
            await WaitWhile(() => !Game.IsTurnInRequestInProgress(npc.TurnInItems[slot]), "WaitHandIn");
            Game.TurnInRequestCommit(slot);
        }
        await WaitUntilSkipTalk(() => !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneStart");
        await WaitUntilSkipTalk(() => Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneEnd");
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
