using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Satisfy;

public unsafe class MainWindow : Window, IDisposable
{
    private readonly IDalamudPluginInterface _dalamud;
    private readonly Config _config;
    private readonly Achievements _achi = new();
    private readonly Automation _auto = new();
    private readonly List<NPCInfo> _npcs = [];
    private readonly List<(uint Currency, int Amount, int Count)> _rewards = [];
    private bool _wasLoaded;

    public MainWindow(IDalamudPluginInterface dalamud, Config config) : base("Satisfier")
    {
        _dalamud = dalamud;
        _config = config;
        _achi.AchievementProgress += OnAchievementProgress;

        TitleBarButtons.Add(new() { Icon = FontAwesomeIcon.Cog, IconOffset = new(1), Click = _ => ImGui.OpenPopup("###config") });

        var inst = SatisfactionSupplyManager.Instance();
        var npcSheet = Service.LuminaSheet<SatisfactionNpc>()!;
        if (inst->Satisfaction.Length + 1 != npcSheet.Count)
        {
            Service.Log.Error($"Npc count mismatch between CS ({inst->Satisfaction.Length}) and lumina ({npcSheet.Count - 1})");
            return;
        }

        for (int i = 0; i < inst->SatisfactionRanks.Length; ++i)
        {
            var npcData = npcSheet.GetRow((uint)(i + 1));
            if (npcData.Npc.RowId != 0)
                _npcs.Add(new(i, npcData.Npc.RowId, npcData.Npc.Value.Singular.ToString(), npcData.DeliveriesPerWeek, [.. npcData.SatisfactionNpcParams.Select(p => p.SupplyIndex)]));
        }

        // hardcoded stuff
        _npcs[0].InitHardcodedData(1784, 478);
        _npcs[1].InitHardcodedData(1979, 635);
        _npcs[2].InitHardcodedData(2077, 613);
        _npcs[3].InitHardcodedData(2193, 478);
        _npcs[4].InitHardcodedData(2435, 820);
        _npcs[5].InitHardcodedData(2633, 886);
        _npcs[6].InitHardcodedData(2845, 886);
        _npcs[7].InitHardcodedData(3069, 962);
        _npcs[8].InitHardcodedData(3173, 816);
        _npcs[9].InitHardcodedData(3361, 956);
        _npcs[10].InitHardcodedData(3602, 1190);
    }

    public void Dispose()
    {
        _auto.Dispose();

        _achi.AchievementProgress -= OnAchievementProgress;
        _achi.Dispose();
    }

    public override void PreOpenCheck()
    {
        var isLoaded = UIState.Instance()->PlayerState.IsLoaded != 0;
        if (_wasLoaded == isLoaded)
            return;

        if (isLoaded)
        {
            IsOpen = _config.AutoShowIfIncomplete && SatisfactionSupplyManager.Instance()->GetRemainingAllowances() > 0 && _npcs.Any(n => n.Unlocked);
        }
        else
        {
            foreach (var npc in _npcs)
                npc.AchievementStart = npc.AchievementMax = 0;
            IsOpen = false;
        }
        _wasLoaded = isLoaded;
    }

    public override void Draw()
    {
        DrawConfig();
        if (_wasLoaded)
        {
            UpdateData();
            DrawMainTable();
            DrawCurrenciesTable();
        }

        if (_config.ShowDebugUI && ImGui.CollapsingHeader("Debug data"))
            DrawDebug();
    }

    private void DrawConfig()
    {
        using var popup = ImRaii.Popup("###config");
        if (popup)
            _config.Draw();
    }

