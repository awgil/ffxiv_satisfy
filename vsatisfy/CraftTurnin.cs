using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Satisfy;

// data & functions needed to buy crafting ingredients, craft and turn-in
public sealed class CraftTurnin
{
    public ulong VendorInstanceId;
    public Vector3 VendorLocation;
    public uint VendorShopId;
    public ulong TurnInInstanceId;
    public Vector3 TurnInLocation;

    public CraftTurnin(uint supplyId, uint turnInENPCId, uint territoryId)
    {
        // note: we assume that first supply subrow for rank-one supply is always a craft (if this changes, we can check Slot column)
        // note: we assume that all ingredients are sold by the same vendor of the same shop in the same territory as turn-in npc
        var craftedItemId = Service.LuminaRow<SatisfactionSupply>(supplyId, 0)!.Value.Item.RowId;
        var ingredientId = GetCraftIngredient(craftedItemId).id;

        string scene = Service.LuminaRow<TerritoryType>(territoryId)!.Value.Bg.ToString();
        var filenameStart = scene.LastIndexOf('/') + 1;
        var planeventLayerGroup = "bg/" + scene[0..filenameStart] + "planevent.lgb";
        Service.Log.Debug($"Territory {territoryId} -> {planeventLayerGroup}");
        var lvb = Service.DataManager.GetFile<LgbFile>(planeventLayerGroup);
        if (lvb != null)
        {
            foreach (var layer in lvb.Layers)
            {
                foreach (var instance in layer.InstanceObjects)
                {
                    if (instance.AssetType != LayerEntryType.EventNPC)
                        continue;

                    var baseId = ((LayerCommon.ENPCInstanceObject)instance.Object).ParentData.ParentData.BaseId;
                    if (baseId == turnInENPCId)
                    {
                        TurnInInstanceId = (1ul << 32) | instance.InstanceId;
                        TurnInLocation = new(instance.Transform.Translation.X, instance.Transform.Translation.Y, instance.Transform.Translation.Z);
                        Service.Log.Debug($"Found turn-in npc {baseId} {instance.InstanceId} '{Service.LuminaRow<ENpcResident>(baseId)?.Singular}' at {TurnInLocation}");
                    }

                    var vendor = FindVendorItem(baseId, ingredientId);
                    if (vendor.itemIndex >= 0)
                    {
                        VendorInstanceId = (1ul << 32) | instance.InstanceId;
                        VendorLocation = new(instance.Transform.Translation.X, instance.Transform.Translation.Y, instance.Transform.Translation.Z);
                        VendorShopId = vendor.shopId;
                        Service.Log.Debug($"Found vendor npc {baseId} {instance.InstanceId} '{Service.LuminaRow<ENpcResident>(baseId)?.Singular}' at {VendorLocation}: shop {vendor.shopId} '{Service.LuminaRow<GilShop>(vendor.shopId)?.Name}' #{vendor.itemIndex}");
                    }
                }
            }
        }
    }

    public static Recipe? GetRecipe(uint craftedItemId)
    {
        return GetCrafter() switch
        {
            8 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.Value,
            9 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.BSM.Value,
            10 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.ARM.Value,
            11 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.GSM.Value,
            12 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.LTW.Value,
            13 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.WVR.Value,
            14 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.ALC.Value,
            15 => Service.LuminaRow<RecipeLookup>(craftedItemId)?.CUL.Value,
            _ => Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.Value,
        };
    }

    public static (uint id, int count) GetCraftIngredient(uint craftedItemId)
    {
        if (GetRecipe(craftedItemId) is { } recipe)
            return (recipe.Ingredient[0].RowId, recipe.AmountIngredient[0]);
        return default;
    }

    private static (uint shopId, int itemIndex) FindVendorItem(uint enpcId, uint itemId)
    {
        var enpcBase = Service.LuminaRow<ENpcBase>(enpcId);
        if (enpcBase == null)
            return (0, -1);

        foreach (var handler in enpcBase.Value.ENpcData)
        {
            if ((handler.RowId >> 16) != (uint)EventHandlerType.Shop)
                continue;

            var items = Service.LuminaSubrows<GilShopItem>(handler.RowId);
            if (items == null)
                continue;

            for (int i = 0; i < items.Value.Count; ++i)
            {
                var shopItem = items.Value[i];
                if (shopItem.Item.RowId == itemId)
                {
                    return (handler.RowId, i);
                }
            }
        }
        return (0, -1);
    }

    private static unsafe uint GetCrafter()
    {
        return Plugin.Config.CraftJobType switch
        {
            Config.JobChoice.Specific => Plugin.Config.SelectedCraftJob,
            Config.JobChoice.Current => Service.LuminaRow<ClassJob>(Service.ClientState.LocalPlayer?.ClassJob.RowId ?? 0)?.RowId ?? Plugin.Config.SelectedCraftJob,
            Config.JobChoice.LowestXP => Service.LuminaSheet<ClassJob>()?.Where(c => c.ClassJobCategory.RowId == 33).OrderBy(c => PlayerState.Instance()->ClassJobLevels[c.ExpArrayIndex]).First().RowId ?? Plugin.Config.SelectedCraftJob,
            Config.JobChoice.HighestXP => Service.LuminaSheet<ClassJob>()?.Where(c => c.ClassJobCategory.RowId == 33).OrderByDescending(c => PlayerState.Instance()->ClassJobLevels[c.ExpArrayIndex]).First().RowId ?? Plugin.Config.SelectedCraftJob,
            _ => Plugin.Config.SelectedCraftJob,
        };
    }
}
