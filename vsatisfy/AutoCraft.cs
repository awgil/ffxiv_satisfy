using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

// execute full crafting delivery: teleport to zone, buy ingredients if needed, craft if needed, turn in
public sealed class AutoCraft(NPCInfo npc, IDalamudPluginInterface dalamud) : AutoCommon(dalamud)
{
    private readonly ICallGateSubscriber<ushort, int, object> _artisanCraft = dalamud.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<bool> _artisanInProgress = dalamud.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");

    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(0);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.CraftData == null)
            throw new Exception("Craft data is not initialized");

        Status = "Teleporting to zone";
        await TeleportTo(npc.TerritoryId, npc.CraftData.VendorLocation);

        var turnInItemId = npc.TurnInItems[0];
        var remainingCrafts = remainingTurnins - Game.NumItemsInInventory(turnInItemId, 1);
        if (remainingCrafts > 0)
        {
            var ingredient = CraftTurnin.GetCraftIngredient(turnInItemId);
            var requiredIngredients = ingredient.count * remainingCrafts;
            var missingIngredients = requiredIngredients - Game.NumItemsInInventory(ingredient.id, 0);
            if (missingIngredients > 0)
            {
                Status = $"Buying {missingIngredients}x {ItemName(ingredient.id)}";
                await MoveTo(npc.CraftData.VendorLocation, 3);
                await Dismount();
                await BuyFromShop(npc.CraftData.VendorInstanceId, npc.CraftData.VendorShopId, ingredient.id, missingIngredients);
            }
            Status = $"Crafting {remainingCrafts}x {ItemName(turnInItemId)}";
            await CraftItem(turnInItemId, remainingCrafts, remainingTurnins);
        }

        Status = $"Turning in {remainingTurnins}x {ItemName(turnInItemId)}";
        await MoveTo(npc.CraftData.TurnInLocation, 3);
        await TurnIn(npc.Index, npc.CraftData.TurnInInstanceId, npc.TurnInItems[0], 0, remainingTurnins);
    }

    private async Task BuyFromShop(ulong vendorInstanceId, uint shopId, uint itemId, int count)
    {
        using var scope = BeginScope("Buy");
        if (!Game.IsShopOpen(shopId))
        {
            Log("Opening shop...");
            ErrorIf(!Game.OpenShop(vendorInstanceId, shopId), $"Failed to open shop {vendorInstanceId:X}.{shopId:X}");
            await WaitWhile(() => !Game.IsShopOpen(shopId), "WaitForOpen");
            await WaitWhile(() => !Service.Conditions[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        }

        Log("Buying...");
        ErrorIf(!Game.BuyItemFromShop(shopId, itemId, count), $"Failed to buy {count}x {itemId} from shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.ShopTransactionInProgress(shopId), "Transaction");
        Log("Closing shop...");
        ErrorIf(!Game.CloseShop(), $"Failed to close shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.IsShopOpen(), "WaitForClose");
        await WaitWhile(() => Service.Conditions[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        await NextFrame();
    }

    private async Task CraftItem(uint itemId, int count, int finalCount)
    {
        using var scope = BeginScope("Craft");
        if (CraftTurnin.GetRecipe(itemId) is { RowId: var rowId } && rowId != 0)
        {

            ArtisanCraft((ushort)rowId, count);
            await WaitWhile(() => !ArtisanInProgress(), "WaitStart");
            await WaitWhile(ArtisanInProgress, "WaitProgress");
            await WaitWhile(() => !Service.Conditions[ConditionFlag.PreparingToCraft], "WaitFinish");
            ErrorIf(Game.NumItemsInInventory(itemId, 1) < finalCount, $"Failed to craft {count}x {ItemName(itemId)}");
            await NextFrame();

            Game.ExitCrafting();
            await WaitWhile(() => Service.Conditions[ConditionFlag.Crafting], "WaitCraftClose");
        }
        else
            Error($"Failed to find recipe for {itemId}");
    }

    private void ArtisanCraft(ushort recipe, int count) => _artisanCraft.InvokeAction(recipe, count);
    private bool ArtisanInProgress() => _artisanInProgress.InvokeFunc();
}
