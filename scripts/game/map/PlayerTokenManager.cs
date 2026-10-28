using System;
using System.Collections.Generic;
using System.Linq;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

public partial class PlayerTokenManager : Node3D
{
    [Signal]
    public delegate void TokenClickedEventHandler(long playerId);

    [Export]
    public PackedScene? TokenScene { get; set; }

    [Export(PropertyHint.Range, "0.1,0.6,0.01")]
    public float LayoutRadius { get; set; } = 0.29f;

    [Export(PropertyHint.Range, "0.05,0.3,0.01")]
    public float TokenRadius { get; set; } = 0.11f;

    [Export(PropertyHint.Range, "0.1,0.8,0.01")]
    public float TokenHeight { get; set; } = 0.3f;

    [Export(PropertyHint.Range, "0.05,0.6,0.01")]
    public float LiftHeight { get; set; } = 0.2f;

    private readonly Dictionary<long, PlayerToken3D> _tokensByPlayerId = new();
    private readonly HashSet<string> _reportedMissingCells = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<long, HexCell3D> _cellsById = new Dictionary<long, HexCell3D>();
    private PlayerViewResponseDto? _latestPlayerView;
    private long _currentUserId;

    public IReadOnlyDictionary<long, PlayerToken3D> TokensByPlayerId => _tokensByPlayerId;
    public int TokenCount => _tokensByPlayerId.Count;

    public void BindCells(IReadOnlyDictionary<long, HexCell3D> cellsById)
    {
        _cellsById = cellsById;
        SynchronizeNow();
    }

    public void SyncPlayerView(PlayerViewResponseDto playerView, long currentUserId)
    {
        _latestPlayerView = playerView;
        _currentUserId = currentUserId;
        SynchronizeNow();
    }

    public void NotifyMapReloading()
    {
        ClearTokenInstances();
    }

    public void NotifyMapLoaded()
    {
        _reportedMissingCells.Clear();
        SynchronizeNow();
    }

    public bool TryGetToken(long playerId, out PlayerToken3D token)
    {
        return _tokensByPlayerId.TryGetValue(playerId, out token!);
    }

    public void SetPlayerLifted(long playerId, bool lifted, bool immediate = false)
    {
        if (_tokensByPlayerId.TryGetValue(playerId, out var token) && GodotObject.IsInstanceValid(token))
        {
            token.SetLifted(lifted, immediate);
        }
    }

    public void CancelPendingClicks()
    {
        foreach (var token in _tokensByPlayerId.Values)
        {
            if (GodotObject.IsInstanceValid(token))
            {
                token.CancelPendingClick();
            }
        }
    }

    public override void _ExitTree()
    {
        ClearTokenInstances();
    }

    private void SynchronizeNow()
    {
        if (_cellsById.Count == 0
            || _latestPlayerView?.Players == null
            || TokenScene == null)
        {
            return;
        }

        var orderedPlayers = _latestPlayerView.Players
            .Where(player => player.PlayerId > 0)
            .OrderBy(player => player.TurnOrder)
            .ThenBy(player => player.PlayerId)
            .ToList();
        var activeIds = orderedPlayers.Select(player => player.PlayerId).ToHashSet();
        foreach (var staleId in _tokensByPlayerId.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            RemoveToken(staleId);
        }

        var playersByCell = new Dictionary<long, List<PlayerViewPlayerDto>>();
        for (var index = 0; index < orderedPlayers.Count; index++)
        {
            var player = orderedPlayers[index];
            var token = EnsureToken(player.PlayerId);
            token.ConfigureDimensions(TokenRadius, TokenHeight, LiftHeight);
            token.BindPlayer(player, player.UserId == _currentUserId, ResolveColorSlot(player, index));

            if (!player.CurrentCellId.HasValue
                || !_cellsById.TryGetValue(player.CurrentCellId.Value, out var cell))
            {
                HideDetachedToken(token);
                ReportMissingCellOnce(player);
                continue;
            }

            token.Visible = true;
            if (token.GetParent() != cell.TokenRoot)
            {
                token.Reparent(cell.TokenRoot, false);
            }

            if (!playersByCell.TryGetValue(player.CurrentCellId.Value, out var occupants))
            {
                occupants = [];
                playersByCell.Add(player.CurrentCellId.Value, occupants);
            }

            occupants.Add(player);
        }

        foreach (var pair in playersByCell)
        {
            ArrangeCell(pair.Key, pair.Value);
        }
    }

    private PlayerToken3D EnsureToken(long playerId)
    {
        if (_tokensByPlayerId.TryGetValue(playerId, out var existing) && GodotObject.IsInstanceValid(existing))
        {
            return existing;
        }

        var token = TokenScene!.Instantiate<PlayerToken3D>();
        token.Name = $"Player_{playerId}";
        AddChild(token);
        token.TokenClicked += OnTokenClicked;
        _tokensByPlayerId[playerId] = token;
        return token;
    }

    private void ArrangeCell(long cellId, IReadOnlyList<PlayerViewPlayerDto> players)
    {
        var ordered = players
            .OrderBy(player => player.TurnOrder)
            .ThenBy(player => player.PlayerId)
            .Take(6)
            .ToList();
        var positions = PlayerTokenLayout.Calculate(ordered.Count, LayoutRadius, TokenRadius);
        for (var index = 0; index < ordered.Count; index++)
        {
            if (_tokensByPlayerId.TryGetValue(ordered[index].PlayerId, out var token))
            {
                token.Position = positions[index];
            }
        }

        if (players.Count > 6)
        {
            GD.PushWarning($"格子 {cellId} 超过六名玩家，仅排列前六个棋子。");
        }
    }

    private void HideDetachedToken(PlayerToken3D token)
    {
        token.Visible = false;
        token.Position = Vector3.Zero;
        if (token.GetParent() != this)
        {
            token.Reparent(this, false);
        }
    }

    private void ReportMissingCellOnce(PlayerViewPlayerDto player)
    {
        var key = $"{player.PlayerId}:{player.CurrentCellId?.ToString() ?? "null"}";
        if (_reportedMissingCells.Add(key))
        {
            GD.PushWarning($"玩家 {player.PlayerId} 的 currentCellId 无法绑定到地图：{player.CurrentCellId?.ToString() ?? "null"}");
        }
    }

    private void RemoveToken(long playerId)
    {
        if (!_tokensByPlayerId.Remove(playerId, out var token) || !GodotObject.IsInstanceValid(token))
        {
            return;
        }

        token.TokenClicked -= OnTokenClicked;
        token.QueueFree();
    }

    private void ClearTokenInstances()
    {
        foreach (var token in _tokensByPlayerId.Values)
        {
            if (!GodotObject.IsInstanceValid(token))
            {
                continue;
            }

            token.TokenClicked -= OnTokenClicked;
            token.QueueFree();
        }

        _tokensByPlayerId.Clear();
    }

    private static int ResolveColorSlot(PlayerViewPlayerDto player, int fallbackIndex)
    {
        return player.TurnOrder > 0 ? player.TurnOrder - 1 : fallbackIndex;
    }

    private void OnTokenClicked(long playerId)
    {
        EmitSignal(SignalName.TokenClicked, playerId);
    }
}
