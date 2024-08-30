using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System.Runtime.InteropServices;

namespace Satisfy;

public unsafe class MainWindow : Window, IDisposable
{
    public record class NPCInfo(int Index, string Name, int MaxDeliveries, int[] SupplyIndices)
    {
        public readonly int[] SupplyIndices = [.. SupplyIndices];
        public int Rank;
        public int UsedDeliveries;
        public uint[] Requests = [];
        public bool[] IsBonusOverride = [false, false, false];
        public bool[] IsBonusEffective = [false, false, false];
        public uint[] Rewards = [0, 0, 0];
        public uint AchievementId;
        public uint AchievementCur;
        public uint AchievementMax;
        public uint AetheryteId; // aetheryte closest to npc & vendor

        public uint SupplyIndex => (uint)SupplyIndices[Rank];

        public void InitHardcodedData(uint achievementId, uint aetheryteId)
        {
            AchievementId = achievementId;
            AetheryteId = aetheryteId;
        }
    }

    private readonly Achievements _achi = new();
    private readonly List<NPCInfo> _npcs = [];
    private readonly List<(uint Currency, int Amount, int Count)> _rewards = [];
    private bool _wasLoaded;

    public MainWindow() : base("Satisfier")
    {
        _achi.AchievementProgress += OnAchievementProgress;

        var inst = SatisfactionSupplyManager.Instance();
        var npcSheet = Service.LuminaSheet<SatisfactionNpc>()!;
        if (inst->Satisfaction.Length + 1 != npcSheet.RowCount)
        {
            Service.Log.Error($"Npc count mismatch between CS ({inst->Satisfaction.Length}) and lumina ({npcSheet.RowCount - 1})");
            return;
        }

        for (int i = 0; i < inst->SatisfactionRanks.Length; ++i)
        {
            var npcData = npcSheet.GetRow((uint)(i + 1))!;
            _npcs.Add(new(i, npcData.Npc.Value!.Singular, npcData.DeliveriesPerWeek, npcData.SupplyIndex));
        }

        // hardcoded stuff
        _npcs[0].InitHardcodedData(1784, 75);
        _npcs[1].InitHardcodedData(1979, 104);
        _npcs[2].InitHardcodedData(2077, 105);
        _npcs[3].InitHardcodedData(2193, 75);
        _npcs[4].InitHardcodedData(2435, 134);
        _npcs[5].InitHardcodedData(2633, 70);
        _npcs[6].InitHardcodedData(2845, 70);
        _npcs[7].InitHardcodedData(3069, 182);
        _npcs[8].InitHardcodedData(3173, 144);
        _npcs[9].InitHardcodedData(3361, 167);
    }

