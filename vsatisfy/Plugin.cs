using clib;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Satisfy;

public sealed class Plugin(IDalamudPluginInterface pluginInterface, ICommandManager cmd) : IAsyncDalamudPlugin
{
    public static Config Config { get; private set; } = null!;

    private readonly WindowSystem WindowSystem = new("vsatisfy");
    private MainWindow? _wndMain;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!pluginInterface.ConfigDirectory.Exists)
            pluginInterface.ConfigDirectory.Create();

        CLibMain.Init(pluginInterface, this, CLibModule.Automation);
        Service.Initialize(this, pluginInterface);

        Config = new Config();
        Config.Load(pluginInterface.ConfigFile);
        Config.Modified += () => Config.Save(pluginInterface.ConfigFile);

        _wndMain = new();
        WindowSystem.AddWindow(_wndMain);

        cmd.AddHandler("/vsatisfy", new((_, _) => _wndMain.IsOpen ^= true) { HelpMessage = "Toggle main window" });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += () => _wndMain.IsOpen = true;
        pluginInterface.UiBuilder.OpenConfigUi += () => _wndMain.IsOpen = true;
    }

    public async ValueTask DisposeAsync()
    {
        CLibMain.Dispose();
        cmd.RemoveHandler("/vsatisfy");
        WindowSystem.RemoveAllWindows();
        _wndMain?.Dispose();
    }
}
