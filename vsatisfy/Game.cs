using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using System.Numerics;

namespace Satisfy;

// utilities for interacting with game
public static unsafe class Game
{
    public static bool ExecuteTeleport(uint aetheryteId) => UIState.Instance()->Telepo.Teleport(aetheryteId, 0);

    public static bool InteractWith(ulong instanceId)
    {
        var obj = GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(instanceId);
        if (obj == null)
            return false;
        TargetSystem.Instance()->InteractWithObject(obj, false);
        return true;
    }

    public static void TeleportToAethernet(uint currentAetheryte, uint destinationAetheryte)
    {
        Span<uint> payload = [4, destinationAetheryte];
        PacketDispatcher.SendEventCompletePacket(0x50000 | currentAetheryte, 0, 0, payload.GetPointer(0), (byte)payload.Length, null);
    }

    public static void TeleportToFirmament(uint currentAetheryte)
    {
        Span<uint> payload = [9];
        PacketDispatcher.SendEventCompletePacket(0x50000 | currentAetheryte, 0, 0, payload.GetPointer(0), (byte)payload.Length, null);
    }

    public static GameObject* Player() => GameObjectManager.Instance()->Objects.IndexSorted[0].Value;

    public static Vector3 PlayerPosition()
    {
        var player = Player();
        return player != null ? player->Position : default;
    }

    public static bool PlayerInRange(Vector3 dest, float dist)
    {
        var d = dest - PlayerPosition();
        return d.X * d.X + d.Z * d.Z <= dist * dist;
    }

    public static bool PlayerIsBusy() => Service.Conditions[ConditionFlag.BetweenAreas] || Service.Conditions[ConditionFlag.Casting] || ActionManager.Instance()->AnimationLock > 0;

    public static bool UseAction(ActionType type, uint actionId) => ActionManager.Instance()->UseAction(type, actionId);

    public static uint CurrentTerritory() => GameMain.Instance()->CurrentTerritoryTypeId;

    public static (ulong id, Vector3 pos) FindAetheryte(uint id)
    {
        foreach (var obj in GameObjectManager.Instance()->Objects.IndexSorted)
            if (obj.Value != null && obj.Value->ObjectKind == ObjectKind.Aetheryte && obj.Value->BaseId == id)
                return (obj.Value->GetGameObjectId(), *obj.Value->GetPosition());
        return (0, default);
    }

    // TODO: collectibility threshold
    public static int NumItemsInInventory(uint itemId, short minCollectibility) => InventoryManager.Instance()->GetInventoryItemCount(itemId, false, false, false, minCollectibility);

    public static AtkUnitBase* GetFocusedAddonByID(uint id)
    {
        var unitManager = &AtkStage.Instance()->RaptureAtkUnitManager->AtkUnitManager.FocusedUnitsList;
        foreach (var j in Enumerable.Range(0, Math.Min(unitManager->Count, unitManager->Entries.Length)))
        {
            var unitBase = unitManager->Entries[j].Value;
            if (unitBase != null && unitBase->Id == id)
            {
                return unitBase;
            }
        }
        return null;
    }

