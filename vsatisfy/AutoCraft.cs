﻿using Dalamud.Game.ClientState.Conditions;
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
        var remainingTurnins = npc.MaxDeliveries - npc.UsedDeliveries;
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.CraftData == null)
            throw new Exception("Craft data is not initialized");

        Status = "Teleporting to zone";
        await TeleportTo(npc.TerritoryId, npc.AetheryteId);

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
                await BuyFromShop(npc.CraftData.VendorInstanceId, npc.CraftData.VendorShopId, ingredient.id, missingIngredients);
            }
            Status = $"Crafting {remainingCrafts}x {ItemName(turnInItemId)}";
            await CraftItem(turnInItemId, remainingCrafts, remainingTurnins);
        }

        Status = $"Turning in {remainingTurnins}x {ItemName(turnInItemId)}";
        await MoveTo(npc.CraftData.TurnInLocation, 3);
        await TurnIn(npc.CraftData.TurnInInstanceId, remainingTurnins);
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

    // TODO: job selection...
    private async Task CraftItem(uint itemId, int count, int finalCount)
    {
        using var scope = BeginScope("Craft");
        var recipe = CraftTurnin.GetRecipeId(itemId);
        ErrorIf(recipe == 0, $"Failed to find recipe for {itemId}");

        ArtisanCraft((ushort)recipe, count);
        await WaitWhile(() => !ArtisanInProgress(), "WaitStart");
        await WaitWhile(ArtisanInProgress, "WaitProgress");
        await WaitWhile(() => !Service.Conditions[ConditionFlag.PreparingToCraft], "WaitFinish");
        ErrorIf(Game.NumItemsInInventory(itemId, 1) < finalCount, $"Failed to craft {count}x {ItemName(itemId)}");
        await NextFrame();

        Game.ExitCrafting();
        await WaitWhile(() => Service.Conditions[ConditionFlag.Crafting], "WaitCraftClose");
    }

    private async Task TurnIn(ulong npcInstanceId, int count)
    {
        using var scope = BeginScope("TurnIn");
        if (!Game.IsTurnInSupplyInProgress((uint)npc.Index + 1))
        {
            ErrorIf(!Game.InteractWith(npcInstanceId), "Failed to interact with turn-in NPC");
            await WaitUntilSkipTalk(Game.IsTurnInSelectInProgress, "WaitSelect");
            Game.SelectTurnIn();
        }
        for (int i = 0; i < count; ++i)
        {
            await WaitUntilSkipTalk(() => Game.IsTurnInSupplyInProgress((uint)npc.Index + 1), "WaitDialog");
            Game.TurnInSupply(0);
            await WaitWhile(() => !Game.IsTurnInRequestInProgress(npc.TurnInItems[0]), "WaitHandIn");
            Game.TurnInRequestCommit();
        }
        await WaitUntilSkipTalk(() => !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneStart");
        await WaitUntilSkipTalk(() => Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneEnd");
    }

    private void ArtisanCraft(ushort recipe, int count) => _artisanCraft.InvokeAction(recipe, count);
    private bool ArtisanInProgress() => _artisanInProgress.InvokeFunc();
}