    private void UpdateData()
    {
        _rewards.Clear();

        var inst = SatisfactionSupplyManager.Instance();
        var bonusOverrideRow = inst->BonusGuaranteeRowId != 0xFF ? inst->BonusGuaranteeRowId : Calculations.CalculateBonusGuarantee();
        var bonusOverride = bonusOverrideRow >= 0 ? Service.LuminaRow<SatisfactionBonusGuarantee>((uint)bonusOverrideRow) : null;
        var supplySheet = Service.LuminaSheetSubrow<SatisfactionSupply>()!;
        foreach (var npc in _npcs)
        {
            var sheetIndex = (byte)(npc.Index + 1);
            if (Service.LuminaRow<SatisfactionNpc>((uint)npc.Index) is { QuestRequired.RowId: var questId })
                npc.Unlocked = QuestManager.IsQuestComplete(questId);
            npc.Rank = inst->SatisfactionRanks[npc.Index];
            npc.SatisfactionCur = inst->Satisfaction[npc.Index];
            npc.SatisfactionMax = Service.LuminaRow<SatisfactionNpc>(sheetIndex)!.Value.SatisfactionNpcParams[npc.Rank].SatisfactionRequired;
            npc.UsedDeliveries = inst->UsedAllowances[npc.Index];
            npc.Requests = npc.Rank > 0 ? Calculations.CalculateRequestedItems(npc.SupplyIndex, inst->SupplySeed) : [];
            Array.Fill(npc.IsBonusOverride, false);
            if (npc.Rank == 5 && bonusOverride != null)
            {
                npc.IsBonusOverride[0] = bonusOverride.Value.BonusDoH.Contains(sheetIndex);
                npc.IsBonusOverride[1] = bonusOverride.Value.BonusDoL.Contains(sheetIndex);
                npc.IsBonusOverride[2] = bonusOverride.Value.BonusFisher.Contains(sheetIndex);
            }
            var supplyRows = supplySheet.GetRow(npc.SupplyIndex);
            for (int i = 0; i < npc.Requests.Length; ++i)
            {
                npc.EffectiveRequests[i] = npc.Requests[i];
                var supply = supplyRows[(int)npc.Requests[i]];
                if (npc.IsBonusOverride[i] && !supply.IsBonus)
                {
                    for (ushort j = 0; j < supplyRows.Count; ++j)
                    {
                        var supplyOverride = supplyRows[j];
                        if (supplyOverride.Slot == supply.Slot && supplyOverride.IsBonus)
                        {
                            supply = supplyOverride;
                            npc.EffectiveRequests[i] = j;
                            break;
                        }
                    }
                }
                npc.IsBonusEffective[i] = npc.IsBonusOverride[i] || supply.IsBonus;
                npc.Rewards[i] = supply.Reward.RowId;
                npc.TurnInItems[i] = supply.Item.RowId;

                var reward = Service.LuminaRow<SatisfactionSupplyReward>(npc.Rewards[i])!.Value;
                AddPotentialReward(reward.SatisfactionSupplyRewardData[0].RewardCurrency, reward.SatisfactionSupplyRewardData[0].QuantityHigh * reward.BonusMultiplier / 100, npc.MaxDeliveries - npc.UsedDeliveries);
                AddPotentialReward(reward.SatisfactionSupplyRewardData[1].RewardCurrency, reward.SatisfactionSupplyRewardData[1].QuantityHigh * reward.BonusMultiplier / 100, npc.MaxDeliveries - npc.UsedDeliveries); // todo: don't add at low level?..
            }

            if (npc.FishData == null || npc.FishData.FishItemId != npc.TurnInItems[2])
            {
                npc.FishData = new(npc.TurnInItems[2]);
            }

            if (npc.AchievementMax == 0 && npc.AchievementId != 0 && _config.AutoFetchAchievements)
            {
                _achi.Request(npc.AchievementId);
            }
        }

        _rewards.Sort((l, r) => (l.Currency, -l.Amount).CompareTo((r.Currency, -r.Amount)));

        var rewardSpan = CollectionsMarshal.AsSpan(_rewards);
        if (rewardSpan.Length > 0)
        {
            var remainingAllowances = inst->GetRemainingAllowances();
            int mergeDest = 0;
            rewardSpan[0].Count = Math.Min(rewardSpan[0].Count, remainingAllowances);
            for (int i = 1; i < _rewards.Count; ++i)
            {
                if (rewardSpan[i].Currency == rewardSpan[mergeDest].Currency)
                {
                    rewardSpan[mergeDest].Count = Math.Min(remainingAllowances, rewardSpan[mergeDest].Count + rewardSpan[i].Count);
                }
                else
                {
                    rewardSpan[i].Count = Math.Min(rewardSpan[i].Count, remainingAllowances);
                    rewardSpan[++mergeDest] = rewardSpan[i];
                }
            }
            ++mergeDest;
            _rewards.RemoveRange(mergeDest, _rewards.Count - mergeDest);
        }
    }

    private void AddPotentialReward(uint currency, int amount, int count)
    {
        var index = _rewards.FindIndex(e => e.Currency == currency && e.Amount == amount);
        if (index < 0)
            _rewards.Add((currency, amount, count));
        else
            CollectionsMarshal.AsSpan(_rewards)[index].Count += count;
    }

