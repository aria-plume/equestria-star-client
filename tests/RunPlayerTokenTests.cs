using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using Godot;

public partial class RunPlayerTokenTests : SceneTree
{
    private readonly List<string> _failures = [];

    public override void _Initialize()
    {
        Callable.From(BeginTests).CallDeferred();
    }

    private void BeginTests()
    {
        _ = RunTestsAsync();
    }

    private async Task RunTestsAsync()
    {
        VerifyLayoutRules();

        var cellScene = GD.Load<PackedScene>("res://scenes/game/map/HexCell3D.tscn");
        var tokenScene = GD.Load<PackedScene>("res://scenes/game/map/PlayerToken3D.tscn");
        AssertTrue(cellScene != null, "HexCell3D 场景必须可加载");
        AssertTrue(tokenScene != null, "PlayerToken3D 场景必须可加载");
        if (cellScene == null || tokenScene == null)
        {
            Finish();
            return;
        }

        var cell1 = CreateCell(cellScene, 101, "A101");
        var cell2 = CreateCell(cellScene, 102, "A102");
        Root.AddChild(cell1);
        Root.AddChild(cell2);

        var manager = new PlayerTokenManager { TokenScene = tokenScene };
        Root.AddChild(manager);
        await ToSignal(this, SignalName.ProcessFrame);
        manager.BindCells(new Dictionary<long, HexCell3D> { [101] = cell1, [102] = cell2 });

        var players = Enumerable.Range(1, 6)
            .Select(index => CreatePlayer(1000 + index, index, index, 101))
            .Reverse()
            .ToList();
        var view = CreateView(players);
        manager.SyncPlayerView(view, 1);
        await ToSignal(this, SignalName.ProcessFrame);

        AssertEqual(manager.TokenCount, 6, "六名玩家应生成六个棋子");
        VerifyStableManagerLayout(manager, players);
        VerifyTokenStructureAndInput(manager);

        var originalTokens = manager.TokensByPlayerId.ToDictionary(pair => pair.Key, pair => pair.Value);
        manager.SyncPlayerView(CreateView(players.Select(ClonePlayer).ToList()), 1);
        AssertEqual(manager.TokenCount, 6, "重复 player-view 不得重复创建棋子");
        foreach (var pair in originalTokens)
        {
            AssertTrue(ReferenceEquals(pair.Value, manager.TokensByPlayerId[pair.Key]), "重复同步必须复用棋子实例");
        }

        var beforeMovePositions = manager.TokensByPlayerId
            .Where(pair => pair.Key != 1001)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Position);
        players.Single(player => player.PlayerId == 1001).CurrentCellId = 102;
        manager.SyncPlayerView(CreateView(players), 1);
        AssertTrue(manager.TokensByPlayerId[1001].GetParent() == cell2.TokenRoot, "移动后棋子必须重新挂到目标格 TokenRoot");
        AssertEqual(manager.TokensByPlayerId[1001].Position, Vector3.Zero, "目标格只有一人时棋子应位于中心");
        AssertTrue(beforeMovePositions.Any(pair => pair.Value != manager.TokensByPlayerId[pair.Key].Position), "原格人数变化后应重新排列");

        players.RemoveAll(player => player.PlayerId == 1006);
        manager.SyncPlayerView(CreateView(players), 1);
        AssertEqual(manager.TokenCount, 5, "玩家离开后应删除对应棋子");

        manager.SetPlayerLifted(1001, true, true);
        var lifted = manager.TokensByPlayerId[1001];
        var layoutPosition = lifted.Position;
        AssertTrue(lifted.LiftRoot.Position.Y > 0.0f, "进入移动模式时 LiftRoot 应抬起");
        AssertEqual(lifted.Position, layoutPosition, "抬起状态不得覆盖多人排列位置");
        manager.SetPlayerLifted(1001, false, true);
        AssertApprox(lifted.LiftRoot.Position.Y, 0.0f, 0.0001f, "退出移动模式时棋子应落下");

        var cachedManager = new PlayerTokenManager { TokenScene = tokenScene };
        Root.AddChild(cachedManager);
        await ToSignal(this, SignalName.ProcessFrame);
        cachedManager.SyncPlayerView(CreateView([CreatePlayer(2001, 1, 1, 101)]), 1);
        AssertEqual(cachedManager.TokenCount, 0, "地图未加载时只应缓存 player-view");
        cachedManager.BindCells(new Dictionary<long, HexCell3D> { [101] = cell1, [102] = cell2 });
        AssertEqual(cachedManager.TokenCount, 1, "地图加载后应自动同步缓存的 player-view");

        manager.NotifyMapReloading();
        AssertEqual(manager.TokenCount, 0, "地图重新加载时应清理旧格子上的棋子引用");
        manager.BindCells(new Dictionary<long, HexCell3D> { [101] = cell1, [102] = cell2 });
        manager.NotifyMapLoaded();
        AssertEqual(manager.TokenCount, 5, "地图重新加载后应从缓存状态重新绑定棋子");

