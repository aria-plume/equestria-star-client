using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Game.UI;

public enum CardViewerKind
{
    None,
    QuestBoard,
    ShopShelf,
    PersonalQuests,
    Inventory,
    Equipment
}

public enum CardViewerState
{
    Closed,
    Loading,
    Content,
    Error
}

public sealed record CardViewerCard(
    string Key,
    Texture2D Texture,
    long? QuestId = null,
    long? OfferId = null,
    long? ShelfSlotId = null,
    long? ItemId = null,
    long? PlayerQuestId = null,
    long? PlayerItemId = null,
    string? ItemName = null,
    string? ItemType = null,
    string? Location = null,
    CardActionAvailability? ActionAvailability = null
);

public sealed record CardActionAvailability(bool Enabled, string TooltipText)
{
    public static CardActionAvailability Loading { get; } = new(false, "正在同步操作资格……");
}

/// <summary>
/// 通用卡牌查看层。它只负责显示、确认交互和消费 UI 输入，不读取 Session 或发起网络请求。
/// </summary>
public partial class CardViewerOverlay : CanvasLayer
{
    [Signal]
    public delegate void ClosedEventHandler();

    [Signal]
    public delegate void RetryRequestedEventHandler();

    [Signal]
    public delegate void CardActionPressedEventHandler(int kind, long primaryId, long secondaryId);

    private const int MaximumVisibleCards = 3;
    private const float QuestAspectRatio = 2161.0f / 1453.0f;
    private const float ItemAspectRatio = 1453.0f / 2161.0f;
    private const float CloseDragThreshold = 8.0f;

    [Export]
    public PackedScene? ReadOnlyQuestCardScene { get; set; }

    [Export]
    public PackedScene? InventoryItemCardScene { get; set; }

    private Control _overlayRoot = null!;
    private Label _titleLabel = null!;
    private Label _hintLabel = null!;
    private Control _loadingState = null!;
    private Control _contentState = null!;
    private Label _emptyLabel = null!;
    private HBoxContainer _cardRow = null!;
    private ScrollContainer _personalQuestScroll = null!;
    private HBoxContainer _personalQuestRow = null!;
    private Control _errorState = null!;
    private Label _errorLabel = null!;
    private Button _retryButton = null!;
    private Control _decisionLayer = null!;
    private Label _decisionMessage = null!;
    private VBoxContainer _decisionOptions = null!;
    private Button _decisionConfirm = null!;
    private Button _decisionCancel = null!;
    private readonly List<VBoxContainer> _cardSlots = [];
    private readonly List<AspectRatioContainer> _cardAspects = [];
    private readonly List<TextureRect> _cardTextures = [];
    private readonly List<PanelContainer> _actionAreas = [];
    private readonly List<Button> _actionButtons = [];
    private readonly List<string> _displayedKeys = [];
    private readonly List<CardViewerCard> _displayedCards = [];
    private readonly List<AspectRatioContainer> _personalQuestCards = [];
    private readonly List<InventoryItemCard> _itemCards = [];
    private readonly List<CardViewerCard> _replacementCandidates = [];
    private CardActionAvailability _actionAvailability = CardActionAvailability.Loading;
    private long _decisionPlayerItemId;
    private bool _closePointerPressed;
    private bool _closePointerDragged;
    private Vector2 _closePointerStart;

    public CardViewerKind Kind { get; private set; }
    public CardViewerState State { get; private set; } = CardViewerState.Closed;
    public bool IsOpen => IsNodeReady() && _overlayRoot.Visible;
    public int VisibleCardCount => IsScrollableKind(Kind)
        ? _personalQuestCards.Count + _itemCards.Count
        : _cardSlots.Count(slot => slot.Visible);
    public IReadOnlyList<string> DisplayedKeys => _displayedKeys;
    public IReadOnlyList<CardViewerCard> DisplayedCards => _displayedCards;
    public bool RetryVisible => IsNodeReady() && _retryButton.Visible;
    public string ErrorText => IsNodeReady() ? _errorLabel.Text : "";
    public Control InputBlocker => _overlayRoot;
    public ScrollContainer PersonalQuestScroll => _personalQuestScroll;
    public bool DecisionVisible => IsNodeReady() && _decisionLayer.Visible;
    public IReadOnlyList<InventoryItemCard> ItemCards => _itemCards;

