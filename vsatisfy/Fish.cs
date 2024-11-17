using Lumina.Excel.Sheets;
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
        if (Service.LuminaSheet<FishingSpot>()!.FirstOrDefault(s => s.Item.Any(i => i.RowId == FishItemId)) is var fish && fish.RowId != 0)
        {
            FishSpotId = fish.RowId;
            TerritoryTypeId = fish.TerritoryType.RowId;
            var map = fish.TerritoryType.ValueNullable?.Map.ValueNullable;
            var scale = (map?.SizeFactor ?? 100) * 0.01f;
            var x = PixelCoordToWorldCoord(fish.X, scale, map?.OffsetX ?? 0);
            var z = PixelCoordToWorldCoord(fish.Z, scale, map?.OffsetY ?? 0);
            Center = new(x, 0, z);
            Radius = fish.Radius;
            ClosestAetheryteId = FindClosestAetheryte(map?.RowId ?? 0, new(fish.X, fish.Z));
        }
        else if (Service.LuminaSheet<SpearfishingItem>()!.FirstOrDefault(s => s.Item.RowId == FishItemId) is var sfish && sfish.RowId != 0)
        {
            IsSpearFish = true;
            FishSpotId = sfish.TerritoryType.RowId;
            var fishSpot = Service.LuminaRow<SpearfishingNotebook>(FishSpotId)!.Value;
            TerritoryTypeId = fishSpot.TerritoryType.RowId;
            var map = fishSpot.TerritoryType.ValueNullable?.Map.ValueNullable;
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

    private static uint FindClosestAetheryte(uint mapId, Vector2 sheetPos)
    {
        List<Aetheryte> aetherytes = [.. Service.LuminaSheet<Aetheryte>()?.Where(a => a.Map.RowId == mapId)];
        return aetherytes.Count > 0 ? aetherytes.MinBy(a => (sheetPos - AetherytePosition(a)).LengthSquared()).RowId : 0;
    }

    // stolen from HTA, same coordinate system as fishingspot sheet?..
    private static Vector2 AetherytePosition(Aetheryte a)
    {
        var marker = Service.LuminaSheetSubrow<MapMarker>()?.Flatten().FirstOrDefault(m => m.DataType == 3 && m.DataKey.RowId == a.RowId);
        return marker != null ? new(marker.Value.X, marker.Value.Y) : default;
    }
}