        manager.QueueFree();
        cachedManager.QueueFree();
        cell1.QueueFree();
        cell2.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        Finish();
    }

    private void VerifyLayoutRules()
    {
        const float tokenRadius = 0.11f;
        for (var count = 1; count <= 6; count++)
        {
            var positions = PlayerTokenLayout.Calculate(count, 0.29f, tokenRadius);
            AssertEqual(positions.Count, count, $"{count} 人布局数量必须正确");
            for (var first = 0; first < positions.Count; first++)
            {
                for (var second = first + 1; second < positions.Count; second++)
                {
                    AssertTrue(
                        positions[first].DistanceTo(positions[second]) > tokenRadius * 2.0f,
                        $"{count} 人布局中的棋子不得重叠"
                    );
                }
            }
        }

        AssertEqual(PlayerTokenLayout.Calculate(1, 0.29f, tokenRadius)[0], Vector3.Zero, "单人必须位于中心");
        var pair = PlayerTokenLayout.Calculate(2, 0.29f, tokenRadius);
        AssertVectorApprox(pair[0], -pair[1], 0.0001f, "双人布局必须中心对称");
    }

    private void VerifyStableManagerLayout(PlayerTokenManager manager, IReadOnlyList<PlayerViewPlayerDto> players)
    {
        var sorted = players.OrderBy(player => player.TurnOrder).ThenBy(player => player.PlayerId).ToList();
        var expected = PlayerTokenLayout.Calculate(sorted.Count, manager.LayoutRadius, manager.TokenRadius);
        for (var index = 0; index < sorted.Count; index++)
        {
            var token = manager.TokensByPlayerId[sorted[index].PlayerId];
            AssertVectorApprox(token.Position, expected[index], 0.0001f, "棋子排列必须按 turnOrder/playerId 稳定排序");
            AssertEqual(token.ColorSlot, sorted[index].TurnOrder - 1, "颜色槽必须由 turnOrder 稳定决定");
        }
    }

    private void VerifyTokenStructureAndInput(PlayerTokenManager manager)
    {
        var current = manager.TokensByPlayerId[1001];
        var other = manager.TokensByPlayerId[1002];
        foreach (var path in new[]
        {
            "CurrentPlayerIndicator",
            "LiftRoot",
            "LiftRoot/VisualRoot/CylinderMesh",
            "LiftRoot/InteractionArea/CollisionShape3D",
            "LiftRoot/NameLabel"
        })
        {
            AssertTrue(current.HasNode(path), $"PlayerToken3D 缺少节点：{path}");
        }

        AssertTrue(current.GetNode<MeshInstance3D>("CurrentPlayerIndicator").Visible, "当前玩家必须显示底部指示环");
        AssertTrue(current.GetNode<Area3D>("LiftRoot/InteractionArea").InputRayPickable, "当前玩家棋子必须可点击");
        AssertFalse(other.GetNode<Area3D>("LiftRoot/InteractionArea").InputRayPickable, "其他玩家棋子不得阻挡底层格子点击");
        AssertFalse(current.GetNode<Label3D>("LiftRoot/NameLabel").Visible, "NameLabel 默认必须隐藏");

        var clickedPlayerId = 0L;
        manager.TokenClicked += playerId => clickedPlayerId = playerId;
        AssertFalse(other.TryActivate(), "其他玩家棋子不得发送有效点击");
        AssertTrue(current.TryActivate(), "当前玩家棋子应允许点击");
        AssertEqual(clickedPlayerId, 1001L, "棋子点击必须携带 game playerId");
    }

    private static HexCell3D CreateCell(PackedScene scene, long id, string code)
    {
        var cell = scene.Instantiate<HexCell3D>();
        cell.BindData(new HexCellData { CellId = id, CellCode = code, CellName = code, Q = 0, R = 0 });
        return cell;
    }

    private static PlayerViewResponseDto CreateView(List<PlayerViewPlayerDto> players)
    {
        return new PlayerViewResponseDto
        {
            GameId = 77,
            GameStatus = "RUNNING",
            CurrentTurnUserId = 1,
            StateVersion = 1,
            Players = players
        };
    }

    private static PlayerViewPlayerDto CreatePlayer(long playerId, long userId, int turnOrder, long cellId)
    {
        return new PlayerViewPlayerDto
        {
            PlayerId = playerId,
            UserId = userId,
            Nickname = $"玩家{userId}",
            TurnOrder = turnOrder,
            CurrentCellId = cellId,
            CurrentSpeedPoints = 3
        };
    }

    private static PlayerViewPlayerDto ClonePlayer(PlayerViewPlayerDto player)
    {
        return new PlayerViewPlayerDto
        {
            PlayerId = player.PlayerId,
            UserId = player.UserId,
            Nickname = player.Nickname,
            TurnOrder = player.TurnOrder,
            CurrentCellId = player.CurrentCellId,
            CurrentSpeedPoints = player.CurrentSpeedPoints
        };
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("玩家棋子测试全部通过。");
            Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PushError(failure);
        }
        Quit(1);
    }

    private void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            _failures.Add(message);
        }
    }

    private void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    private void AssertEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }

    private void AssertApprox(float actual, float expected, float tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            _failures.Add($"{message}，期望：{expected}±{tolerance}，实际：{actual}");
        }
    }

    private void AssertVectorApprox(Vector3 actual, Vector3 expected, float tolerance, string message)
    {
        if (actual.DistanceTo(expected) > tolerance)
        {
            _failures.Add($"{message}，期望：{expected}，实际：{actual}");
        }
    }
}
