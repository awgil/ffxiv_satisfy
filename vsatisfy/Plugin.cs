using Dalamud.Common;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Reflection;

namespace Satisfy;

public sealed class Plugin : IDalamudPlugin
{
    public static Config Config { get; private set; } = null!;
    private WindowSystem WindowSystem = new("vsatisfy");
    private MainWindow _wndMain;
    private ICommandManager _cmd;

    public Plugin(IDalamudPluginInterface dalamud, ISigScanner sigScanner, ICommandManager commandManager)
    {
        if (!dalamud.ConfigDirectory.Exists)
            dalamud.ConfigDirectory.Create();

        var dalamudRoot = dalamud.GetType().Assembly.
                GetType("Dalamud.Service`1", true)!.MakeGenericType(dalamud.GetType().Assembly.GetType("Dalamud.Dalamud", true)!).
                GetMethod("Get")!.Invoke(null, BindingFlags.Default, null, [], null);
        var dalamudStartInfo = dalamudRoot?.GetType().GetProperty("StartInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dalamudRoot) as DalamudStartInfo;
        var gameVersion = dalamudStartInfo?.GameVersion?.ToString() ?? "unknown";
        InteropGenerator.Runtime.Resolver.GetInstance.Setup(sigScanner.SearchBase, gameVersion, new(dalamud.ConfigDirectory.FullName + "/cs.json"));
        FFXIVClientStructs.Interop.Generated.Addresses.Register();
        InteropGenerator.Runtime.Resolver.GetInstance.Resolve();

        dalamud.Create<Service>();

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