    public void Dispose()
    {
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
            IsOpen = SatisfactionSupplyManager.Instance()->GetRemainingAllowances() > 0;
        }
        else
        {
            foreach (var npc in _npcs)
                npc.AchievementCur = npc.AchievementMax = 0;
            IsOpen = false;
        }
        _wasLoaded = isLoaded;
    }

    public override void Draw()
    {
        if (_wasLoaded)
        {
            UpdateData();
            DrawMainTable();
            DrawCurrenciesTable();
        }

        if (ImGui.CollapsingHeader("Debug data"))
            DrawDebug();
    }

    private void UpdateData()
    {
        _rewards.Clear();

        var inst = SatisfactionSupplyManager.Instance();
        var bonusOverrideRow = inst->BonusGuaranteeRowId != 0xFF ? inst->BonusGuaranteeRowId : Calculations.CalculateBonusGuarantee();
        var bonusOverride = bonusOverrideRow >= 0 ? Service.LuminaRow<SatisfactionBonusGuarantee>((uint)bonusOverrideRow) : null;
        var supplySheet = Service.LuminaSheet<SatisfactionSupply>()!;
        foreach (var npc in _npcs)
        {
            npc.Rank = inst->SatisfactionRanks[npc.Index];
            npc.UsedDeliveries = inst->UsedAllowances[npc.Index];
            npc.Requests = npc.Rank > 0 ? Calculations.CalculateRequestedItems(npc.SupplyIndex, inst->SupplySeed) : [];
            Array.Fill(npc.IsBonusOverride, false);
            if (npc.Rank == 5 && bonusOverride != null)
            {
                var sheetIndex = npc.Index + 1;
                npc.IsBonusOverride[0] = bonusOverride.Unknown0 == sheetIndex || bonusOverride.Unknown1 == sheetIndex;
                npc.IsBonusOverride[1] = bonusOverride.Unknown2 == sheetIndex || bonusOverride.Unknown3 == sheetIndex;
                npc.IsBonusOverride[2] = bonusOverride.Unknown4 == sheetIndex || bonusOverride.Unknown5 == sheetIndex;
            }
            for (int i = 0; i < npc.Requests.Length; ++i)
            {
                var supply = supplySheet.GetRow(npc.SupplyIndex, npc.Requests[i])!;
                npc.IsBonusEffective[i] = npc.IsBonusOverride[i] || supply.Unknown7;
                npc.Rewards[i] = supply.Reward.Row;
                if (npc.IsBonusOverride[i] && !supply.Unknown7)
                {
                    var numSubrows = supplySheet.GetRowParser(npc.SupplyIndex)!.RowCount;
                    for (uint j = 0; j < numSubrows; ++j)
                    {
                        var supplyOverride = supplySheet.GetRow(npc.SupplyIndex, j)!;
                        if (supplyOverride.Slot == supply.Slot && supplyOverride.Unknown7)
                        {
                            npc.Rewards[i] = supplyOverride.Reward.Row;
                            break;
                        }
                    }
                }

                var reward = Service.LuminaRow<SatisfactionSupplyReward>(npc.Rewards[i])!;
                AddPotentialReward(reward.UnkData1[0].RewardCurrency, reward.UnkData1[0].QuantityHigh * reward.Unknown0 / 100, npc.MaxDeliveries - npc.UsedDeliveries);
                AddPotentialReward(reward.UnkData1[1].RewardCurrency, reward.UnkData1[1].QuantityHigh * reward.Unknown0 / 100, npc.MaxDeliveries - npc.UsedDeliveries); // todo: don't add at low level?..
            }

            if (npc.AchievementMax == 0 && npc.AchievementId != 0)
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

            ImGui.TableNextColumn();
            if (ImGui.Button("Teleport"))
                UIState.Instance()->Telepo.Teleport(npc.AetheryteId, 0);
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
        var inst = SatisfactionSupplyManager.Instance();
        var supplySheet = Service.LuminaSheet<SatisfactionSupply>()!;
        var calcBonus = Calculations.CalculateBonusGuarantee();
        var bonusOverrideRow = inst->BonusGuaranteeRowId != 0xFF ? inst->BonusGuaranteeRowId : calcBonus;
        var bonusOverride = bonusOverrideRow >= 0 ? Service.LuminaRow<SatisfactionBonusGuarantee>((uint)bonusOverrideRow) : null;

        ImGui.TextUnformatted($"Seed: {inst->SupplySeed}, fixed-rng={inst->FixedRandom}");
        ImGui.TextUnformatted($"Guarantee row: {inst->BonusGuaranteeRowId}, adj={inst->TimeAdjustmentForBonusGuarantee}, calculated={calcBonus}");
        foreach (var npc in _npcs)
        {
            var numSubrows = supplySheet.GetRowParser(npc.SupplyIndex)!.RowCount;
            ImGui.TextUnformatted($"#{npc.Index}: rank={npc.Rank}, supply={npc.SupplyIndex} ({numSubrows} subrows), satisfaction={inst->Satisfaction[npc.Index]}, usedAllowances={npc.UsedDeliveries}");
            for (int i = 0; i < npc.Requests.Length; ++i)
                ImGui.TextUnformatted($"- {npc.Requests[i]} '{supplySheet.GetRow(npc.SupplyIndex, npc.Requests[i])!.Item.Value?.Name}'{(npc.IsBonusOverride[i] ? " *****" : "")}");
        }
        ImGui.TextUnformatted($"Current NPC: {inst->CurrentNpc}, supply={inst->CurrentSupplyRowId}");

        var ui = UIState.Instance();
        ImGui.TextUnformatted($"Player loaded: {ui->PlayerState.IsLoaded}");
        ImGui.TextUnformatted($"Achievement state: complete={ui->Achievement.State}, progress={ui->Achievement.ProgressRequestState}");
    }

    private void OnAchievementProgress(uint id, uint current, uint max)
    {
        var npc = _npcs.FirstOrDefault(npc => npc.AchievementId == id);
        if (npc != null)
        {
            npc.AchievementCur = current;
            npc.AchievementMax = max;
        }
    }
}
