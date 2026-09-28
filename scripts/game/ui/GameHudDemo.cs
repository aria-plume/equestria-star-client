using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public partial class GameHudDemo : Control
{
    private const long CurrentUserId = 20;
    private GameHud _hud = null!;
    private Label _demoStatusLabel = null!;
    private PlayerViewResponseDto _view = null!;
    private int _refreshCount;

    public override void _Ready()
    {
        _hud = GetNode<GameHud>("GameHud");
        _demoStatusLabel = GetNode<Label>("DemoControls/Margin/Content/StatusLabel");
        _view = CreateDemoView();
        _hud.BindPlayerView(_view, CurrentUserId);

        GetNode<Button>("DemoControls/Margin/Content/Buttons/RefreshButton").Pressed += RefreshDemoData;
        GetNode<Button>("DemoControls/Margin/Content/Buttons/RebindButton").Pressed += RebindDemoData;
        UpdateDemoStatus(VerifyDemoBinding() ? "模拟数据绑定自检通过" : "模拟数据绑定自检失败");
        GD.Print($"GameHudDemo: 窗口 {DisplayServer.WindowGetSize()}，设计视口 {GetViewportRect().Size}");
        Callable.From(PreviewFirstOtherPlayer).CallDeferred();
    }

    private void RefreshDemoData()
    {
        _refreshCount++;
        var currentPlayer = _view.Players![1];
        currentPlayer.Coins += 2;
        currentPlayer.Prestige += 1;
        currentPlayer.EffectiveAttributes!.Magic += 1;

        var otherPlayer = _view.Players[0];
        otherPlayer.Coins += 1;
        otherPlayer.EffectiveAttributes!.Speed += 1;

        _hud.RefreshPlayerView(_view, CurrentUserId);
        UpdateDemoStatus($"已刷新 {_refreshCount} 次，列表保持 2 名其他玩家");
    }

    private void RebindDemoData()
    {
        _hud.BindPlayerView(_view, CurrentUserId);
        UpdateDemoStatus($"重复绑定完成，其他玩家条目：{_hud.OtherPlayerCount}");
    }

    private void UpdateDemoStatus(string message)
    {
        _demoStatusLabel.Text = message;
        GD.Print($"GameHudDemo: {message}");
    }

    private void PreviewFirstOtherPlayer()
    {
        var firstEntry = _hud
            .GetNode<VBoxContainer>("HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/ContentMargin/Content/OtherPlayersScroll/OtherPlayersList")
            .GetChildren()
            .OfType<OtherPlayerHudEntry>()
            .FirstOrDefault();
        if (firstEntry != null)
        {
            Input.WarpMouse(firstEntry.GetGlobalRect().GetCenter());
        }
    }

    private bool VerifyDemoBinding()
    {
        var currentNickname = _hud.GetNode<Label>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/NicknameLabel"
        );
        var currentCoins = _hud.GetNode<Label>(
            "HudRoot/LeftMargin/LeftRail/CurrentPlayerPanel/ContentMargin/Content/ResourceRow/CoinsValue"
        );
        var playerList = _hud.GetNode<VBoxContainer>(
            "HudRoot/LeftMargin/LeftRail/OtherPlayersPanel/ContentMargin/Content/OtherPlayersScroll/OtherPlayersList"
        );

        var currentPlayer = _view.Players![1];
        var originalCoins = currentPlayer.Coins;
        currentPlayer.Coins = 19;
        _hud.RefreshPlayerView(_view, CurrentUserId);
        var refreshSucceeded = currentCoins.Text == "19";
        currentPlayer.Coins = originalCoins;
        _hud.RefreshPlayerView(_view, CurrentUserId);

        _hud.BindPlayerView(_view, CurrentUserId);
        var otherEntries = playerList.GetChildren().OfType<OtherPlayerHudEntry>().ToList();
        var succeeded = refreshSucceeded
            && currentNickname.Text == "星辉旅人"
            && _hud.BoundCurrentUserId == CurrentUserId
            && _hud.OtherPlayerCount == 2
            && otherEntries.Count == 2
            && otherEntries[0].Player?.UserId == 10
            && otherEntries[1].Player?.UserId == 30;

        if (!succeeded)
        {
            GD.PushError("GameHudDemo 模拟数据绑定自检失败。");
        }

        return succeeded;
    }

    private static PlayerViewResponseDto CreateDemoView()
    {
        return new PlayerViewResponseDto
        {
            GameId = 100,
            RoomId = 10,
            GameStatus = "RUNNING",
            GameStatusText = "进行中",
            CurrentRound = 2,
            CurrentTurnUserId = 10,
            CurrentTurnSeq = 4,
            StateVersion = 12,
            Players = new List<PlayerViewPlayerDto>
            {
                CreatePlayer(1001, 10, "晨曦邮差", 1, 7, 3, 3, 5, 2, 4, 1, 4),
                CreatePlayer(1002, CurrentUserId, "星辉旅人", 2, 11, 6, 4, 3, 5, 2, 6, 4),
                CreatePlayer(1003, 30, "云端观测员的超长昵称示例", 3, 4, 8, 5, 2, 4, 6, 3, 5)
            }
        };
    }

    private static PlayerViewPlayerDto CreatePlayer(
        long playerId,
        long userId,
        string nickname,
        int turnOrder,
        int coins,
        int prestige,
        int strength,
        int speed,
        int social,
        int charm,
        int magic,
        int mind)
    {
        return new PlayerViewPlayerDto
        {
            PlayerId = playerId,
            UserId = userId,
            Nickname = nickname,
            TurnOrder = turnOrder,
            Coins = coins,
            Prestige = prestige,
            EquivalentPrestige = prestige + 2,
            EffectiveAttributes = new EffectiveAttributesDto
            {
                Strength = strength,
                Speed = speed,
                Social = social,
                Charm = charm,
                Magic = magic,
                Mind = mind
            }
        };
    }
}
