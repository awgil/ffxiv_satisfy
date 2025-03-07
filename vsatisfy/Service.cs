using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace Satisfy;

public class Service
{
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static ICondition Conditions { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;

    public static Lumina.GameData LuminaGameData => DataManager.GameData;
    public static Lumina.Excel.ExcelSheet<T>? LuminaSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T> => LuminaGameData?.GetExcelSheet<T>(Lumina.Data.Language.English);
    public static Lumina.Excel.SubrowExcelSheet<T>? LuminaSheetSubrow<T>() where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaGameData?.GetSubrowExcelSheet<T>(Lumina.Data.Language.English);
    public static T? LuminaRow<T>(uint row) where T : struct, Lumina.Excel.IExcelRow<T> => LuminaSheet<T>()?.GetRowOrDefault(row);
    public static Lumina.Excel.SubrowCollection<T>? LuminaSubrows<T>(uint row) where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaSheetSubrow<T>()?.GetRowOrDefault(row);
    public static T? LuminaRow<T>(uint row, ushort subRow) where T : struct, Lumina.Excel.IExcelSubrow<T> => LuminaSheetSubrow<T>()?.GetSubrowOrDefault(row, subRow);
}
