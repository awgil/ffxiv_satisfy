using Lumina.Excel.GeneratedSheets;
using System.Numerics;

namespace Satisfy;

// data & functions needed to fish an item
public sealed class Fish
{
    public uint FishItemId;
    public bool IsSpearFish;
    public uint FishSpotId;
    public uint TerritoryTypeId;
    public Vector3 Center;
    public int Radius; // TODO: no idea what scale it uses?..
    public uint ClosestAetheryteId;

    public Fish(uint itemId)
    {
        FishItemId = itemId;
        if (Service.LuminaSheet<FishingSpot>()?.FirstOrDefault(s => s.Item.Any(i => i.Row == FishItemId)) is var fish && fish != null)
        {
            FishSpotId = fish.RowId;
            TerritoryTypeId = fish.TerritoryType.Row;
            var map = fish.TerritoryType.Value?.Map.Value;
            var scale = (map?.SizeFactor ?? 100) * 0.01f;
            var x = PixelCoordToWorldCoord(fish.X, scale, map?.OffsetX ?? 0);
            var z = PixelCoordToWorldCoord(fish.Z, scale, map?.OffsetY ?? 0);
            Center = new(x, 0, z);
            Radius = fish.Radius;
            ClosestAetheryteId = FindClosestAetheryte(map?.RowId ?? 0, new(fish.X, fish.Z));
        }
        else if (Service.LuminaSheet<SpearfishingItem>()?.FirstOrDefault(s => s.Item.Row == FishItemId) is var sfish && sfish != null)
        {
            IsSpearFish = true;
            FishSpotId = sfish.TerritoryType.Row;
            var fishSpot = Service.LuminaRow<SpearfishingNotebook>(FishSpotId)!;
            TerritoryTypeId = fishSpot.TerritoryType.Row;
            var map = fishSpot.TerritoryType.Value?.Map.Value;
            var scale = (map?.SizeFactor ?? 100) * 0.01f;
            var x = PixelCoordToWorldCoord(fishSpot.X, scale, map?.OffsetX ?? 0);
            var z = PixelCoordToWorldCoord(fishSpot.Y, scale, map?.OffsetY ?? 0);
            Center = new(x, 0, z);
            Radius = fishSpot.Radius;
            ClosestAetheryteId = FindClosestAetheryte(map?.RowId ?? 0, new(fishSpot.X, fishSpot.Y));
        }
    }

    // see: https://github.com/xivapi/ffxiv-datamining/blob/master/docs/MapCoordinates.md
    // see: dalamud MapLinkPayload class
    private static float PixelCoordToWorldCoord(float coord, float scale, short offset)
    {
        // +1 - networkAdjustment == 0
        // (coord / scale * 2) * (scale / 100) = coord / 50
        // * 2048 / 41 / 50 = 0.999024
        const float factor = 2048.0f / (50 * 41);
        return (coord * factor - 1024f) / scale - offset * 0.001f;
    }

    private static uint FindClosestAetheryte(uint mapId, Vector2 sheetPos) => Service.LuminaSheet<Aetheryte>()?.Where(a => a.Map.Row == mapId).MinBy(a => (sheetPos - AetherytePosition(a)).LengthSquared())?.RowId ?? 0;

    // stolen from HTA, same coordinate system as fishingspot sheet?..
    private static Vector2 AetherytePosition(Aetheryte a)
    {
        var marker = Service.LuminaSheet<MapMarker>()?.FirstOrDefault(m => m.DataType == 3 && m.DataKey == a.RowId);
        return marker != null ? new(marker.X, marker.Y) : default;
    }
}
