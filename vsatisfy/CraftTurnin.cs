using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Data.Files;
using Dalamud.Game.ClientState.Objects;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Linq.Expressions;
using FFXIVClientStructs.STD.Helper;

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

    // TODO: job selection
    // rather than job selection im using current job instead, setting default as CUL 
    public static uint GetRecipeId(uint craftedItemId)
    {
        var localplayer = Service.ClientState.LocalPlayer;
        var currentjob = Service.ClientState.LocalPlayer!.ClassJob.RowId;
        if (localplayer !=null)
        {
            switch (currentjob)
            {
                case 8:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.RowId ?? 0;
                case 9:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.BSM.RowId ?? 0;
                case 10:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.ARM.RowId ?? 0;
                case 11:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.GSM.RowId ?? 0;
                case 12:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.LTW.RowId ?? 0;
                case 13:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.WVR.RowId ?? 0;
                case 14:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.ALC.RowId ?? 0;
                case 15:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.CUL.RowId ?? 0;
                default:
                    return Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.RowId ?? 0;
            }
        }
        return Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.RowId ?? 0;
    }

    public static (uint id, int count) GetCraftIngredient(uint craftedItemId)
    {
        var localplayer = Service.ClientState.LocalPlayer;
        var recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.CUL.Value;
        if (localplayer != null)
        {
            var currentjob = Service.ClientState.LocalPlayer!.ClassJob.RowId;
            switch (currentjob)
            {
                case 8:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.Value;
                    break;
                case 9:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.BSM.Value;
                    break;
                case 10:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.ARM.Value;
                    break;
                case 11:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.GSM.Value;
                    break;
                case 12:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.LTW.Value;
                    break;
                case 13:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.WVR.Value;
                    break;
                case 14:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.ALC.Value;
                    break;
                case 15:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.CUL.Value;
                    break;
                default:
                    recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.Value;
                    break;
            }
            return recipe != null ? (recipe.Value.Ingredient[0].RowId, recipe.Value.AmountIngredient[0]) : default;
        }
        return recipe != null ? (recipe.Value.Ingredient[0].RowId, recipe.Value.AmountIngredient[0]) : default;
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
}
