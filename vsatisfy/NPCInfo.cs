using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Satisfy;

public record class NPCInfo(int Index, uint TurninId, string Name, int MaxDeliveries, int[] SupplyIndices)
{
    public readonly int[] SupplyIndices = [.. SupplyIndices];
    public bool Unlocked;
    public int Rank;
    public int SatisfactionCur;
    public int SatisfactionMax;
    public int UsedDeliveries;
    public uint[] Requests = [];
    public bool[] IsBonusOverride = [false, false, false];
    public bool[] IsBonusEffective = [false, false, false];
    public uint[] EffectiveRequests = [0, 0, 0]; // accounts for bonus override
    public uint[] Rewards = [0, 0, 0];
    public uint[] TurnInItems = [0, 0, 0];
    public uint AchievementId;
    public uint AchievementStart; // since we don't get any achievement updates while making deliveries, store state 'at the beginning of the week'
    public uint AchievementMax;
    public uint TerritoryId; // where turn-in npc is located (note: we assume crafting vendor is always is the same zone)
    public CraftTurnin? CraftData;
    public Fish? FishData;

    public uint SupplyIndex => (uint)SupplyIndices[Rank];
    public uint AchievementCur => Math.Min(AchievementStart + (uint)UsedDeliveries, AchievementMax);
    public bool IsUnlocked => Service.LuminaRow<SatisfactionNpc>((uint)Index) is { QuestRequired.RowId: var questId } && QuestManager.IsQuestComplete(questId);

    public void InitHardcodedData(uint achievementId, uint territoryId)
    {
        AchievementId = achievementId;
        TerritoryId = territoryId;
        CraftData = new((uint)SupplyIndices[1], TurninId, TerritoryId);
    }

    public int RemainingTurnins(int requestIndex)
    {
        var res = MaxDeliveries - UsedDeliveries;
        if (SatisfactionMax > SatisfactionCur)
        {
            var reward = Service.LuminaRow<SatisfactionSupplyReward>(Rewards[requestIndex])!.Value.SatisfactionHigh;
            res = Math.Min(res, (int)Math.Ceiling((SatisfactionMax - SatisfactionCur) / (float)reward));
        }
        return res;
    }
}
