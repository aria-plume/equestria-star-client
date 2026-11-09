using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using EquestriaStar.Api;
using EquestriaStar.Game.Map;
using EquestriaStar.Game.UI;

namespace EquestriaStar.UI;

public partial class GameScreen
{
    private bool _itemSystemSubmitting;
    private Task _itemSystemTask = Task.CompletedTask;
    private PlayerItemsResponseDto? _itemViewerSnapshot;

    public bool IsItemSystemSubmitting => _itemSystemSubmitting;
    public PlayerItemsResponseDto? ItemViewerSnapshot => _itemViewerSnapshot;

    private void ResetItemSystemForStart()
    {
        _itemSystemSubmitting = false;
        _itemSystemTask = Task.CompletedTask;
        _itemViewerSnapshot = null;
    }

    private void StopItemSystem()
    {
        _itemSystemSubmitting = false;
        _itemViewerSnapshot = null;
    }

    private async Task LoadPlayerItemsForViewerCoreAsync(
        long gameId,
        long playerId,
        CardViewerKind kind,
        int generation,
        int requestSequence)
    {
        try
        {
            ApiResult<PlayerItemsResponseDto> result;
            try
            {
                result = await ApiClient.GetPlayerItemsAsync(gameId, playerId);
            }
            catch (Exception exception)
            {
                result = ApiResult<PlayerItemsResponseDto>.Failure(
                    $"道具列表请求异常：{exception.Message}",
                    "PLAYER_ITEMS_REQUEST_EXCEPTION"
                );
            }

            if (!IsCardResponseCurrent(generation, requestSequence, kind))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                _cardViewer.ShowError(kind, FirstNonEmpty(result.Message, null, "道具列表加载失败，请重试。"));
                return;
            }

            if (!ValidatePlayerItems(result.Data, gameId, playerId, out var validationError))
            {
                _cardViewer.ShowError(kind, validationError);
                return;
            }

            // 即使本地卡图存在问题，权威持有实例也应先完成同步，避免后续操作使用旧快照。
            _itemViewerSnapshot = result.Data;
            _inventorySnapshot = result.Data;

            if (!TryBuildItemViewerCards(result.Data, kind, out var cards, out var replacements, out var cardError))
            {
                _cardViewer.ShowError(kind, cardError);
                return;
            }

            _cardQualificationStateVersion = _latestPlayerView?.StateVersion ?? -1;
            _cardViewer.ShowCards(kind, cards, replacementCandidates: replacements);
        }
        finally
        {
            _cardRequestsInFlight = Math.Max(0, _cardRequestsInFlight - 1);
        }
    }

    public Task SubmitShopPurchaseAsync(long shelfSlotId, long itemId)
    {
        if (!CanStartItemWrite()
            || _cardViewerKind != CardViewerKind.ShopShelf
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content
            || !TryGetCurrentTurnPlayer(out var player)
            || !BuildCardActionAvailability(CardViewerKind.ShopShelf).Enabled
            || !_cardViewer.DisplayedCards.Any(card => card.ShelfSlotId == shelfSlotId && card.ItemId == itemId))
        {
            return _itemSystemTask;
        }

        var request = ItemSystemModel.CreateShopBuyRequest(
            player.PlayerId,
            _latestPlayerView!.StateVersion,
            shelfSlotId,
            CreateGameActionRequestId()
        );
        return StartItemWrite(request, ItemWriteKind.ShopBuy, CardViewerKind.ShopShelf);
    }

    public Task SubmitInventoryItemActionAsync(long playerItemId, long replacePlayerItemId = 0)
    {
        if (!CanStartItemWrite()
            || _cardViewerKind != CardViewerKind.Inventory
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content
            || !TryGetCurrentTurnPlayer(out var player)
            || _itemViewerSnapshot?.Items == null)
        {
            return _itemSystemTask;
        }

        var item = _itemViewerSnapshot.Items.FirstOrDefault(candidate => candidate.PlayerItemId == playerItemId);
        if (item == null || !ItemSystemModel.TryResolveHandAction(item, out var actionType, out _))
        {
            return _itemSystemTask;
        }

        var availability = BuildItemActionAvailability(item, CardViewerKind.Inventory);
        if (!availability.Enabled)
        {
            ShowGameStatusError(availability.TooltipText);
            return _itemSystemTask;
        }

        var equipped = _itemViewerSnapshot.Items
            .Where(candidate => ItemSystemModel.IsAt(candidate, ItemSystemModel.Equipped))
            .ToList();
        long? replacement = null;
        if (string.Equals(actionType, "EQUIP_ITEM", StringComparison.Ordinal))
        {
            if (equipped.Count >= 2)
            {
                if (replacePlayerItemId <= 0
                    || equipped.All(candidate => candidate.PlayerItemId != replacePlayerItemId))
                {
                    ShowGameStatusError("装备栏已满，请选择一个当前装备进行替换。");
                    return _itemSystemTask;
                }
                replacement = replacePlayerItemId;
            }
            else if (replacePlayerItemId > 0)
            {
                return _itemSystemTask;
            }
        }

        var request = ItemSystemModel.CreateItemRequest(
            player.PlayerId,
            _latestPlayerView!.StateVersion,
            actionType,
            playerItemId,
            replacement,
            CreateGameActionRequestId()
        );
        return StartItemWrite(request, ItemWriteKind.ItemAction, CardViewerKind.Inventory);
    }

    public Task SubmitUnequipItemAsync(long playerItemId)
    {
        if (!CanStartItemWrite()
            || _cardViewerKind != CardViewerKind.Equipment
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content
            || !TryGetCurrentTurnPlayer(out var player)
            || _itemViewerSnapshot?.Items == null)
        {
            return _itemSystemTask;
        }

        var item = _itemViewerSnapshot.Items.FirstOrDefault(candidate => candidate.PlayerItemId == playerItemId);
        if (item == null || !ItemSystemModel.IsAt(item, ItemSystemModel.Equipped))
        {
            return _itemSystemTask;
        }
        var availability = BuildItemActionAvailability(item, CardViewerKind.Equipment);
        if (!availability.Enabled)
        {
            ShowGameStatusError(availability.TooltipText);
            return _itemSystemTask;
        }

        var request = ItemSystemModel.CreateItemRequest(
            player.PlayerId,
            _latestPlayerView!.StateVersion,
            "UNEQUIP_ITEM",
            playerItemId,
            null,
            CreateGameActionRequestId()
        );
        return StartItemWrite(request, ItemWriteKind.ItemAction, CardViewerKind.Equipment);
    }

    private Task StartItemWrite(GameActionRequestDto request, ItemWriteKind kind, CardViewerKind sourceKind)
    {
        _itemSystemSubmitting = true;
        ExitMoveMode();
        _hud.SetActionControlsLocked(true);
        DisableOpenItemActions("正在提交道具操作……");
        _hudErrorState.Visible = false;
        _itemSystemTask = SubmitItemWriteCoreAsync(_gameId, request, kind, sourceKind, _generation);
        return _itemSystemTask;
    }

    private async Task SubmitItemWriteCoreAsync(
        long gameId,
        GameActionRequestDto request,
        ItemWriteKind kind,
        CardViewerKind sourceKind,
        int generation)
    {
        ApiResult<GameActionResponseDto> result;
        try
        {
            result = await ApiClient.SubmitGameActionAsync(gameId, request);
        }
        catch (Exception exception)
        {
            result = ApiResult<GameActionResponseDto>.Failure(
                $"道具行动请求异常：{exception.Message}",
                "ITEM_ACTION_REQUEST_EXCEPTION"
            );
        }

        try
        {
            if (!IsResponseCurrent(generation))
            {
                return;
            }

            if (!result.Ok || result.Data == null)
            {
                await RecoverAfterItemActionFailureAsync(result, sourceKind, request.PlayerId, generation);
                return;
            }

            var expectedAction = request.ActionType;
            if ((result.Data.GameId > 0 && result.Data.GameId != gameId)
                || (!string.IsNullOrWhiteSpace(result.Data.ActionType)
                    && !string.Equals(result.Data.ActionType, expectedAction, StringComparison.OrdinalIgnoreCase)))
            {
                ShowItemViewerError(sourceKind, "服务器返回了不匹配的道具行动结果。");
                return;
            }

            string? receiptError = null;
            if (kind == ItemWriteKind.ShopBuy)
            {
                var bought = result.Data.ShopBuy?.BoughtItem;
                if (bought?.PlayerItemId is null or <= 0 || bought.ItemId is null or <= 0)
                {
                    receiptError = "购买成功回执缺少 shopBuy.boughtItem 实例数据";
                }
            }
            else if (result.Data.ItemUse?.PlayerItemId != request.Payload["playerItemId"]?.GetValue<long>())
            {
                receiptError = "道具行动成功回执缺少匹配的 itemUse 实例数据";
            }

            _stateVersionFloor = Math.Max(_stateVersionFloor, result.Data.NewStateVersion);
            if (kind == ItemWriteKind.ShopBuy)
            {
                _shopStateVersionFloor = Math.Max(_shopStateVersionFloor, result.Data.NewStateVersion);
            }
            InvalidateAvailableActions();
            if (result.Data.PlayerViewState != null
                && ValidatePlayerView(result.Data.PlayerViewState, gameId, _currentUserId, out _)
                && !IsOlderPlayerView(result.Data.PlayerViewState))
            {
                ApplyPlayerView(result.Data.PlayerViewState, false);
            }

            await RefreshPlayerViewToVersionFloorAsync();
            if (result.Data.NextAvailableActions != null && _latestPlayerView != null)
            {
                ApplyAvailableActions(result.Data.NextAvailableActions, _latestPlayerView.StateVersion);
            }
            else
            {
                await RefreshAvailableActionsAsync(true);
            }

            var refreshError = await RefreshItemsAfterConfirmedActionAsync(
                gameId,
                request.PlayerId,
                sourceKind,
                kind == ItemWriteKind.ShopBuy,
                generation
            );
            if (!string.IsNullOrWhiteSpace(receiptError))
            {
                var suffix = string.IsNullOrWhiteSpace(refreshError)
                    ? "；已同步权威状态，请核对列表。"
                    : $"；同时{refreshError}";
                ShowItemViewerError(sourceKind, $"{ActionSuccessText(request.ActionType)}，但{receiptError}{suffix}");
            }
            else if (!string.IsNullOrWhiteSpace(refreshError))
            {
                ShowItemViewerError(sourceKind, $"{ActionSuccessText(request.ActionType)}，但{refreshError}");
            }
            else
            {
                LastPlayerViewError = "";
                _hudErrorState.Visible = false;
            }
        }
        finally
        {
            if (IsResponseCurrent(generation))
            {
                _itemSystemSubmitting = false;
                if (!IsWorkOverlayBlocking) _hud.SetActionControlsLocked(false);
                RefreshOpenCardActionAvailability();
            }
        }
    }

    private async Task<string?> RefreshItemsAfterConfirmedActionAsync(
        long gameId,
        long playerId,
        CardViewerKind sourceKind,
        bool refreshShop,
        int generation)
    {
        if (!IsResponseCurrent(generation))
        {
            return "页面或对局已经切换。";
        }

        var refreshErrors = new List<string>();
        var viewerOpen = GodotObject.IsInstanceValid(_cardViewer)
            && _cardViewer.IsOpen
            && _cardViewerKind == sourceKind;
        var itemSnapshotRefreshedByViewer = false;
        if (viewerOpen)
        {
            var sequence = ++_cardRequestSequence;
            _cardViewer.OpenLoading(sourceKind);
            _cardRequestsInFlight++;
            if (refreshShop)
            {
                await LoadShopShelfCoreAsync(gameId, playerId, generation, sequence);
                if (IsCardResponseCurrent(generation, sequence, sourceKind)
                    && _cardViewer.State == CardViewerState.Error)
                {
                    refreshErrors.Add($"货架刷新失败：{_cardViewer.ErrorText}");
                }
            }
            else
            {
                await LoadPlayerItemsForViewerCoreAsync(gameId, playerId, sourceKind, generation, sequence);
                itemSnapshotRefreshedByViewer = IsCardResponseCurrent(generation, sequence, sourceKind)
                    && _itemViewerSnapshot?.GameId == gameId
                    && _itemViewerSnapshot.PlayerId == playerId;
                if (IsCardResponseCurrent(generation, sequence, sourceKind)
                    && _cardViewer.State == CardViewerState.Error)
                {
                    refreshErrors.Add($"道具列表刷新失败：{_cardViewer.ErrorText}");
                }
            }
        }

        // 背包/装备弹层已经通过同一次 GET 更新快照时不重复请求；购买仍需独立同步入包实例。
        if (!itemSnapshotRefreshedByViewer)
        {
            ApiResult<PlayerItemsResponseDto> itemsResult;
            try
            {
                itemsResult = await ApiClient.GetPlayerItemsAsync(gameId, playerId);
            }
            catch (Exception exception)
            {
                itemsResult = ApiResult<PlayerItemsResponseDto>.Failure(
                    $"背包刷新异常：{exception.Message}",
                    "PLAYER_ITEMS_REFRESH_EXCEPTION"
                );
            }
            if (!IsResponseCurrent(generation))
            {
                return "页面或对局已经切换。";
            }
            if (!itemsResult.Ok || itemsResult.Data == null)
            {
                refreshErrors.Add($"背包刷新失败：{FirstNonEmpty(itemsResult.Message, null, "请重试读取。")}");
            }
            else if (!ValidatePlayerItems(itemsResult.Data, gameId, playerId, out var validationError))
            {
                refreshErrors.Add($"背包刷新失败：{validationError}");
            }
            else
            {
                _itemViewerSnapshot = itemsResult.Data;
                _inventorySnapshot = itemsResult.Data;
            }
        }

        return refreshErrors.Count == 0 ? null : string.Join("；", refreshErrors.Distinct());
    }

    private async Task RecoverAfterItemActionFailureAsync(
        ApiResult<GameActionResponseDto> result,
        CardViewerKind sourceKind,
        long playerId,
        int generation)
    {
        InvalidateAvailableActions();
        await RefreshPlayerViewAsync();
        await RefreshAvailableActionsAsync(true);
        await RefreshInventorySnapshotAfterRewardAsync(_gameId, playerId, generation);
        var unknown = result.ErrorCode is "REQUEST_TIMEOUT" or "NETWORK_TIMEOUT" or "NETWORK_ERROR"
            or "ITEM_ACTION_REQUEST_EXCEPTION" or "REQUEST_IN_PROGRESS";
        var prefix = unknown
            ? "操作结果未知，已保留原请求身份且不会自动重发。"
            : "操作未完成。";
        ShowItemViewerError(sourceKind, $"{prefix}{FirstNonEmpty(result.Message, null, "请刷新后重试。")}");
    }

    private bool TryBuildItemViewerCards(
        PlayerItemsResponseDto response,
        CardViewerKind kind,
        out List<CardViewerCard> cards,
        out List<CardViewerCard> replacements,
        out string error)
    {
        cards = [];
        replacements = [];
        foreach (var item in response.Items!)
        {
            if (!CardArtCatalog.TryGetItemTexture(item.ItemId!.Value, out var texture, out error) || texture == null)
            {
                return false;
            }

            var availability = BuildItemActionAvailability(item, kind);
            var card = new CardViewerCard(
                $"{item.PlayerItemId}",
                texture,
                ItemId: item.ItemId,
                PlayerItemId: item.PlayerItemId,
                ItemName: item.ItemName,
                ItemType: item.ItemType,
                Location: item.Location,
                ActionAvailability: availability
            );
            if (ItemSystemModel.IsAt(item, ItemSystemModel.Equipped))
            {
                replacements.Add(card);
            }
            if ((kind == CardViewerKind.Inventory && ItemSystemModel.IsAt(item, ItemSystemModel.Hand))
                || (kind == CardViewerKind.Equipment && ItemSystemModel.IsAt(item, ItemSystemModel.Equipped)))
            {
                cards.Add(card);
            }
        }
        error = "";
        return true;
    }

    private CardActionAvailability BuildItemActionAvailability(PlayerItemDto item, CardViewerKind kind)
    {
        var common = BuildItemCommonAvailability();
        if (!common.Enabled)
        {
            return common;
        }

        string actionType;
        if (kind == CardViewerKind.Equipment)
        {
            if (!ItemSystemModel.IsAt(item, ItemSystemModel.Equipped))
            {
                return new CardActionAvailability(false, "该实例当前不在装备区。");
            }
            actionType = "UNEQUIP_ITEM";
        }
        else if (!ItemSystemModel.TryResolveHandAction(item, out actionType, out _))
        {
            return new CardActionAvailability(false, "该道具类型或持有位置不支持当前操作。");
        }

        var action = _latestAvailableActions.FirstOrDefault(candidate =>
            string.Equals(candidate.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
        if (action == null)
        {
            return new CardActionAvailability(false, "服务端未返回对应操作资格。");
        }
        return action.Available
            ? new CardActionAvailability(true, "通用行动资格已满足，最终结果以服务端校验为准。")
            : new CardActionAvailability(false, FirstNonEmpty(action.ReasonText, action.Reason, "当前不能执行该道具操作。"));
    }

    private CardActionAvailability BuildItemCommonAvailability()
    {
        if (!_active || !IsGameConnectionReady)
        {
            return new CardActionAvailability(false, "对局连接尚未就绪。");
        }
        if (_latestPlayerView == null)
        {
            return new CardActionAvailability(false, "正在同步玩家状态……");
        }
        if (!string.Equals(_latestPlayerView.GameStatus, "RUNNING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_latestPlayerView.EngineState?.PausedReason, "ALL_OFFLINE", StringComparison.Ordinal))
        {
            return new CardActionAvailability(false, "当前对局状态不允许道具操作。");
        }
        if (_latestPlayerView.CurrentTurnUserId != _currentUserId)
        {
            return new CardActionAvailability(false, "当前不是你的回合。");
        }
        if (_itemSystemSubmitting || _questSystemSubmitting || _questAcceptSubmitting
            || _endTurnSubmitting || IsWorkActionInProgress || _moveState == PlayerMoveState.SubmittingMove)
        {
            return new CardActionAvailability(false, "正在处理其他业务行动……");
        }
        if (_availableActionsStateVersion != _latestPlayerView.StateVersion)
        {
            return new CardActionAvailability(false,
                _availableActionsRequestInProgress ? "正在同步操作资格……" : "操作资格同步失败，请稍后重试。");
        }
        return new CardActionAvailability(true, "");
    }

    private bool CanStartItemWrite() =>
        _active
        && IsGameConnectionReady
        && !_itemSystemSubmitting
        && !_questSystemSubmitting
        && !_questAcceptSubmitting
        && !_endTurnSubmitting
        && !IsWorkActionInProgress
        && _moveState != PlayerMoveState.SubmittingMove
        && _latestPlayerView != null;

    private void RefreshOpenItemActionAvailability()
    {
        if (_cardViewerKind is not (CardViewerKind.Inventory or CardViewerKind.Equipment)
            || !_cardViewer.IsOpen
            || _cardViewer.State != CardViewerState.Content
            || _itemViewerSnapshot?.Items == null)
        {
            return;
        }
        var map = _itemViewerSnapshot.Items
            .Where(item => item.PlayerItemId is > 0)
            .ToDictionary(
                item => item.PlayerItemId!.Value,
                item => BuildItemActionAvailability(item, _cardViewerKind)
            );
        _cardViewer.UpdateItemActionAvailabilities(map);
    }

    private void DisableOpenItemActions(string message)
    {
        if (_itemViewerSnapshot?.Items == null)
        {
            return;
        }
        var map = _itemViewerSnapshot.Items
            .Where(item => item.PlayerItemId is > 0)
            .ToDictionary(
                item => item.PlayerItemId!.Value,
                _ => new CardActionAvailability(false, message)
            );
        _cardViewer.UpdateItemActionAvailabilities(map);
    }

    private void HandlePlayerViewForItemSystem(PlayerViewResponseDto playerView)
    {
        if (_cardViewerKind is CardViewerKind.Inventory or CardViewerKind.Equipment
            && _cardViewer.IsOpen
            && !_itemSystemSubmitting)
        {
            RefreshOpenItemActionAvailability();
        }
    }

    private void ShowItemViewerError(CardViewerKind kind, string message)
    {
        ShowGameStatusError(message);
        if (_cardViewer.IsOpen && _cardViewerKind == kind)
        {
            _cardViewer.ShowError(kind, message);
        }
    }

    private static string ActionSuccessText(string actionType) => actionType switch
    {
        "SHOP_BUY_ITEM" => "购买已成功",
        "USE_ITEM" => "使用已成功",
        "EQUIP_ITEM" => "装备已成功",
        "UNEQUIP_ITEM" => "卸下已成功",
        _ => "操作已成功"
    };

    private enum ItemWriteKind
    {
        ShopBuy,
        ItemAction
    }
}
