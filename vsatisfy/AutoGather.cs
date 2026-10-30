using clib.TaskSystem;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

public sealed class AutoGather(NPCInfo npc) : AutoCommon
{
    private readonly ICallGateSubscriber<int> _gatherChillApiVersion = Service.PluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
    private readonly ICallGateSubscriber<bool> _gatherChillIsBusy = Service.PluginInterface.GetIpcSubscriber<bool>("GatherChill.IsBusy");
    private readonly ICallGateSubscriber<uint, bool> _gatherChillCanGather = Service.PluginInterface.GetIpcSubscriber<uint, bool>("GatherChill.CanGather");
    // uint itemId, int amount;
    private readonly ICallGateSubscriber<uint, int, bool> _gatherChillGatherItem = Service.PluginInterface.GetIpcSubscriber<uint, int, bool>("GatherChill.GatherItem");
    private readonly ICallGateSubscriber<object> _gatherChillStop = Service.PluginInterface.GetIpcSubscriber<object>("GatherChill.Stop");
    private readonly ICallGateSubscriber<bool> _questionableIsRunning = Service.PluginInterface.GetIpcSubscriber<bool>("Questionable.IsRunning");
    private readonly ICallGateSubscriber<string, bool> _questionableStop = Service.PluginInterface.GetIpcSubscriber<string, bool>("Questionable.Stop");
    // uint npcId, uint itemId, byte classJob = ((byte)Job.MIN), int quantity = 1, ushort collectability = 0
    private readonly ICallGateSubscriber<uint, uint, byte, int, ushort, bool> _questionableStartGathering = Service.PluginInterface.GetIpcSubscriber<uint, uint, byte, int, ushort, bool>("Questionable.StartGatheringComplex");

    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(1);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.GatherData == null || npc.CraftData == null)
            throw new Exception("Gather or turn-in data is not initialized");

        var remainingGathers = remainingTurnins - Game.NumItemsInInventory(npc.GatherData.GatherItemId, (short)npc.GatherData.CollectabilityLow);
        if (remainingGathers > 0)
        {
            if (GatherChillCanGather(npc.GatherData.GatherItemId))
                await GatherWithGatherChill(npc.GatherData.GatherItemId, remainingGathers);
            else
                await GatherWithQuestionable();
        }

        Status = "Teleporting back to Npc";
        await TeleportTo(npc.TerritoryId, npc.CraftData.TurnInLocation);

        Status = "Moving to Npc";
        await MoveTo(npc.CraftData.TurnInLocation, MovementConfig.InteractRange);
        Status = $"Turning in {remainingTurnins}x {ItemName(npc.TurnInItems[1])}";
        await TurnIn(npc, 1);
    }

    private async Task GatherWithGatherChill(uint itemId, int amount)
    {
        Status = "Gathering with Gather n Chill";
        using var scope = BeginScope("Gathering");
        ErrorIf(_gatherChillIsBusy.InvokeFunc(), "Gather n Chill is already running"); // before the stop hook, so we don't cancel the user's own run
        using var stop = new OnDispose(_gatherChillStop.InvokeAction);
        ErrorIf(!_gatherChillGatherItem.InvokeFunc(itemId, amount), "Unable to invoke Gather n Chill");
        await WaitWhile(_gatherChillIsBusy.InvokeFunc, "Waiting for gathering to finish"); // busy as soon as GatherItem returns
    }

    private async Task GatherWithQuestionable()
    {
        Status = "Gathering with Questionable";
        using var scope = BeginScope("Gathering");
        using var stop = new OnDispose(() => _questionableStop.InvokeFunc($"{Service.PluginInterface.Manifest.InternalName}"));
        ErrorIf(!_questionableStartGathering.InvokeFunc(npc.TurninId, npc.GatherData!.GatherItemId, (byte)npc.GatherData.ClassJobId, npc.RemainingTurnins(1), (ushort)npc.GatherData.CollectabilityHigh), "Unable to invoke Questionable");
        await WaitWhile(() => !_questionableIsRunning.InvokeFunc(), "Waiting for gathering to start");
        await WaitWhile(_questionableIsRunning.InvokeFunc, "Waiting for gathering to finish");
    }

    private bool GatherChillCanGather(uint itemId) => _gatherChillApiVersion.HasFunction && _gatherChillApiVersion.InvokeFunc() >= 2 && _gatherChillCanGather.InvokeFunc(itemId);
}
