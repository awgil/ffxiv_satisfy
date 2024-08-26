using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace Satisfy;

public class Service
{
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;

    public static Lumina.GameData LuminaGameData => DataManager.GameData;
    public static T? LuminaRow<T>(uint row) where T : Lumina.Excel.ExcelRow => LuminaGameData.GetExcelSheet<T>(Lumina.Data.Language.English)?.GetRow(row);
}
