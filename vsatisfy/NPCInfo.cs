namespace Satisfy;

public record class NPCInfo(int Index, uint TurninId, string Name, int MaxDeliveries, int[] SupplyIndices)
{
    public readonly int[] SupplyIndices = [.. SupplyIndices];
    public int Rank;
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
    public uint AetheryteId; // aetheryte closest to npc & vendor
    public uint TerritoryId;
    public CraftTurnin? CraftData;
    public Fish? FishData;

    public uint SupplyIndex => (uint)SupplyIndices[Rank];
    public uint AchievementCur => Math.Min(AchievementStart + (uint)UsedDeliveries, AchievementMax);

    public void InitHardcodedData(uint achievementId, uint aetheryteId, uint territoryId = 0)
    {
        AchievementId = achievementId;
        AetheryteId = aetheryteId;
        TerritoryId = territoryId != 0 ? territoryId : Service.LuminaRow<Lumina.Excel.Sheets.Aetheryte>(aetheryteId)!.Value.Territory.RowId;
        CraftData = new((uint)SupplyIndices[1], TurninId, TerritoryId);
    }
}
