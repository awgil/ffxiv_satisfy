using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.ComponentModel;
using System.Reflection;

namespace Satisfy;
public static class ImGuiUtils
{
    public static string EnumString(Enum v)
    {
        var name = v.ToString();
        return v.GetType().GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
    }

    public static bool Enum<T>(string label, ref T v) where T : Enum
    {
        var res = false;
        ImGui.SetNextItemWidth(200);
        using var combo = ImRaii.Combo(label, EnumString(v));
        if (!combo) return false;
        foreach (var opt in System.Enum.GetValues(v.GetType()))
        {
            if (ImGui.Selectable(EnumString((Enum)opt), opt.Equals(v)))
            {
                v = (T)opt;
                res = true;
            }
        }
        return res;
    }
}
