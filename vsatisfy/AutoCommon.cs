using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Numerics;
using System.Threading.Tasks;

namespace Satisfy;

// common automation utilities
public abstract class AutoCommon(IDalamudPluginInterface dalamud) : AutoTask
{
    private readonly ICallGateSubscriber<bool> _vnavIsReady = dalamud.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    private readonly ICallGateSubscriber<float> _vnavBuildProgress = dalamud.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
    private readonly ICallGateSubscriber<Vector3, bool, bool> _vnavPathfindAndMoveTo = dalamud.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
    private readonly ICallGateSubscriber<object> _vnavStop = dalamud.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

    protected async Task MoveTo(Vector3 dest, float tolerance, bool fly = false)
    {
        using var scope = BeginScope("MoveTo");
        if (Game.PlayerInRange(dest, tolerance))
            return; // already in range

        // ensure navmesh is ready
        await WaitWhile(() => NavBuildProgress() >= 0, "BuildMesh");
        ErrorIf(!NavIsReady(), "Failed to build navmesh for the zone");
        ErrorIf(!NavPathfindAndMoveTo(dest, fly), "Failed to start pathfinding to destination");
        using var stop = new OnDispose(NavStop);
        await WaitWhile(() => !Game.PlayerInRange(dest, tolerance), "Navigate");
    }

    protected async Task TeleportTo(uint territoryId, Vector3 destination)
    {
        using var scope = BeginScope("Teleport");
        if (Game.CurrentTerritory() == territoryId)
            return; // already in correct zone

        var closestAetheryteId = Map.FindClosestAetheryte(territoryId, destination);
        var teleportAetheryteId = Map.FindPrimaryAetheryte(closestAetheryteId);
        ErrorIf(teleportAetheryteId == 0, $"Failed to find aetheryte in {territoryId}");
        if (Game.CurrentTerritory() != Service.LuminaRow<Lumina.Excel.Sheets.Aetheryte>(teleportAetheryteId)!.Value.Territory.RowId)
        {
            ErrorIf(!Game.ExecuteTeleport(teleportAetheryteId), $"Failed to teleport to {teleportAetheryteId}");
            await WaitWhile(() => !Game.PlayerIsBusy(), "TeleportStart");
            await WaitWhile(Game.PlayerIsBusy, "TeleportFinish");
        }

        if (teleportAetheryteId != closestAetheryteId)
        {
            var (aetheryteId, aetherytePos) = Game.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, 10);
            ErrorIf(!Game.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipTalk(Game.IsSelectStringAddonActive, "WaitSelectAethernet");
            Game.TeleportToAethernet(teleportAetheryteId, closestAetheryteId);
            await WaitWhile(() => !Game.PlayerIsBusy(), "TeleportAethernetStart");
            await WaitWhile(Game.PlayerIsBusy, "TeleportAethernetFinish");
        }

        if (territoryId == 886)
        {
            // firmament special case
            var (aetheryteId, aetherytePos) = Game.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, 10);
            ErrorIf(!Game.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipTalk(Game.IsSelectStringAddonActive, "WaitSelectFirmament");
            Game.TeleportToFirmament(teleportAetheryteId);
            await WaitWhile(() => !Game.PlayerIsBusy(), "TeleportFirmamentStart");
            await WaitWhile(Game.PlayerIsBusy, "TeleportFirmamentFinish");
        }

        ErrorIf(Game.CurrentTerritory() != territoryId, "Failed to teleport to expected zone");
    }

    protected async Task TurnIn(int npcIndex, ulong npcInstanceId, uint itemId, int slot, int count)
    {
        using var scope = BeginScope("TurnIn");
        if (!Game.IsTurnInSupplyInProgress((uint)npcIndex + 1))
        {
            ErrorIf(!Game.InteractWith(npcInstanceId), "Failed to interact with turn-in NPC");
            await WaitUntilSkipTalk(Game.IsSelectStringAddonActive, "WaitSelect");
            Game.SelectTurnIn();
        }
        for (int i = 0; i < count; ++i)
        {
            await WaitUntilSkipTalk(() => Game.IsTurnInSupplyInProgress((uint)npcIndex + 1), "WaitDialog");
            Game.TurnInSupply(slot);
            await WaitWhile(() => !Game.IsTurnInRequestInProgress(itemId), "WaitHandIn");
            Game.TurnInRequestCommit();
        }
        await WaitUntilSkipTalk(() => !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneStart");
        await WaitUntilSkipTalk(() => Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent], "WaitCutsceneEnd");
    }

    // wait until condition is satisfied, skipping all talks as they appear
    protected async Task WaitUntilSkipTalk(Func<bool> condition, string scopeName)
    {
        using var scope = BeginScope(scopeName);
        while (!condition())
        {
            if (Game.IsTalkInProgress())
            {
                Log("progressing talk...");
                Game.ProgressTalk();
            }
            Log("waiting...");
            await NextFrame();
        }
    }

    protected static string ItemName(uint itemId) => Service.LuminaRow<Lumina.Excel.Sheets.Item>(itemId)?.Name.ToString() ?? itemId.ToString();

    private bool NavIsReady() => _vnavIsReady.InvokeFunc();
    private float NavBuildProgress() => _vnavBuildProgress.InvokeFunc();
    private bool NavPathfindAndMoveTo(Vector3 dest, bool fly = false) => _vnavPathfindAndMoveTo.InvokeFunc(dest, fly);
    private void NavStop() => _vnavStop.InvokeAction();
}
