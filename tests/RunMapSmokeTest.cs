using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using Godot;

public partial class RunMapSmokeTest : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTest).CallDeferred();
    }

    private void BeginTest()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var client = Root.GetNode<ApiClient>("ApiClient");
        var result = await client.GetMainMapAsync();
        if (!result.Ok || result.Data == null)
        {
            Fail($"正式地图接口请求失败：{result.Message}");
            Finish();
            return;
        }

        var validation = MapDataValidator.Validate(result.Data);
        if (!validation.IsValid)
        {
            Fail($"正式地图校验失败：{validation.Error}");
        }

        AssertEqual(result.Data.Cells?.Count ?? 0, 138, "正式地图格子数");
        AssertEqual(result.Data.Edges?.Count ?? 0, 385, "正式地图边数");
        AssertEqual(result.Data.Edges?.Count(edge => edge.EdgeType == "GAP") ?? 0, 98, "GAP 边数");
        AssertFalse(result.Data.Cells?.Any(cell => cell.CellCode == "B34") == true, "正式地图不得包含 B34");
        AssertEqual(
            result.Data.Cells?.Where(cell => cell.Q.HasValue && cell.R.HasValue).Select(cell => (cell.Q, cell.R)).Distinct().Count() ?? 0,
            138,
            "正式地图 QR 坐标唯一数"
        );
        Finish();
    }

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            Fail($"{message}不匹配，期望：{expected}，实际：{actual}");
        }
    }

    private void AssertFalse(bool value, string message)
    {
        if (value)
        {
            Fail(message);
        }
    }

    private void Fail(string message)
    {
        _failures.Add(message);
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("正式地图线上冒烟测试通过：138 格、385 边、98 条 GAP、无 B34、QR 唯一。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }
        Quit(1);
    }
}
