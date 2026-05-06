using clib;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Satisfy;

public sealed class Plugin : IDalamudPlugin
{
    public static Config Config { get; private set; } = null!;

    private readonly WindowSystem WindowSystem = new("vsatisfy");
    private readonly MainWindow _wndMain;
    private readonly ICommandManager _cmd;

    public Plugin(IDalamudPluginInterface dalamud, ICommandManager commandManager)
    {
        if (!dalamud.ConfigDirectory.Exists)
            dalamud.ConfigDirectory.Create();

        CLibMain.Init(dalamud, this);
        Service.Initialize(this, dalamud);

        Config = new Config();
        Config.Load(dalamud.ConfigFile);
        Config.Modified += () => Config.Save(dalamud.ConfigFile);

        _wndMain = new(dalamud);
        WindowSystem.AddWindow(_wndMain);

        _cmd = commandManager;
        commandManager.AddHandler("/vsatisfy", new((_, _) => _wndMain.IsOpen ^= true) { HelpMessage = "Toggle main window" });

        dalamud.UiBuilder.Draw += WindowSystem.Draw;
        dalamud.UiBuilder.OpenMainUi += () => _wndMain.IsOpen = true;
        dalamud.UiBuilder.OpenConfigUi += () => _wndMain.IsOpen = true;
    }

    public void Dispose()
    {
        _cmd.RemoveHandler("/vsatisfy");
        WindowSystem.RemoveAllWindows();
        _wndMain.Dispose();
    }
}
