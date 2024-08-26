using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.GeneratedSheets;

namespace Satisfy;

public unsafe static class Calculations
{
    // see Client::Game::SatisfactionSupplyManager.setCurrentNpc
    public static int CalculateBonusGuarantee()
    {
        var framework = Framework.Instance();
        var proxy = framework->IsNetworkModuleInitialized ? framework->NetworkModuleProxy : null;
        var module = proxy != null ? proxy->NetworkModule : null;
        if (module == null)
            return -1;
        var timestamp = *(int*)((nint)module + 0xB54); // GetCurrentDeviceTime; TODO update offset in CS
        timestamp += SatisfactionSupplyManager.Instance()->TimeAdjustmentForBonusGuarantee;
        return CalculateBonusGuarantee(timestamp);
    }

    // see getBonusGuaranteeIndex
    public static int CalculateBonusGuarantee(int timestamp)
    {
        var secondsSinceStart = timestamp - 1657008000;
        var weeksSinceStart = secondsSinceStart / 604800;
        return weeksSinceStart % 10;
    }

    public static uint[] CalculateRequestedItems(int npcIndex)
    {
        var inst = SatisfactionSupplyManager.Instance();
        var rank = inst->SatisfactionRanks[npcIndex];
        var supplyIndex = Service.LuminaRow<SatisfactionNpc>((uint)npcIndex + 1)!.SupplyIndex[rank];
        return CalculateRequestedItems((uint)supplyIndex, inst->SupplySeed);
    }

    // see Client::Game::SatisfactionSupplyManager.onSatisfactionSupplyRead
    public static uint[] CalculateRequestedItems(uint supplyIndex, uint seed)
    {
        var sheet = Service.LuminaSheet<SatisfactionSupply>()!;
        var numSubRows = sheet.GetRowParser(supplyIndex)!.RowCount;

        var h1 = (0x03CEA65Cu * supplyIndex) ^ (0x1A0DD20Eu * seed);
        var h2 = (0xDF585D5Du * supplyIndex) ^ (0x3057656Eu * seed);
        var h3 = (0xED69E442u * supplyIndex) ^ (0x2202EA5Au * seed);
        var h4 = (0xAEFC3901u * supplyIndex) ^ (0xE70723F6u * seed);
        uint[] res = [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF];
        var h5 = h1;
        for (int iSlot = 1; iSlot < 4; ++iSlot)
        {
            var sumProbabilities = 0;
            for (uint iSub = 0; iSub < numSubRows; ++iSub)
            {
                var row = sheet.GetRow(supplyIndex, iSub)!;
                if (row.Slot == iSlot)
                    sumProbabilities += row.ProbabilityPct;
            }

            var hTemp = h5 ^ (h5 << 11);
            h1 = h3;
            h3 = h4;
            h5 = h2;
            h4 ^= hTemp ^ ((hTemp ^ (h4 >> 11)) >> 8);
            h2 = h1;

            var roll = h4 % sumProbabilities;
            for (uint iSub = 0; iSub < numSubRows; ++iSub)
            {
                var row = sheet.GetRow(supplyIndex, iSub)!;
                if (row.Slot != iSlot)
                    continue;
                if (roll < row.ProbabilityPct)
                {
                    res[iSlot - 1] = iSub;
                    break;
                }
                roll -= row.ProbabilityPct;
            }
        }
        return res;
    }
}