    public override void _Ready()
    {
        ReadOnlyQuestCardScene ??= GD.Load<PackedScene>("res://scenes/game/ui/ReadOnlyQuestCard.tscn");
        InventoryItemCardScene ??= GD.Load<PackedScene>("res://scenes/game/ui/InventoryItemCard.tscn");
        _overlayRoot = GetNode<Control>("OverlayRoot");
        _titleLabel = GetNode<Label>("OverlayRoot/Center/Layout/TitleLabel");
        _hintLabel = GetNode<Label>("OverlayRoot/Center/Layout/HintLabel");
        _loadingState = GetNode<Control>("OverlayRoot/Center/Layout/LoadingState");
        _contentState = GetNode<Control>("OverlayRoot/Center/Layout/ContentState");
        _emptyLabel = GetNode<Label>("OverlayRoot/Center/Layout/ContentState/EmptyLabel");
        _cardRow = GetNode<HBoxContainer>("OverlayRoot/Center/Layout/ContentState/CardRow");
        _personalQuestScroll = GetNode<ScrollContainer>("OverlayRoot/Center/Layout/ContentState/PersonalQuestScroll");
        _personalQuestRow = GetNode<HBoxContainer>("OverlayRoot/Center/Layout/ContentState/PersonalQuestScroll/PersonalQuestRow");
        _errorState = GetNode<Control>("OverlayRoot/Center/Layout/ErrorState");
        _errorLabel = GetNode<Label>("OverlayRoot/Center/Layout/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/ErrorLabel");
        _retryButton = GetNode<Button>("OverlayRoot/Center/Layout/ErrorState/ErrorPanel/ErrorMargin/ErrorContent/RetryButton");
        _decisionLayer = GetNode<Control>("OverlayRoot/DecisionLayer");
        _decisionMessage = GetNode<Label>("OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Message");
        _decisionOptions = GetNode<VBoxContainer>("OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/ReplacementOptions");
        _decisionConfirm = GetNode<Button>("OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Actions/ConfirmButton");
        _decisionCancel = GetNode<Button>("OverlayRoot/DecisionLayer/Center/Panel/Margin/Content/Actions/CancelButton");

        for (var index = 1; index <= MaximumVisibleCards; index++)
        {
            var slot = GetNode<VBoxContainer>($"OverlayRoot/Center/Layout/ContentState/CardRow/CardSlot{index}");
            var cardAspect = slot.GetNode<AspectRatioContainer>("CardAspect");
            var actionArea = slot.GetNode<PanelContainer>("ActionArea");
            var actionButton = actionArea.GetNode<Button>("ActionButton");
            var slotIndex = index - 1;
            _cardSlots.Add(slot);
            _cardAspects.Add(cardAspect);
            _cardTextures.Add(cardAspect.GetNode<TextureRect>("CardTexture"));
            _actionAreas.Add(actionArea);
            _actionButtons.Add(actionButton);
            actionArea.GuiInput += inputEvent => OnActionAreaGuiInput(slotIndex, inputEvent);
            // Button 必须自行完成按下/释放状态机；在它的 GuiInput 中提前 AcceptEvent 会吞掉 Pressed。
            // 父级 ActionArea 仍负责消费按钮周边及灰色区域输入，Button 自身的 Stop 过滤负责阻止穿透。
            actionButton.Pressed += () => OnActionButtonPressed(slotIndex);
        }

        _overlayRoot.GuiInput += OnOverlayGuiInput;
        _personalQuestScroll.GuiInput += OnPersonalQuestScrollGuiInput;
        _retryButton.Pressed += OnRetryPressed;
        _decisionConfirm.Pressed += OnDecisionConfirmed;
        _decisionCancel.Pressed += CloseDecision;
        GetViewport().SizeChanged += OnViewportSizeChanged;
        _overlayRoot.Visible = false;
        ApplyState(CardViewerState.Closed);
        CloseDecision();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_overlayRoot))
        {
            _overlayRoot.GuiInput -= OnOverlayGuiInput;
        }

        if (GodotObject.IsInstanceValid(_retryButton))
        {
            _retryButton.Pressed -= OnRetryPressed;
        }

        if (GodotObject.IsInstanceValid(_decisionConfirm)) _decisionConfirm.Pressed -= OnDecisionConfirmed;
        if (GodotObject.IsInstanceValid(_decisionCancel)) _decisionCancel.Pressed -= CloseDecision;

        if (GodotObject.IsInstanceValid(_personalQuestScroll))
        {
            _personalQuestScroll.GuiInput -= OnPersonalQuestScrollGuiInput;
        }

        if (GetViewport() != null)
        {
            GetViewport().SizeChanged -= OnViewportSizeChanged;
        }
    }

    public void OpenLoading(CardViewerKind kind)
    {
        if (kind == CardViewerKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        _displayedKeys.Clear();
        _displayedCards.Clear();
        ClearTextures();
        _titleLabel.Text = TitleFor(kind);
        _hintLabel.Text = IsScrollableKind(kind)
            ? "只读查看 · 滚轮或拖动滚动条浏览 · 点击其他区域关闭"
            : "点击卡牌下方按钮操作 · 点击其他区域关闭";
        if (kind is CardViewerKind.Inventory or CardViewerKind.Equipment)
        {
            _hintLabel.Text = "滚轮或拖动滚动条浏览 · 点击卡牌下方按钮操作";
        }
        ResetCloseGesture();
        _overlayRoot.Visible = true;
        ApplyState(CardViewerState.Loading);
    }

    public void ShowCards(
        CardViewerKind kind,
        IReadOnlyList<CardViewerCard> cards,
        CardActionAvailability? actionAvailability = null,
        IReadOnlyList<CardViewerCard>? replacementCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        Kind = kind;
        _displayedKeys.Clear();
        _displayedCards.Clear();
        ClearTextures();
        _actionAvailability = actionAvailability ?? CardActionAvailability.Loading;
        _replacementCandidates.Clear();
        if (replacementCandidates != null)
        {
            _replacementCandidates.AddRange(replacementCandidates);
        }

        var visibleCards = IsScrollableKind(kind)
            ? cards.ToList()
            : cards.Take(MaximumVisibleCards).ToList();
        if (kind == CardViewerKind.PersonalQuests)
        {
            CreatePersonalQuestCards(visibleCards);
        }
        else if (kind is CardViewerKind.Inventory or CardViewerKind.Equipment)
        {
            CreateInventoryItemCards(visibleCards);
        }

        for (var index = 0; index < visibleCards.Count; index++)
        {
            if (IsScrollableKind(kind))
            {
                _displayedKeys.Add(visibleCards[index].Key);
                _displayedCards.Add(visibleCards[index]);
                continue;
            }

            _cardTextures[index].Texture = visibleCards[index].Texture;
            _cardSlots[index].Visible = true;
            _displayedKeys.Add(visibleCards[index].Key);
            _displayedCards.Add(visibleCards[index]);
        }

        _emptyLabel.Text = kind switch
        {
            CardViewerKind.QuestBoard => "当前公共任务板暂无卡牌。",
            CardViewerKind.ShopShelf => "当前公共货架暂无卡牌。",
            CardViewerKind.Inventory => "背包中没有手牌道具。",
            CardViewerKind.Equipment => "当前没有已装备道具。",
            _ => "当前没有已接任务。"
        };
        _emptyLabel.Visible = visibleCards.Count == 0;
        _cardRow.Visible = visibleCards.Count > 0 && !IsScrollableKind(kind);
        _personalQuestScroll.Visible = visibleCards.Count > 0 && IsScrollableKind(kind);
        _overlayRoot.Visible = true;
        ApplyState(CardViewerState.Content);
        RefreshActionButtons();
        RefreshCardSlotSizes();
    }

    public void UpdateActionAvailability(CardActionAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        _actionAvailability = availability;
        if (State == CardViewerState.Content)
        {
            RefreshActionButtons();
        }
    }

    public void UpdateItemActionAvailabilities(IReadOnlyDictionary<long, CardActionAvailability> availabilities)
    {
        foreach (var entry in _itemCards)
        {
            var availability = availabilities.TryGetValue(entry.PlayerItemId, out var current)
                ? current
                : new CardActionAvailability(false, "服务端未返回对应操作资格。");
            entry.UpdateAvailability(availability);
        }
    }

    public bool IsActionButtonEnabled(int index)
    {
        return index >= 0 && index < _actionButtons.Count && !_actionButtons[index].Disabled;
    }

    public string GetActionButtonTooltip(int index)
    {
        return index >= 0 && index < _actionButtons.Count ? _actionButtons[index].TooltipText : "";
    }

    public Button GetActionButton(int index)
    {
        return _actionButtons[index];
    }

    public Control GetActionArea(int index)
    {
        return _actionAreas[index];
    }

    public void ShowError(CardViewerKind kind, string message)
    {
        Kind = kind;
        _displayedKeys.Clear();
        _displayedCards.Clear();
        ClearTextures();
        _errorLabel.Text = string.IsNullOrWhiteSpace(message) ? "卡牌数据加载失败，请重试。" : message.Trim();
        _overlayRoot.Visible = true;
        ApplyState(CardViewerState.Error);
    }

    public void CloseOverlay()
    {
        if (!IsNodeReady() || !_overlayRoot.Visible)
        {
            return;
        }

        _overlayRoot.Visible = false;
        CloseDecision();
        ResetCloseGesture();
        Kind = CardViewerKind.None;
        ApplyState(CardViewerState.Closed);
        EmitSignal(SignalName.Closed);
    }

    public void RequestRetry()
    {
        if (State != CardViewerState.Error || !_overlayRoot.Visible)
        {
            return;
        }

        EmitSignal(SignalName.RetryRequested);
    }

    public void CloseFromBackdrop()
    {
        CloseOverlay();
    }

    private void ApplyState(CardViewerState state)
    {
        State = state;
        if (!IsNodeReady())
        {
            return;
        }

        _loadingState.Visible = state == CardViewerState.Loading;
        _contentState.Visible = state == CardViewerState.Content;
        _errorState.Visible = state == CardViewerState.Error;
        _retryButton.Visible = state == CardViewerState.Error;
        _retryButton.Disabled = state != CardViewerState.Error;
    }

    private void ClearTextures()
    {
        for (var index = 0; index < _cardSlots.Count; index++)
        {
            _cardSlots[index].Visible = false;
            _cardTextures[index].Texture = null;
            _actionButtons[index].Disabled = true;
            _actionButtons[index].TooltipText = "";
        }

        foreach (var card in _personalQuestCards)
        {
            _personalQuestRow.RemoveChild(card);
            card.QueueFree();
        }

        _personalQuestCards.Clear();
        foreach (var card in _itemCards)
        {
            _personalQuestRow.RemoveChild(card);
            card.QueueFree();
        }
        _itemCards.Clear();
        _replacementCandidates.Clear();
        _personalQuestScroll.ScrollHorizontal = 0;
        _personalQuestScroll.Visible = false;
    }

    private void RefreshActionButtons()
    {
        var buttonText = Kind == CardViewerKind.QuestBoard ? "接取" : "购买";
        for (var index = 0; index < _actionButtons.Count; index++)
        {
            var hasCard = !IsScrollableKind(Kind) && index < _displayedCards.Count;
            _actionAreas[index].Visible = hasCard;
            _actionButtons[index].Text = buttonText;
            _actionButtons[index].Disabled = !hasCard || !_actionAvailability.Enabled;
            _actionButtons[index].TooltipText = hasCard ? _actionAvailability.TooltipText : "";
        }
    }

    private void RefreshCardSlotSizes()
    {
        if (!IsNodeReady() || Kind == CardViewerKind.None)
        {
            return;
        }

        var viewportWidth = GetViewport().GetVisibleRect().Size.X;
        var availableWidth = Mathf.Max(720.0f, viewportWidth - 180.0f);
        if (IsScrollableKind(Kind))
        {
            var scrollWidth = Mathf.Min(1600.0f, availableWidth);
            var isItemView = Kind is CardViewerKind.Inventory or CardViewerKind.Equipment;
            var scrollCardRatio = isItemView ? ItemAspectRatio : QuestAspectRatio;
            var cardWidth = Mathf.Min(isItemView ? 330.0f : 500.0f, (scrollWidth - 48.0f) / 3.0f);
            var cardHeight = cardWidth / scrollCardRatio;
            _personalQuestScroll.CustomMinimumSize = new Vector2(scrollWidth, cardHeight + 28.0f);
            foreach (var card in _personalQuestCards)
            {
                card.Ratio = QuestAspectRatio;
                card.CustomMinimumSize = new Vector2(cardWidth, cardHeight);
            }
            foreach (var card in _itemCards)
            {
                card.ResizeCard(cardWidth, scrollCardRatio);
            }

            return;
        }

        var width = Mathf.Min(Kind == CardViewerKind.QuestBoard ? 520.0f : 330.0f, (availableWidth - 48.0f) / 3.0f);
        var ratio = Kind == CardViewerKind.QuestBoard ? QuestAspectRatio : ItemAspectRatio;
        var height = width / ratio;
        for (var index = 0; index < _cardSlots.Count; index++)
        {
            _cardAspects[index].Ratio = ratio;
            _cardAspects[index].CustomMinimumSize = new Vector2(width, height);
            _cardSlots[index].CustomMinimumSize = new Vector2(width, height + 58.0f);
        }
    }

    private void OnViewportSizeChanged()
    {
        if (State == CardViewerState.Content)
        {
            RefreshCardSlotSizes();
        }
    }

    private void OnOverlayGuiInput(InputEvent inputEvent)
    {
        TrackCloseGesture(inputEvent, _overlayRoot);
    }

    private void OnRetryPressed()
    {
        RequestRetry();
    }

    private void OnActionAreaGuiInput(int index, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left })
        {
            return;
        }

        // 亮起和灰色按钮都消费输入，避免冒泡到全屏关闭层或地图。
        ResetCloseGesture();
        _actionAreas[index].AcceptEvent();
    }

    private void OnActionButtonPressed(int index)
    {
        if (index < 0
            || index >= _displayedCards.Count
            || _actionButtons[index].Disabled)
        {
            return;
        }

        var card = _displayedCards[index];
        var primaryId = Kind == CardViewerKind.QuestBoard ? card.QuestId : card.ShelfSlotId;
        var secondaryId = Kind == CardViewerKind.QuestBoard ? card.OfferId : card.ItemId;
        if (!primaryId.HasValue || primaryId.Value <= 0)
        {
            return;
        }

        // 展示层只发出稳定身份，是否提交行动由 GameScreen 统一决定。
        EmitSignal(SignalName.CardActionPressed, (int)Kind, primaryId.Value, secondaryId ?? 0);
    }

    private void CreatePersonalQuestCards(IReadOnlyList<CardViewerCard> cards)
    {
        if (cards.Count == 0)
        {
            return;
        }

        if (ReadOnlyQuestCardScene == null)
        {
            GD.PushError("CardViewerOverlay 缺少 ReadOnlyQuestCardScene。");
            return;
        }

        foreach (var card in cards)
        {
            var cardNode = ReadOnlyQuestCardScene.Instantiate<AspectRatioContainer>();
            cardNode.GetNode<TextureRect>("CardTexture").Texture = card.Texture;
            _personalQuestRow.AddChild(cardNode);
            _personalQuestCards.Add(cardNode);
        }
    }

    private void CreateInventoryItemCards(IReadOnlyList<CardViewerCard> cards)
    {
        if (cards.Count == 0 || InventoryItemCardScene == null)
        {
            return;
        }

        foreach (var card in cards)
        {
            var cardNode = InventoryItemCardScene.Instantiate<InventoryItemCard>();
            _personalQuestRow.AddChild(cardNode);
            var availability = card.ActionAvailability ?? CardActionAvailability.Loading;
            cardNode.Bind(card, ActionTextFor(card), availability, 330.0f, ItemAspectRatio);
            cardNode.ActionRequested += OnInventoryItemActionRequested;
            _itemCards.Add(cardNode);
        }
    }

    private void OnInventoryItemActionRequested(long playerItemId)
    {
        var card = _displayedCards.FirstOrDefault(candidate => candidate.PlayerItemId == playerItemId);
        if (card == null)
        {
            return;
        }

        if (Kind == CardViewerKind.Equipment)
        {
            EmitSignal(SignalName.CardActionPressed, (int)Kind, playerItemId, 0L);
            return;
        }

        if (!string.Equals(card.Location, "HAND", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(card.ItemType, "CONSUMABLE", StringComparison.OrdinalIgnoreCase))
        {
            _decisionPlayerItemId = playerItemId;
            _decisionMessage.Text = $"确定使用「{DisplayItemName(card)}」吗？";
            ClearDecisionOptions();
            _decisionConfirm.Visible = true;
            _decisionConfirm.Text = "使用";
            _decisionLayer.Visible = true;
            ResetCloseGesture();
            return;
        }

        if (!string.Equals(card.ItemType, "EQUIPMENT", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_replacementCandidates.Count < 2)
        {
            EmitSignal(SignalName.CardActionPressed, (int)Kind, playerItemId, 0L);
            return;
        }

        _decisionPlayerItemId = playerItemId;
        _decisionMessage.Text = $"装备栏已满，请选择要替换的装备以装备「{DisplayItemName(card)}」。";
        ClearDecisionOptions();
        _decisionConfirm.Visible = false;
        foreach (var replacement in _replacementCandidates)
        {
            if (!replacement.PlayerItemId.HasValue || replacement.PlayerItemId.Value <= 0)
            {
                continue;
            }
            var replacementId = replacement.PlayerItemId.Value;
            var button = new Button
            {
                Text = $"替换「{DisplayItemName(replacement)}」",
                CustomMinimumSize = new Vector2(380, 44)
            };
            button.Pressed += () => ConfirmReplacement(replacementId);
            _decisionOptions.AddChild(button);
        }
        _decisionLayer.Visible = true;
        ResetCloseGesture();
    }

    private void OnDecisionConfirmed()
    {
        var playerItemId = _decisionPlayerItemId;
        CloseDecision();
        if (playerItemId > 0)
        {
            EmitSignal(SignalName.CardActionPressed, (int)CardViewerKind.Inventory, playerItemId, 0L);
        }
    }

    private void ConfirmReplacement(long replacePlayerItemId)
    {
        var playerItemId = _decisionPlayerItemId;
        CloseDecision();
        if (playerItemId > 0 && replacePlayerItemId > 0)
        {
            EmitSignal(SignalName.CardActionPressed, (int)CardViewerKind.Inventory, playerItemId, replacePlayerItemId);
        }
    }

    private void CloseDecision()
    {
        if (!IsNodeReady())
        {
            return;
        }
        _decisionLayer.Visible = false;
        _decisionPlayerItemId = 0;
        ClearDecisionOptions();
    }

    private void ClearDecisionOptions()
    {
        foreach (var child in _decisionOptions.GetChildren())
        {
            _decisionOptions.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnPersonalQuestScrollGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { Pressed: true } wheel
            && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            var direction = wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
            _personalQuestScroll.ScrollHorizontal += direction * 120;
            _personalQuestScroll.AcceptEvent();
            return;
        }

        TrackCloseGesture(inputEvent, _personalQuestScroll);
    }

    private void TrackCloseGesture(InputEvent inputEvent, Control receiver)
    {
        if (_decisionLayer.Visible)
        {
            receiver.AcceptEvent();
            ResetCloseGesture();
            return;
        }
        switch (inputEvent)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouseButton:
                _closePointerPressed = true;
                _closePointerDragged = false;
                _closePointerStart = mouseButton.Position;
                receiver.AcceptEvent();
                break;
            case InputEventMouseMotion mouseMotion when _closePointerPressed:
                if (mouseMotion.Position.DistanceTo(_closePointerStart) >= CloseDragThreshold)
                {
                    _closePointerDragged = true;
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (!_closePointerPressed)
                {
                    return;
                }

                var shouldClose = !_closePointerDragged;
                ResetCloseGesture();
                receiver.AcceptEvent();
                if (shouldClose)
                {
                    CloseFromBackdrop();
                }
                break;
        }
    }

    private void ResetCloseGesture()
    {
        _closePointerPressed = false;
        _closePointerDragged = false;
        _closePointerStart = Vector2.Zero;
    }

    private static string TitleFor(CardViewerKind kind)
    {
        return kind switch
        {
            CardViewerKind.QuestBoard => "公共任务板",
            CardViewerKind.ShopShelf => "公共货架",
            CardViewerKind.PersonalQuests => "当前任务",
            CardViewerKind.Inventory => "背包",
            CardViewerKind.Equipment => "装备区",
            _ => "卡牌查看"
        };
    }

    private static bool IsScrollableKind(CardViewerKind kind) =>
        kind is CardViewerKind.PersonalQuests or CardViewerKind.Inventory or CardViewerKind.Equipment;

    private static string ActionTextFor(CardViewerCard card)
    {
        if (string.Equals(card.Location, "EQUIPPED", StringComparison.OrdinalIgnoreCase))
        {
            return "卸下";
        }
        return string.Equals(card.ItemType, "CONSUMABLE", StringComparison.OrdinalIgnoreCase) ? "使用" : "装备";
    }

    private static string DisplayItemName(CardViewerCard card) =>
        string.IsNullOrWhiteSpace(card.ItemName) ? $"道具 {card.ItemId}" : card.ItemName.Trim();
}
