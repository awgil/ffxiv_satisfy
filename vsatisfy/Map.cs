using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System.Numerics;

namespace Satisfy;

public static class Map
{
    public static Vector3 PixelCoordsToWorldCoords(int x, int z, uint mapId)
    {
        var map = Service.LuminaRow<Lumina.Excel.Sheets.Map>(mapId);
        var scale = (map?.SizeFactor ?? 100) * 0.01f;
        var wx = PixelCoordToWorldCoord(x, scale, map?.OffsetX ?? 0);
        var wz = PixelCoordToWorldCoord(z, scale, map?.OffsetY ?? 0);
        return new(wx, 0, wz);
    }

    // see: https://github.com/xivapi/ffxiv-datamining/blob/master/docs/MapCoordinates.md
    // see: dalamud MapLinkPayload class
    public static float PixelCoordToWorldCoord(float coord, float scale, short offset)
    {
        // +1 - networkAdjustment == 0
        // (coord / scale * 2) * (scale / 100) = coord / 50
        // * 2048 / 41 / 50 = 0.999024
        const float factor = 2048.0f / (50 * 41);
        return (coord * factor - 1024f) / scale - offset * 0.001f;
    }

    public static uint FindClosestAetheryte(uint territoryTypeId, Vector3 worldPos, float? preferredY = null, float verticalWeight = 8f)
    {
        if (territoryTypeId == 886)
        {
            // firmament special case - just return ishgard main aetheryte
            // firmament aetherytes are special (see 
            return 70;
        }
        List<Aetheryte> aetherytes = [.. Service.LuminaSheet<Aetheryte>()?.Where(a => a.Territory.RowId == territoryTypeId) ?? []];
        if (aetherytes.Count == 0)
            return 0;

        if (preferredY.HasValue)
        {
            float targetY = preferredY.Value;
            return aetherytes
                .MinBy(a =>
                {
                    var pos = AetherytePosition(a);
                    var dx = worldPos.X - pos.X;
                    var dz = worldPos.Z - pos.Z;
                    var dy = (targetY - pos.Y) * verticalWeight;
                    return dx * dx + dz * dz + dy * dy;
                })
                .RowId;
        }

        return aetherytes.MinBy(a => (worldPos - AetherytePosition(a)).LengthSquared()).RowId;
    }

    public static bool ShouldUseAethernet(Vector3 primaryAetherytePos, Vector3 shardPos, Vector3 destination, float verticalWeight = 8f, float improvementFactor = 0.8f, float maxVerticalDelta = 6f)
    {
        // this is hopefully to avoid an issue like in the Eulmore where on a 2D plane the basement aethernet is closer
        if (MathF.Abs(shardPos.Y - destination.Y) > maxVerticalDelta)
            return false;

        var primaryCost = WeightedDistanceSquared(primaryAetherytePos, destination, verticalWeight);
        var shardCost = WeightedDistanceSquared(shardPos, destination, verticalWeight);
        return shardCost < primaryCost * improvementFactor;
    }

    private static float WeightedDistanceSquared(in Vector3 a, in Vector3 b, float verticalWeight)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        var dy = (a.Y - b.Y) * verticalWeight;
        return dx * dx + dz * dz + dy * dy;
    }

    public static Vector3 AetherytePosition(Aetheryte a)
    {
        // stolen from HTA, uses pixel coordinates
        var level = a.Level[0].ValueNullable;
        if (level != null)
            return new(level.Value.X, level.Value.Y, level.Value.Z);
        var marker = Service.LuminaSheetSubrow<MapMarker>()!.Flatten().FirstOrNull(m => m.DataType == 3 && m.DataKey.RowId == a.RowId)
            ?? Service.LuminaSheetSubrow<MapMarker>()!.Flatten().First(m => m.DataType == 4 && m.DataKey.RowId == a.AethernetName.RowId);
        return PixelCoordsToWorldCoords(marker.X, marker.Y, a.Territory.Value.Map.RowId);
    }

    // if aetheryte is 'primary' (i.e. can be teleported to), return it; otherwise (i.e. aethernet shard) find and return primary aetheryte from same group
    public static uint FindPrimaryAetheryte(uint aetheryteId)
    {
        if (aetheryteId == 0)
            return 0;
        var row = Service.LuminaRow<Aetheryte>(aetheryteId)!.Value;
        if (row.IsAetheryte)
            return aetheryteId;
        var primary = Service.LuminaSheet<Aetheryte>()!.FirstOrNull(a => a.AethernetGroup == row.AethernetGroup);
        return primary?.RowId ?? 0;
    }
}
