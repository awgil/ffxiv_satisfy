using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Satisfy;

public unsafe class Achievements : IDisposable
{
    public event Action<uint, uint, uint>? AchievementProgress;

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

    public void Request(uint id)
    {
        var ui = UIState.Instance();
        if (ui->PlayerState.IsLoaded != 0 && ui->Achievement.ProgressRequestState != Achievement.AchievementState.Requested)
            ui->Achievement.RequestAchievementProgress(id);
    }

    private void ReceiveAchievementDetour(Achievement* self, uint id, uint current, uint max)
    {
        AchievementProgress?.Invoke(id, current, max);
        _hook.Original(self, id, current, max);
    }
}
