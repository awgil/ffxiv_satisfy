using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.GeneratedSheets;
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
        var craftedItemId = Service.LuminaRow<SatisfactionSupply>(supplyId, 0)!.Item.Row;
        var ingredientId = GetCraftIngredient(craftedItemId).id;

        string scene = Service.LuminaRow<TerritoryType>(territoryId)!.Bg;
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

    public static (uint id, int count) GetCraftIngredient(uint craftedItemId)
    {
        var recipe = Service.LuminaRow<RecipeLookup>(craftedItemId)?.CRP.Value;
        return recipe != null ? ((uint)recipe.UnkData5[0].ItemIngredient, recipe.UnkData5[0].AmountIngredient) : default;
    }

    private static (uint shopId, int itemIndex) FindVendorItem(uint enpcId, uint itemId)
    {
        var enpcBase = Service.LuminaRow<ENpcBase>(enpcId);
        if (enpcBase == null)
            return (0, -1);

        foreach (var handler in enpcBase.ENpcData)
        {
            if ((handler >> 16) != (uint)EventHandlerType.Shop)
                continue;

            var numItems = Service.LuminaSheet<GilShopItem>()!.GetRowParser(handler)?.RowCount ?? 0;
            for (int i = 0; i < numItems; ++i)
            {
                var shopItem = Service.LuminaRow<GilShopItem>(handler, (uint)i);
                if (shopItem?.Item.Row == itemId)
                {
                    return (handler, i);
                }
            }
        }
        return (0, -1);
    }

    public static unsafe bool IsShopOpen(uint shopId = 0)
    {
        var agent = AgentShop.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->EventReceiver == null)
            return false;
        if (shopId == 0)
            return true; // some shop is open...
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
            return false;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        return proxy->Handler == eh->Value;
    }

    public static unsafe bool OpenShop(GameObject* vendor, uint shopId)
    {
        Service.Log.Debug($"Interacting with {(ulong)vendor->GetGameObjectId():X}");
        TargetSystem.Instance()->InteractWithObject(vendor);
        var selector = EventHandlerSelector.Instance();
        if (selector->Target == null)
            return true; // assume interaction was successful without selector

        if (selector->Target != vendor)
        {
            Service.Log.Error($"Unexpected selector target {(ulong)selector->Target->GetGameObjectId():X} when trying to interact with {(ulong)vendor->GetGameObjectId():X}");
            return false;
        }

        for (int i = 0; i < selector->OptionsCount; ++i)
        {
            if (selector->Options[i].Handler->Info.EventId.Id == shopId)
            {
                Service.Log.Debug($"Selecting selector option {i} for shop {shopId:X}");
                EventFramework.Instance()->InteractWithHandlerFromSelector(i);
                return true;
            }
        }

        Service.Log.Error($"Failed to find shop {shopId:X} in selector for {(ulong)vendor->GetGameObjectId():X}");
        return false;
    }

    public static unsafe bool CloseShop()
    {
        var agent = AgentShop.Instance();
        if (agent == null || agent->EventReceiver == null)
            return false;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        proxy->Handler->CancelInteraction();
        return true;
    }

    public static unsafe bool BuyItemFromShop(uint shopId, uint itemId, int count)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            Service.Log.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (eh->Value->Info.EventId.ContentId != EventHandlerType.Shop)
        {
            Service.Log.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        for (int i = 0; i < shop->VisibleItemsCount; ++i)
        {
            var index = shop->VisibleItems[i];
            if (shop->Items[index].ItemId == itemId)
            {
                Service.Log.Debug($"Buying {count}x {itemId} from {shopId:X}");
                shop->BuyItemIndex = index;
                shop->ExecuteBuy(count);
                return true;
            }
        }

        Service.Log.Error($"Did not find item {itemId} in shop {shopId:X}");
        return false;
    }
}
