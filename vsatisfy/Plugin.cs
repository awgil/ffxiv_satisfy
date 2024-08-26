using Dalamud.Common;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System.Reflection;

namespace Satisfy;

public sealed class Plugin : IDalamudPlugin
{
    private WindowSystem WindowSystem = new("vsatisfy");
    private MainWindow _wndMain;

    public Plugin(IDalamudPluginInterface dalamud, ISigScanner sigScanner)
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
