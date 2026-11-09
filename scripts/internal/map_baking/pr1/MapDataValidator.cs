using System;
using System.Collections.Generic;
using System.Linq;
using EquestriaStar.Api;

namespace EquestriaStar.Internal.MapBaking.Pr1;

public sealed record MapValidationResult(bool IsValid, string Error)
{
    public static MapValidationResult Success { get; } = new(true, "");
    public static MapValidationResult Failure(string error) => new(false, error);
}

/// <summary>隔离制图器使用的正式地图契约校验，不发起任何网络请求。</summary>
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
        if (map?.Cells == null || map.Edges == null)
            return MapValidationResult.Failure("地图 data、cells 或 edges 为空。");
        if (map.Cells.Count != ExpectedCellCount || map.Edges.Count != ExpectedEdgeCount)
            return MapValidationResult.Failure($"地图必须为 {ExpectedCellCount} 格/{ExpectedEdgeCount} 边，实际 {map.Cells.Count}/{map.Edges.Count}。");

        var ids = new HashSet<long>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var coordinates = new HashSet<(int Q, int R)>();
        var cellsByCode = new Dictionary<string, MapCellDto>(StringComparer.Ordinal);
        foreach (var cell in map.Cells)
        {
            if (!ids.Add(cell.CellId)) return MapValidationResult.Failure($"重复 cellId：{cell.CellId}。");
            if (string.IsNullOrWhiteSpace(cell.CellCode) || !codes.Add(cell.CellCode))
                return MapValidationResult.Failure($"cellCode 为空或重复：{cell.CellCode}。");
            if (!cell.Q.HasValue || !cell.R.HasValue || !coordinates.Add((cell.Q.Value, cell.R.Value)))
                return MapValidationResult.Failure($"格子 {cell.CellCode} 缺少或重复 QR 坐标。");
            if (!MapLayoutConfig.IsKnownBoard(cell.BoardCode))
                return MapValidationResult.Failure($"格子 {cell.CellCode} 的 boardCode 无效：{cell.BoardCode}。");
            if (cell.CellCode.Length < 2 || cell.CellCode[0].ToString() != cell.BoardCode || cell.CellCode == "B34")
                return MapValidationResult.Failure($"格子编码与板块不匹配或出现 B34：{cell.CellCode}。");
            cellsByCode.Add(cell.CellCode, cell);
        }

        foreach (var (code, expected) in CriticalCoordinates)
        {
            if (!cellsByCode.TryGetValue(code, out var cell) || cell.Q != expected.Q || cell.R != expected.R)
                return MapValidationResult.Failure($"关键格子 {code} 坐标不符合 ({expected.Q},{expected.R})。");
        }

        var pairs = new HashSet<(long A, long B)>();
        foreach (var edge in map.Edges)
        {
            if (!ids.Contains(edge.FromCellId) || !ids.Contains(edge.ToCellId) || edge.FromCellId == edge.ToCellId)
                return MapValidationResult.Failure($"边 {edge.EdgeId} 引用无效格子。");
            var pair = edge.FromCellId < edge.ToCellId
                ? (edge.FromCellId, edge.ToCellId)
                : (edge.ToCellId, edge.FromCellId);
            if (!pairs.Add(pair)) return MapValidationResult.Failure($"重复无向边：{pair}。");
        }

        var typeCounts = map.Edges.GroupBy(edge => edge.EdgeType ?? "", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var (type, expected) in ExpectedEdgeTypes)
            if (!typeCounts.TryGetValue(type, out var actual) || actual != expected)
                return MapValidationResult.Failure($"边类型 {type} 数量应为 {expected}，实际 {actual}。");

        return MapValidationResult.Success;
    }
}
