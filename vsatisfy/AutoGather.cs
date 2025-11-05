using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace Satisfy;

public sealed class AutoGather(NPCInfo npc, IDalamudPluginInterface dalamud) : AutoCommon(dalamud)
{
    private readonly ICallGateSubscriber<bool> _isRunning = dalamud.GetIpcSubscriber<bool>("Questionable.IsRunning");
    // npcId, itemId, classJob, quantity
    private readonly ICallGateSubscriber<uint, uint, byte, int, bool> _startGathering = dalamud.GetIpcSubscriber<uint, uint, byte, int, bool>("Questionable.StartGathering");
    protected override async Task Execute()
    {
        var remainingTurnins = npc.RemainingTurnins(1);
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.GatherData == null || npc.CraftData == null)
            throw new Exception("Gather or turn-in data is not initialized");

        ErrorIf(!_startGathering.InvokeFunc(npc.TurninId, npc.GatherData.GatherItemId, (byte)npc.GatherData.ClassJobId, remainingTurnins), "Unable to invoke Questionable");

        Status = "Gathering with Questionable";
        await WaitWhile(() => !_isRunning.InvokeFunc(), "Waiting for gathering to start");
        await WaitWhile(_isRunning.InvokeFunc, "Waiting for gathering to finish");

        Status = "Teleporting back to Npc";
        await TeleportTo(npc.TerritoryId, npc.CraftData.VendorLocation);

        Status = "Moving to Npc";
        await MoveTo(npc.CraftData.VendorLocation, 3);
        Status = $"Turning in {remainingTurnins}x {ItemName(npc.TurnInItems[1])}";
        await TurnIn(npc.Index, npc.CraftData.TurnInInstanceId, npc.TurnInItems[1], 1, remainingTurnins);
    }
}