    private void DrawMainTable()
    {
        using (ImRaii.Disabled(!_auto.Running))
            if (ImGui.Button("Stop current task"))
                _auto.Stop();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Status: {_auto.CurrentTask?.Status ?? "idle"}");

        using var table = ImRaii.Table("main_table", 5);
        if (!table)
            return;
        ImGui.TableSetupColumn("NPC", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Bonuses", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Deliveries", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Achievement", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Actions");
        ImGui.TableHeadersRow();
        foreach (var npc in _npcs)
        {
            using var id = ImRaii.PushId(npc.Index);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"[{npc.Index}] {npc.Name}");

            ImGui.TableNextColumn();
            using (ImRaii.Disabled())
            {
                ImGui.Checkbox("###bonus_doh", ref npc.IsBonusEffective[0]);
                ImGui.SameLine();
                ImGui.Checkbox("###bonus_dol", ref npc.IsBonusEffective[1]);
                ImGui.SameLine();
                ImGui.Checkbox("###bonus_fsh", ref npc.IsBonusEffective[2]);
            }

            ImGui.TableNextColumn();
            ImGui.ProgressBar((float)npc.UsedDeliveries / npc.MaxDeliveries, new(120, 0), $"{npc.UsedDeliveries} / {npc.MaxDeliveries}");

            ImGui.TableNextColumn();
            if (npc.AchievementMax > 0)
                ImGui.ProgressBar((float)npc.AchievementCur / npc.AchievementMax, new(120, 0), $"{npc.AchievementCur} / {npc.AchievementMax}");
            else if (npc.AchievementId != 0 && !_config.AutoFetchAchievements && ImGui.Button("Fetch...", new(120, 0)))
                _achi.Request(npc.AchievementId);

            ImGui.TableNextColumn();
            DrawActions(npc);
        }
    }

    private void DrawCurrenciesTable()
    {
        using var table = ImRaii.Table("currencies_table", 4);
        if (!table)
            return;
        ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthFixed, 180);
        ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Max gain", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Overcap", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();
        var cm = CurrencyManager.Instance();
        foreach (var reward in _rewards)
        {
            var currItemId = cm->GetItemIdBySpecialId((byte)reward.Currency);
            var count = cm->GetItemCount(currItemId);
            var max = cm->GetItemMaxCount(currItemId);
            var gain = reward.Amount * reward.Count;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"[{reward.Currency}] {Service.LuminaRow<Item>(currItemId)?.Name}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{count}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{gain}");

            var overcap = count + gain - max;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(overcap > 0 ? overcap.ToString() : "---");
        }
    }

    private void DrawDebug()
    {
        if (ImGui.Button("Reset achievement data"))
            foreach (var npc in _npcs)
                npc.AchievementStart = npc.AchievementMax = 0;

        var inst = SatisfactionSupplyManager.Instance();
        var supplySheet = Service.LuminaSheetSubrow<SatisfactionSupply>()!;
        var calcBonus = Calculations.CalculateBonusGuarantee();
        var bonusOverrideRow = inst->BonusGuaranteeRowId != 0xFF ? inst->BonusGuaranteeRowId : calcBonus;
        var bonusOverride = bonusOverrideRow >= 0 ? Service.LuminaRow<SatisfactionBonusGuarantee>((uint)bonusOverrideRow) : null;

        ImGui.TextUnformatted($"Seed: {inst->SupplySeed}, fixed-rng={inst->FixedRandom}");
        ImGui.TextUnformatted($"Guarantee row: {inst->BonusGuaranteeRowId}, adj={inst->TimeAdjustmentForBonusGuarantee}, calculated={calcBonus}");
        foreach (var npc in _npcs)
        {
            var supplyRows = supplySheet.GetRow(npc.SupplyIndex);
            ImGui.TextUnformatted($"#{npc.Index}: rank={npc.Rank}, supply={npc.SupplyIndex} ({supplyRows.Count} subrows), satisfaction={npc.SatisfactionCur}/{npc.SatisfactionMax}, usedAllowances={npc.UsedDeliveries}");
            for (int i = 0; i < npc.Requests.Length; ++i)
            {
                var item = supplyRows[(int)npc.Requests[i]].Item;
                ImGui.TextUnformatted($"- {npc.Requests[i]} '{item.Value.Name}'{(npc.IsBonusOverride[i] ? " *****" : "")}");
            }
            string locationString(uint territory, Vector3 pos)
            {
                var aetheryte = Map.FindClosestAetheryte(territory, pos);
                var aetheryteRow = Service.LuminaRow<Aetheryte>(aetheryte)!.Value;
                var aetheryteName = aetheryteRow.AethernetName.RowId != 0 ? aetheryteRow.AethernetName.Value : aetheryteRow.PlaceName.Value;
                return $"{territory} '{Service.LuminaRow<TerritoryType>(territory)!.Value.Name}' {pos} near {aetheryte} '{aetheryteName.Name}'";
            }
            if (npc.CraftData != null)
            {
                ImGui.TextUnformatted($"> buy from {npc.CraftData.VendorInstanceId:X}/{npc.CraftData.VendorShopId} @ {locationString(npc.TerritoryId, npc.CraftData.VendorLocation)}");
                ImGui.TextUnformatted($"> turnin to {npc.CraftData.TurnInInstanceId:X} @ {locationString(npc.TerritoryId, npc.CraftData.VendorLocation)}");
            }
            if (npc.FishData != null)
            {
                if (npc.FishData.IsSpearFish)
                    ImGui.TextUnformatted($"> spearfish from {npc.FishData.FishSpotId} '{Service.LuminaRow<SpearfishingNotebook>(npc.FishData.FishSpotId)?.PlaceName.ValueNullable?.Name}' @ {locationString(npc.FishData.TerritoryTypeId, npc.FishData.Center)}");
                else
                    ImGui.TextUnformatted($"> fish from {npc.FishData.FishSpotId} '{Service.LuminaRow<FishingSpot>(npc.FishData.FishSpotId)?.PlaceName.ValueNullable?.Name}' @ {locationString(npc.FishData.TerritoryTypeId, npc.FishData.Center)}");
            }
        }
        ImGui.TextUnformatted($"Current NPC: {inst->CurrentNpc}, supply={inst->CurrentSupplyRowId}");

        var ui = UIState.Instance();
        ImGui.TextUnformatted($"Player loaded: {ui->PlayerState.IsLoaded}");
        ImGui.TextUnformatted($"Achievement state: complete={ui->Achievement.State}, progress={ui->Achievement.ProgressRequestState}");

        var agentSat = AgentSatisfactionSupply.Instance();
        var addonSat = Game.GetFocusedAddonByID(agentSat->AddonId);
        ImGui.TextUnformatted($"AgentSat: {agentSat->IsAgentActive()}/{(addonSat != null ? addonSat->IsVisible : null)}, id={agentSat->NpcInfo.Id}");

        var agentReq = AgentRequest.Instance();
        ImGui.TextUnformatted($"NPCTrade: {ui->NpcTrade.Requests.Count}");
        for (int i = 0; i < ui->NpcTrade.Requests.Count; ++i)
            ImGui.TextUnformatted($"[{i}] = {ui->NpcTrade.Requests.Items[i].ItemId} '{ui->NpcTrade.Requests.Items[i].ItemName}'");
        ImGui.TextUnformatted($"AgentReq: {agentReq->IsAgentActive()}, slot={agentReq->SelectedTurnInSlot}, opt={agentReq->SelectedTurnInSlotItemOptions}");

        var target = TargetSystem.Instance()->Target;
        if (target != null)
        {
            Span<nint> ptrs = stackalloc nint[0x20];
            var handlers = (FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler**)ptrs.GetPointer(0);
            var numHandlers = target->GetEventHandlersImpl(handlers);
            for (int i = 0; i < numHandlers; ++i)
            {
                ImGui.TextUnformatted($"eh{i}: {handlers[i]->Info.EventId.Id:X}");
            }
        }
    }

    private void DrawActions(NPCInfo npc)
    {
        var remainingTurnins = npc.MaxDeliveries - npc.UsedDeliveries;
        if (remainingTurnins <= 0)
            return;

        if (ImGui.Button("Auto craft turnin"))
            _auto.Start(new AutoCraft(npc, _dalamud));
        ImGui.SameLine();
        if (ImGui.Button("Auto fish turnin"))
            _auto.Start(new AutoFish(npc, _dalamud));
    }

    private void OnAchievementProgress(uint id, uint current, uint max)
    {
        var npc = _npcs.FirstOrDefault(npc => npc.AchievementId == id);
        if (npc != null)
        {
            npc.AchievementStart = current - (uint)npc.UsedDeliveries;
            npc.AchievementMax = max;
        }
    }
}
