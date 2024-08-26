using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace Satisfy;

public sealed class Plugin : IDalamudPlugin
{
    private WindowSystem WindowSystem = new("vsatisfy");
    private MainWindow _wndMain;

    public Plugin(IDalamudPluginInterface dalamud)
    {
        if (!dalamud.ConfigDirectory.Exists)
            dalamud.ConfigDirectory.Create();
        dalamud.Create<Service>();

        _wndMain = new();
        _wndMain.IsOpen = true;
        WindowSystem.AddWindow(_wndMain);

        dalamud.UiBuilder.Draw += WindowSystem.Draw;
        dalamud.UiBuilder.OpenConfigUi += () => _wndMain.IsOpen = true;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        _wndMain.Dispose();
    }
}
