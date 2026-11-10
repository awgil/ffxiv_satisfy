using FFXIVClientStructs.FFXIV.Client.Game;
using clib.TaskSystem;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

public sealed class AutoGather(NPCInfo npc) : AutoCommon
{
    private const int JobStarted = 0, JobNoRoute = 2;
    private const int JobRunning = 1, JobStopping = 2, JobSucceeded = 3;
    private const int MaxGatherAttempts = 3;

    private readonly ICallGateSubscriber<int> _gatherChillApiVersion = Service.PluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
    private readonly ICallGateSubscriber<string, uint, int, (int Result, Guid JobId, string Message)> _gatherChillStartJob = Service.PluginInterface.GetIpcSubscriber<string, uint, int, (int, Guid, string)>("GatherChill.StartJob");
    private readonly ICallGateSubscriber<string, uint, int, uint, (int Result, Guid JobId, string Message)> _gatherChillStartJobForJob = Service.PluginInterface.GetIpcSubscriber<string, uint, int, uint, (int, Guid, string)>("GatherChill.StartJobForJob");
    private readonly ICallGateSubscriber<Guid, (int State, int Gathered, int Wanted, string Message)> _gatherChillGetJob = Service.PluginInterface.GetIpcSubscriber<Guid, (int, int, int, string)>("GatherChill.GetJob");
    private readonly ICallGateSubscriber<string, Guid, bool> _gatherChillCancelJob = Service.PluginInterface.GetIpcSubscriber<string, Guid, bool>("GatherChill.CancelJob");
    private readonly ICallGateSubscriber<bool> _gatherChillIsBusy = Service.PluginInterface.GetIpcSubscriber<bool>("GatherChill.IsBusy");
    private readonly ICallGateSubscriber<object> _gatherChillOpenOverlay = Service.PluginInterface.GetIpcSubscriber<object>("GatherChill.OpenOverlay");
    private readonly ICallGateSubscriber<object> _gatherChillCloseOverlay = Service.PluginInterface.GetIpcSubscriber<object>("GatherChill.CloseOverlay");

    private static string Owner => Service.PluginInterface.Manifest.InternalName;

    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(1);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.GatherData == null || npc.CraftData == null)
            throw new Exception("Gather or turn-in data is not initialized");

        ErrorIf(!_gatherChillApiVersion.HasFunction || _gatherChillApiVersion.InvokeFunc() < 1, "Gather n Chill is not installed or is out of date");

        var itemId = npc.GatherData.GatherItemId;
        var remainingGathers = remainingTurnins - InventoryManager.GetItemCount(itemId, (short)npc.GatherData.CollectabilityLow);
        for (var attempt = 1; remainingGathers > 0; ++attempt)
        {
            ErrorIf(attempt > MaxGatherAttempts, $"Still missing {remainingGathers}x {ItemName(itemId)} after {MaxGatherAttempts} gathering attempts");
            await Gather(itemId, remainingGathers);
            remainingGathers = remainingTurnins - InventoryManager.GetItemCount(itemId, (short)npc.GatherData.CollectabilityLow);
        }

        Status = "Teleporting back to Npc";
        await TeleportTo(npc.TerritoryId, npc.CraftData.TurnInLocation);

        Status = "Moving to Npc";
        await MoveTo(npc.CraftData.TurnInLocation, MovementConfig.InteractRange);
        Status = $"Turning in {remainingTurnins}x {ItemName(npc.TurnInItems[1])}";
        await TurnIn(npc, 1);
    }

    private async Task Gather(uint itemId, int amount)
    {
        Status = "Gathering with Gather n Chill";
        using var scope = BeginScope("Gathering");
        await WaitWhile(_gatherChillIsBusy.InvokeFunc, "Waiting for Gather n Chill to be free");

        // only uses that job's routes; if the item has none for it, let gather n chill pick rather than failing the delivery
        var classJob = npc.GatherData!.ClassJobId;
        var (result, jobId, startMessage) = _gatherChillStartJobForJob.InvokeFunc(Owner, itemId, amount, classJob);
        if (result == JobNoRoute)
        {
            Warning($"Gather n Chill has no {Service.LuminaRow<Lumina.Excel.Sheets.ClassJob>(classJob)?.Abbreviation} route for {ItemName(itemId)}, letting it pick the job. I think you should tell Ice about this.");
            (result, jobId, startMessage) = _gatherChillStartJob.InvokeFunc(Owner, itemId, amount);
        }
        ErrorIf(result != JobStarted, $"Gather n Chill can't gather {ItemName(itemId)}: {startMessage}");
        using var cancel = new OnDispose(() => _gatherChillCancelJob.InvokeFunc(Owner, jobId));
        _gatherChillOpenOverlay.InvokeAction();

        var job = _gatherChillGetJob.InvokeFunc(jobId);
        await WaitWhile(() => (job = _gatherChillGetJob.InvokeFunc(jobId)).State is JobRunning or JobStopping, "Waiting for gathering to finish");
        ErrorIf(job.State != JobSucceeded, $"Gather n Chill stopped gathering {ItemName(itemId)}: {job.Message}");

        _gatherChillCloseOverlay.InvokeAction();
    }
}
