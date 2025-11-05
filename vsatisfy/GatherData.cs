using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Satisfy;

public sealed class GatherData
{
    public uint GatherItemId;
    public uint ClassJobId => GetGatherer();

    public GatherData(uint itemId)
    {
        GatherItemId = itemId;
    }

    private unsafe uint GetGatherer()
    {
        return Plugin.Config.CraftJobType switch
        {
            Config.JobChoice.Specific => Plugin.Config.SelectedGatherJob,
            Config.JobChoice.Current => GetCurrentGatheringJob(),
            Config.JobChoice.LowestXP => (Service.LuminaSheet<ClassJob>()?
                .Where(c => c.RowId is 16 or 17 &&
                    PlayerState.Instance() != null &&
                    c.ExpArrayIndex >= 0 &&
                    c.ExpArrayIndex < PlayerState.Instance()->ClassJobLevels.Length &&
                    PlayerState.Instance()->ClassJobLevels[c.ExpArrayIndex] >= 1)
                .OrderBy(c =>
                    PlayerState.Instance()->ClassJobLevels[c.ExpArrayIndex])
                .FirstOrDefault())?.RowId ?? Plugin.Config.SelectedGatherJob,
            Config.JobChoice.HighestXP => (Service.LuminaSheet<ClassJob>()?
                .Where(c => c.RowId is 16 or 17 &&
                    PlayerState.Instance() != null &&
                    c.ExpArrayIndex >= 0 &&
                    c.ExpArrayIndex < PlayerState.Instance()->ClassJobLevels.Length &&
                    PlayerState.Instance()->ClassJobLevels[c.ExpArrayIndex] >= 1)
                .OrderByDescending(c =>
                    PlayerState.Instance()->ClassJobLevels[c.ExpArrayIndex])
                .FirstOrDefault())?.RowId ?? Plugin.Config.SelectedGatherJob,
            _ => Plugin.Config.SelectedGatherJob,
        };
    }

    private uint GetCurrentGatheringJob()
    {
        uint jobId = Plugin.Config.SelectedGatherJob;
        _ = Service.Framework.RunOnFrameworkThread(() =>
        {
            if (Service.ClientState.LocalPlayer is { } player)
                jobId = player.ClassJob.RowId;
        }).Wait(5000);
        return jobId is 16 or 17 ? jobId : Plugin.Config.SelectedGatherJob;
    }
}