using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

public sealed class AutoGather(NPCInfo npc, IDalamudPluginInterface dalamud) : AutoCommon(dalamud)
{
    private readonly ICallGateSubscriber<bool> _isRunning = dalamud.GetIpcSubscriber<bool>("Questionable.IsRunning");
    private readonly ICallGateSubscriber<string, bool> _stop = dalamud.GetIpcSubscriber<string, bool>("Questionable.Stop");
    // npcId, itemId, classJob, quantity
    private readonly ICallGateSubscriber<uint, uint, byte, int, bool> _startGathering = dalamud.GetIpcSubscriber<uint, uint, byte, int, bool>("Questionable.StartGathering");
    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(1);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.GatherData == null || npc.CraftData == null)
            throw new Exception("Gather or turn-in data is not initialized");

        if (remainingTurnins - Game.NumItemsInInventory(npc.GatherData.GatherItemId, (short)npc.GatherData.CollectabilityLow) > 0)
            await Gather();

        Status = "Teleporting back to Npc";
        await TeleportTo(npc.TerritoryId, npc.CraftData.TurnInLocation);

        Status = "Moving to Npc";
        await MoveTo(npc.CraftData.TurnInLocation, 3);
        Status = $"Turning in {remainingTurnins}x {ItemName(npc.TurnInItems[1])}";
        await TurnIn(npc.Index, npc.CraftData.TurnInInstanceId, npc.TurnInItems[1], 1, remainingTurnins);
    }

    private async Task Gather()
    {
        Status = "Gathering with Questionable";
        using var scope = BeginScope("Gathering");
        using var stop = new OnDispose(() => _stop.InvokeAction($"{Service.PluginInterface.Manifest.InternalName}"));
        ErrorIf(!_startGathering.InvokeFunc(npc.TurninId, npc.GatherData!.GatherItemId, (byte)npc.GatherData.ClassJobId, npc.RemainingTurnins(1)), "Unable to invoke Questionable");
        await WaitWhile(() => !_isRunning.InvokeFunc(), "Waiting for gathering to start");
        await WaitWhile(_isRunning.InvokeFunc, "Waiting for gathering to finish");
    }
}
