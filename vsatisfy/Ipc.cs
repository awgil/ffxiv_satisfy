using clib.Services;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Satisfy;

public sealed class Ipc : IDisposable
{
    private readonly ICallGateProvider<uint, int, bool> _start;
    private readonly ICallGateProvider<bool> _isBusy;
    private readonly ICallGateProvider<object> _stop;

    public Ipc(IDalamudPluginInterface pluginInterface, MainWindow window)
    {
        _start = pluginInterface.GetIpcProvider<uint, int, bool>("vsatisfy.Start");
        _start.RegisterFunc(window.StartTurnIn);
        _isBusy = pluginInterface.GetIpcProvider<bool>("vsatisfy.IsBusy");
        _isBusy.RegisterFunc(() => Svc.Automation.Running);
        _stop = pluginInterface.GetIpcProvider<object>("vsatisfy.Stop");
        _stop.RegisterAction(() => Svc.Automation.Stop());
    }

    public void Dispose()
    {
        _start.UnregisterFunc();
        _isBusy.UnregisterFunc();
        _stop.UnregisterAction();
    }
}
