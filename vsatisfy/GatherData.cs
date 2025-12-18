using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Satisfy;

public sealed class GatherData
{
    public uint GatherItemId;
    public uint ClassJobId => GetGatherer();
    public uint CollectabilityLow;
    public uint CollectabilityMid;
    public uint CollectabilityHigh;
    public GatherPoint[] GatherPoints = [];

    public GatherData(uint itemId)
    {
        GatherItemId = itemId;
        if (Service.LuminaSheetSubrow<SatisfactionSupply>()?.Flatten().FirstOrDefault(x => x.Item.RowId == itemId) is { } subrow)
            (CollectabilityLow, CollectabilityMid, CollectabilityHigh) = (subrow.CollectabilityLow, subrow.CollectabilityMid, subrow.CollectabilityHigh);
        if (Service.LuminaRow<GatheringItem>(GatherItemId) is { RowId: var item } && Service.LuminaSubrows<GatheringItemPoint>(item) is { } points)
            foreach (var point in points)
                GatherPoints = [.. GatherPoints, GatherPoint.FromSubrow(point)];
    }

    public record struct GatherPoint(uint TerritoryId, Vector2 Position, uint Radius, uint ClassJob)
    {
        public static GatherPoint FromSubrow(GatheringItemPoint point)
        {
            var exportedPoint = Service.LuminaRow<ExportedGatheringPoint>(point.GatheringPoint.Value.GatheringPointBase.RowId)!;
            var pos = new Vector2(exportedPoint.Value.X, exportedPoint.Value.Y);
            var classJob = exportedPoint.Value.GatheringType.RowId switch
            {
                0 or 1 => 16u,
                2 or 3 => 17u,
                4 or 5 => 18u,
                _ => throw new Exception($"Unknown gathering type {exportedPoint.Value.GatheringType.RowId}"),
            };
            return new(point.GatheringPoint.Value.TerritoryType.RowId, pos, exportedPoint.Value.Radius, classJob);
        }
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
            jobId = Service.PlayerState.ClassJob.RowId;
        }).Wait(5000);
        return jobId is 16 or 17 ? jobId : Plugin.Config.SelectedGatherJob;
    }
}
