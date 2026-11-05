using clib.TaskSystem;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

public sealed class AutoGather(NPCInfo npc) : AutoCommon
{
    // Gather n Chill API 3 job states and start results
    private const int GatherChillApiVersion = 3;
    private const int JobStarted = 0;
    private const int JobRunning = 1, JobStopping = 2, JobSucceeded = 3;
    // a low-quality collectable still counts as gathered for Gather n Chill, so retry for whatever is still missing
    private const int MaxGatherAttempts = 3;

    private readonly ICallGateSubscriber<int> _gatherChillApiVersion = Service.PluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
    // string owner, uint itemId, int amount -> (result, jobId, message)
    private readonly ICallGateSubscriber<string, uint, int, (int Result, Guid JobId, string Message)> _gatherChillStartJob = Service.PluginInterface.GetIpcSubscriber<string, uint, int, (int, Guid, string)>("GatherChill.StartJob");
    // Guid jobId -> (state, gathered, wanted, message)
    private readonly ICallGateSubscriber<Guid, (int State, int Gathered, int Wanted, string Message)> _gatherChillGetJob = Service.PluginInterface.GetIpcSubscriber<Guid, (int, int, int, string)>("GatherChill.GetJob");
    // string owner, Guid jobId
    private readonly ICallGateSubscriber<string, Guid, bool> _gatherChillCancelJob = Service.PluginInterface.GetIpcSubscriber<string, Guid, bool>("GatherChill.CancelJob");

    private static string Owner => Service.PluginInterface.Manifest.InternalName;

    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(1);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.GatherData == null || npc.CraftData == null)
            throw new Exception("Gather or turn-in data is not initialized");

        var itemId = npc.GatherData.GatherItemId;
        var remainingGathers = remainingTurnins - Game.NumItemsInInventory(itemId, (short)npc.GatherData.CollectabilityLow);
        for (var attempt = 1; remainingGathers > 0; ++attempt)
        {
            ErrorIf(attempt > MaxGatherAttempts, $"Still missing {remainingGathers}x {ItemName(itemId)} after {MaxGatherAttempts} gathering attempts");
            await Gather(itemId, remainingGathers);
            remainingGathers = remainingTurnins - Game.NumItemsInInventory(itemId, (short)npc.GatherData.CollectabilityLow);
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
        ErrorIf(!GatherChillAvailable(), $"Gather n Chill (API {GatherChillApiVersion} or newer) is not installed");

        var (result, jobId, startMessage) = _gatherChillStartJob.InvokeFunc(Owner, itemId, amount);
        ErrorIf(result != JobStarted, $"Gather n Chill can't gather {ItemName(itemId)}: {startMessage}");
        using var cancel = new OnDispose(() => _gatherChillCancelJob.InvokeFunc(Owner, jobId)); // no-op once the job has ended

        var job = _gatherChillGetJob.InvokeFunc(jobId);
        await WaitWhile(() => (job = _gatherChillGetJob.InvokeFunc(jobId)).State is JobRunning or JobStopping, "Waiting for gathering to finish");
        ErrorIf(job.State != JobSucceeded, $"Gather n Chill stopped gathering {ItemName(itemId)}: {job.Message}");
    }

    private bool GatherChillAvailable() => _gatherChillApiVersion.HasFunction && _gatherChillApiVersion.InvokeFunc() >= GatherChillApiVersion;
}
