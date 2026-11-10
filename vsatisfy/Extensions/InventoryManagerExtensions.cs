using FFXIVClientStructs.FFXIV.Client.Game;

namespace Satisfy;

public static unsafe class InventoryManagerExtensions
{
    extension(InventoryManager)
    {
        // TODO: collectibility threshold
        public static int GetItemCount(uint itemId, short minCollectability)
            => InventoryManager.Instance()->GetInventoryItemCount(itemId, false, false, false, minCollectability);
    }
}
