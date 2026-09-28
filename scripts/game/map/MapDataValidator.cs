using System;
using System.Collections.Generic;
using System.Linq;
using EquestriaStar.Api;

namespace EquestriaStar.Game.Map;

public sealed record MapValidationResult(bool IsValid, string Error)
{
    public static MapValidationResult Success { get; } = new(true, "");

    public static MapValidationResult Failure(string error)
    {
        return new MapValidationResult(false, error);
    }
}

public static class MapDataValidator
{
    public const int ExpectedCellCount = 138;
    public const int ExpectedEdgeCount = 385;

    private static readonly IReadOnlyDictionary<string, (int Q, int R)> CriticalCoordinates =
        new Dictionary<string, (int Q, int R)>(StringComparer.Ordinal)
        {
            ["A10"] = (1, 5),
            ["B24"] = (-6, 10),
            ["I1"] = (-6, 11),
            ["I4"] = (-3, 11),
            ["I5"] = (-6, 12),
            ["I7"] = (-4, 12)
        };

    private static readonly IReadOnlyDictionary<string, int> ExpectedEdgeTypes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["NORMAL"] = 188,
            ["EVERFREE"] = 86,
            ["STATION_ROUTE"] = 13,
            ["GAP"] = 98
        };

    public static MapValidationResult Validate(GameMapDto? map)
    {
        if (map == null)
        {
            return MapValidationResult.Failure("地图 data 为空。");
        }

        if (map.Cells == null)
        {
            return MapValidationResult.Failure("地图 cells 为空。");
        }

        if (map.Edges == null)
        {
            return MapValidationResult.Failure("地图 edges 为空。");
        }

        if (map.Cells.Count != ExpectedCellCount)
        {
            return MapValidationResult.Failure($"地图格子数量应为 {ExpectedCellCount}，实际为 {map.Cells.Count}。");
        }

        if (map.Edges.Count != ExpectedEdgeCount)
        {
            return MapValidationResult.Failure($"地图边数量应为 {ExpectedEdgeCount}，实际为 {map.Edges.Count}。");
        }

        var ids = new HashSet<long>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var coordinates = new HashSet<(int Q, int R)>();
        var cellsByCode = new Dictionary<string, MapCellDto>(StringComparer.Ordinal);
        foreach (var cell in map.Cells)
        {
            if (!ids.Add(cell.CellId))
            {
                return MapValidationResult.Failure($"存在重复 cellId：{cell.CellId}。");
            }

            if (string.IsNullOrWhiteSpace(cell.CellCode) || !codes.Add(cell.CellCode))
            {
                return MapValidationResult.Failure($"cellCode 为空或重复：{cell.CellCode}。");
            }

            if (!cell.Q.HasValue || !cell.R.HasValue)
            {
                return MapValidationResult.Failure($"格子 {cell.CellCode} 缺少 q/r 坐标。");
            }

            if (!coordinates.Add((cell.Q.Value, cell.R.Value)))
            {
                return MapValidationResult.Failure($"格子 {cell.CellCode} 使用了重复 QR 坐标 ({cell.Q},{cell.R})。");
            }

            if (!MapLayoutConfig.IsKnownBoard(cell.BoardCode))
            {
                return MapValidationResult.Failure($"格子 {cell.CellCode} 使用未知 boardCode：{cell.BoardCode}。");
            }

            if (cell.CellCode.Length < 2 || cell.CellCode[0].ToString() != cell.BoardCode)
            {
                return MapValidationResult.Failure($"格子 {cell.CellCode} 与 boardCode {cell.BoardCode} 不匹配。");
            }

            if (cell.CellCode == "B34")
            {
                return MapValidationResult.Failure("正式地图不得包含 B34。");
            }

            cellsByCode.Add(cell.CellCode, cell);
        }

        foreach (var (cellCode, expected) in CriticalCoordinates)
        {
            if (!cellsByCode.TryGetValue(cellCode, out var cell))
            {
                return MapValidationResult.Failure($"正式地图缺少关键格子 {cellCode}。");
            }

            if (cell.Q != expected.Q || cell.R != expected.R)
            {
                return MapValidationResult.Failure(
                    $"关键格子 {cellCode} 坐标应为 ({expected.Q},{expected.R})，实际为 ({cell.Q},{cell.R})。"
                );
            }
        }

        var edgePairs = new HashSet<(long A, long B)>();
        foreach (var edge in map.Edges)
        {
            if (!ids.Contains(edge.FromCellId) || !ids.Contains(edge.ToCellId))
            {
                return MapValidationResult.Failure(
                    $"边 {edge.EdgeId} 引用了不存在的格子：{edge.FromCellId} -> {edge.ToCellId}。"
                );
            }

            if (edge.FromCellId == edge.ToCellId)
            {
                return MapValidationResult.Failure($"边 {edge.EdgeId} 的起点和终点相同。");
            }

            var pair = edge.FromCellId < edge.ToCellId
                ? (edge.FromCellId, edge.ToCellId)
                : (edge.ToCellId, edge.FromCellId);
            if (!edgePairs.Add(pair))
            {
                return MapValidationResult.Failure(
                    $"格子 {pair.Item1} 与 {pair.Item2} 之间存在重复无向边。"
                );
            }
        }

        var typeCounts = map.Edges
            .GroupBy(edge => edge.EdgeType ?? "", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var (edgeType, expectedCount) in ExpectedEdgeTypes)
        {
            typeCounts.TryGetValue(edgeType, out var actualCount);
            if (actualCount != expectedCount)
            {
                return MapValidationResult.Failure(
                    $"边类型 {edgeType} 数量应为 {expectedCount}，实际为 {actualCount}。"
                );
            }
        }

        return MapValidationResult.Success;
    }
}
