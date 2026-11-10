using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Satisfy;

public static unsafe class RaptureAtkUnitManagerExtensions
{
    extension(RaptureAtkUnitManager)
    {
        public static AtkUnitBase* GetFocusedAddonById(uint id)
        {
            ref var focused = ref RaptureAtkUnitManager.Instance()->AtkUnitManager.FocusedUnitsList;
            for (var i = 0; i < Math.Min(focused.Count, focused.Entries.Length); ++i)
            {
                var addon = focused.Entries[i].Value;
                if (addon != null && addon->Id == id)
                    return addon;
            }
            return null;
        }
    }
}
