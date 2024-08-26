using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace Satisfy;

public class MainWindow() : Window("Satisfier"), IDisposable
{
    public void Dispose()
    {
    }

    public unsafe override void Draw()
    {
        ImGui.TextUnformatted("Hello");
    }
}
