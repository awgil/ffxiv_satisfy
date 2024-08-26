using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Satisfy;

public unsafe class Achievements : IDisposable
{
    public record struct State(uint Id, uint Cur = 0, uint Max = 0);

    public readonly List<State> Data = [new(1784), new(1979), new(2077), new(2193), new(2435), new(2633), new(2845), new(3069), new(3173), new(3361)];
    private Hook<Achievement.Delegates.ReceiveAchievementProgress> _hook;

    public Achievements()
    {
        _hook = Service.Hook.HookFromAddress<Achievement.Delegates.ReceiveAchievementProgress>(Achievement.Addresses.ReceiveAchievementProgress.Value, ReceiveAchievementDetour);
        _hook.Enable();
    }

    public void Dispose()
    {
        _hook.Dispose();
    }

    public void Reset()
    {
        for (int i = 0; i < Data.Count; ++i)
            Data[i] = new(Data[i].Id);
    }

    public void Request(uint id)
    {
        var ui = UIState.Instance();
        if (ui->Achievement.ProgressRequestState != Achievement.AchievementState.Requested)
            ui->Achievement.RequestAchievementProgress(id);
    }

    private void ReceiveAchievementDetour(Achievement* self, uint id, uint current, uint max)
    {
        var index = Data.FindIndex(d => d.Id == id);
        if (index >= 0)
            Data[index] = new(id, current, max);
        _hook.Original(self, id, current, max);
    }
}