    public static bool IsSelectStringAddonActive()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("SelectString");
        return addon != null && addon->IsVisible && addon->IsReady;
    }

    public static bool IsShopOpen(uint shopId = 0)
    {
        var agent = AgentShop.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->EventReceiver == null || !agent->IsAddonReady())
            return false;
        if (shopId == 0)
            return true; // some shop is open...
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
            return false;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        return proxy->Handler == eh->Value;
    }

    public static bool OpenShop(GameObject* vendor, uint shopId)
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

    public static bool OpenShop(ulong vendorInstanceId, uint shopId)
    {
        var vendor = GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(vendorInstanceId);
        if (vendor == null)
        {
            Service.Log.Error($"Failed to find vendor {vendorInstanceId:X}");
            return false;
        }
        return OpenShop(vendor, shopId);
    }

    public static bool CloseShop()
    {
        var agent = AgentShop.Instance();
        if (agent == null || agent->EventReceiver == null)
            return false;
        AtkValue res = default, arg = default;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        proxy->Handler->CancelInteraction();
        arg.SetInt(-1);
        agent->ReceiveEvent(&res, &arg, 1, 0);
        return true;
    }

    public static bool BuyItemFromShop(uint shopId, uint itemId, int count)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            Service.Log.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (eh->Value->Info.EventId.ContentId != EventHandlerContent.Shop)
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

    public static bool ShopTransactionInProgress(uint shopId)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            Service.Log.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (eh->Value->Info.EventId.ContentId != EventHandlerContent.Shop)
        {
            Service.Log.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        return shop->WaitingForTransactionToFinish;
    }

    public static void ExitCrafting()
    {
        //AtkValue res = default, param = default;
        //param.SetInt(-1);
        //AgentRecipeNote.Instance()->ReceiveEvent(&res, &param, 1, 0);
        AgentRecipeNote.Instance()->Hide();
    }

    public static bool IsTalkInProgress()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("Talk");
        return addon != null && addon->IsVisible && addon->IsReady;
    }

    public static void ProgressTalk()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("Talk");
        if (addon != null && addon->IsReady)
        {
            var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
            var data = new AtkEventData();
            addon->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
        }
    }

    // TODO: this really needs revision...
    public static void SelectTurnIn()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("SelectString");
        if (addon != null && addon->IsReady)
        {
            AtkValue val = default;
            val.SetInt(0);
            addon->FireCallback(1, &val, true);
        }
    }

    public static bool IsTurnInSupplyInProgress(uint npcIndex)
    {
        var agent = AgentSatisfactionSupply.Instance();
        var addon = GetFocusedAddonByID(agent->AddonId);
        return agent->IsAgentActive() && agent->NpcInfo.Id == npcIndex && agent->NpcInfo.Valid && agent->NpcInfo.Initialized && addon != null && addon->IsVisible;
    }

    public static void TurnInSupply(int slot)
    {
        var agent = AgentSatisfactionSupply.Instance();
        var res = new AtkValue();
        Span<AtkValue> values = stackalloc AtkValue[2];
        values[0].SetInt(1);
        values[1].SetInt(slot);
        agent->ReceiveEvent(&res, values.GetPointer(0), 2, 0);
    }

    public static bool IsTurnInRequestInProgress(uint itemId)
    {
        var ui = UIState.Instance();
        var agent = AgentNpcTrade.Instance();
        return agent->IsAgentActive() && ui->NpcTrade.Requests.Count == 1 && ui->NpcTrade.Requests.Items[0].ItemId == itemId;
    }

    public static void TurnInRequestCommit()
    {
        var agent = AgentNpcTrade.Instance();
        if (!agent->IsAgentActive())
        {
            Service.Log.Error("Agent not active...");
            return;
        }

        if (agent->SelectedTurnInSlot >= 0)
        {
            Service.Log.Error($"Turn-in already in progress for slot {agent->SelectedTurnInSlot}");
            return;
        }

        var res = new AtkValue();
        Span<AtkValue> param = stackalloc AtkValue[4];
        param[0].SetInt(2); // start turnin
        param[1].SetInt(0); // slot
        param[2].SetInt(0); // ???
        param[3].SetInt(0); // ???
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);

        if (agent->SelectedTurnInSlot != 0 || agent->SelectedTurnInSlotItemOptions <= 0)
        {
            Service.Log.Error($"Failed to start turn-in: cur slot={agent->SelectedTurnInSlot}, count={agent->SelectedTurnInSlotItemOptions}");
            return;
        }

        param[0].SetInt(0); // confirm
        param[1].SetInt(0); // option #0
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 1);

        if (agent->SelectedTurnInSlot >= 0)
        {
            Service.Log.Error($"Turn-in not confirmed: cur slot={agent->SelectedTurnInSlot}");
            return;
        }

        // commit
        var addonId = agent->AddonId;
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);
        var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)addonId);
        if (addon != null && addon->IsVisible)
            addon->Close(false);
    }

    public static bool IsTerritoryLoaded() => GameMain.Instance()->TerritoryLoadState == 2;

    public static bool IsCastingTeleport()
    {
        var info = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)Service.Objects.LocalPlayer!.Address)->GetCastInfo();
        return info is not null && info->IsCasting && info->ActionType == ActionType.Action && info->ActionId == 5;
    }

    public static bool Interactable() => Service.Objects.LocalPlayer?.IsTargetable ?? false;
}
