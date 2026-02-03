using Dalamud.Plugin;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace Satisfy;

// execute full fishing delivery: teleport to zone, fish, turn in
// TODO: automate actual fishing, use autohook
public sealed class AutoFish(NPCInfo npc, IDalamudPluginInterface dalamud) : AutoCommon(dalamud)
{
    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(2);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.FishData == null || npc.CraftData == null)
            throw new Exception("Fish or turn-in data is not initialized");

        var turnInItemId = npc.FishData.FishItemId;
        var remainingFish = remainingTurnins - Game.NumItemsInInventory(turnInItemId, 1);
        if (remainingFish > 0)
        {
            Status = "Teleporting to fish zone";
            await TeleportTo(npc.FishData.TerritoryTypeId, npc.FishData.Center);

            // TODO: improve move-to destination (ideally closest point where you can actually fish...)
            if (npc.FishData.IsSpearFish)
                Status = $"Spearfishing at {Service.LuminaRow<SpearfishingNotebook>(npc.FishData.FishSpotId)?.PlaceName.ValueNullable?.Name}";
            else
                Status = $"Fishing at {Service.LuminaRow<FishingSpot>(npc.FishData.FishSpotId)?.PlaceName.ValueNullable?.Name}";
            await MoveTo(npc.FishData.Center, 10, true, true, true);
        }
        else // TODO: full auto...
        {
            Status = "Teleporting to turn-in zone";
            await TeleportTo(npc.TerritoryId, npc.CraftData.TurnInLocation);

            Status = $"Turning in {remainingTurnins}x {ItemName(turnInItemId)}";
            await MoveTo(npc.CraftData.TurnInLocation, 3);
            await TurnIn(npc, 2);
        }
    }
}
