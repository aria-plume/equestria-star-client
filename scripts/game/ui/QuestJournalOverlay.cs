using System;
using System.Collections.Generic;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public enum QuestJournalState
{
    Closed,
    Loading,
    Tasks,
    Error,
    Settlement,
    Reward,
    Inventory
}

public enum QuestJournalRetryKind
{
    Tasks,
    Inventory
}

public partial class QuestJournalOverlay : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();
    [Signal] public delegate void RetryRequestedEventHandler(int kind);
    [Signal] public delegate void SubmitRequestedEventHandler(long playerQuestId);
    [Signal] public delegate void AbandonRequestedEventHandler(long playerQuestId, long questId);
    [Signal] public delegate void RewardResolveRequestedEventHandler(long rewardId, long cardId);
    [Signal] public delegate void RewardAbandonRequestedEventHandler(long rewardId);
    [Signal] public delegate void InventoryRequestedEventHandler();
    [Signal] public delegate void DiscardRequestedEventHandler(long playerItemId);
    [Signal] public delegate void RewardDataInvalidEventHandler();

    private static readonly PackedScene QuestCardScene = GD.Load<PackedScene>("res://scenes/game/ui/QuestTaskCard.tscn");

    private Control _root = null!;
    private Button _backdrop = null!;
    private Label _title = null!;
    private Button _closeButton = null!;
    private Label _status = null!;
    private CenterContainer _loading = null!;
    private VBoxContainer _error = null!;
    private Label _errorLabel = null!;
    private Button _retryButton = null!;
    private ScrollContainer _taskScroll = null!;
    private VBoxContainer _taskList = null!;
    private VBoxContainer _settlement = null!;
    private Label _settlementLabel = null!;
    private ScrollContainer _rewardScroll = null!;
    private HBoxContainer _rewardList = null!;
    private HBoxContainer _rewardActions = null!;
    private Button _rewardAbandonButton = null!;
    private Button _inventoryButton = null!;
    private ScrollContainer _inventoryScroll = null!;
    private VBoxContainer _inventoryList = null!;
    private Control _confirmLayer = null!;
    private CenterContainer _confirmCenter = null!;
    private PanelContainer _confirmPanel = null!;
    private Label _confirmLabel = null!;
    private Button _confirmYes = null!;
    private Button _confirmNo = null!;
    private QuestJournalRetryKind _retryKind;
    private ConfirmKind _confirmKind;
    private long _confirmPrimaryId;
    private long _confirmSecondaryId;
    private readonly List<QuestTaskCard> _taskCards = [];
    private readonly Dictionary<long, string> _taskNames = [];
    private PendingRewardDto? _lastReward;
    private long _lastRewardPlayerId;
    private bool _keepSettlementSummary;
    private bool _busy;

    public QuestJournalState State { get; private set; } = QuestJournalState.Closed;
    public bool IsOpen => State != QuestJournalState.Closed;
    public int DisplayedTaskCount => _taskCards.Count;

    public void SetStatusMessage(string message) => _status.Text = message;

    public override void _Ready()
    {
        _root = GetNode<Control>("Root");
        _backdrop = GetNode<Button>("Root/Backdrop");
        _title = GetNode<Label>("Root/Center/Panel/Margin/Content/Header/Title");
        _closeButton = GetNode<Button>("Root/Center/Panel/Margin/Content/Header/CloseButton");
        _status = GetNode<Label>("Root/Center/Panel/Margin/Content/Status");
        _loading = GetNode<CenterContainer>("Root/Center/Panel/Margin/Content/Loading");
        _error = GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/Error");
        _errorLabel = GetNode<Label>("Root/Center/Panel/Margin/Content/Error/ErrorLabel");
        _retryButton = GetNode<Button>("Root/Center/Panel/Margin/Content/Error/RetryButton");
        _taskScroll = GetNode<ScrollContainer>("Root/Center/Panel/Margin/Content/TaskScroll");
        _taskList = GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/TaskScroll/TaskList");
        _settlement = GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/Settlement");
        _settlementLabel = GetNode<Label>("Root/Center/Panel/Margin/Content/Settlement/SettlementLabel");
        _rewardScroll = GetNode<ScrollContainer>("Root/Center/Panel/Margin/Content/Settlement/RewardScroll");
        _rewardList = GetNode<HBoxContainer>("Root/Center/Panel/Margin/Content/Settlement/RewardScroll/RewardList");
        _rewardActions = GetNode<HBoxContainer>("Root/Center/Panel/Margin/Content/Settlement/RewardActions");
        _rewardAbandonButton = GetNode<Button>("Root/Center/Panel/Margin/Content/Settlement/RewardActions/AbandonRewardButton");
        _inventoryButton = GetNode<Button>("Root/Center/Panel/Margin/Content/Settlement/RewardActions/InventoryButton");
        _inventoryScroll = GetNode<ScrollContainer>("Root/Center/Panel/Margin/Content/InventoryScroll");
        _inventoryList = GetNode<VBoxContainer>("Root/Center/Panel/Margin/Content/InventoryScroll/InventoryList");
        _confirmLayer = GetNode<Control>("Root/ConfirmLayer");
        _confirmCenter = GetNode<CenterContainer>("Root/ConfirmLayer/Center");
        _confirmPanel = GetNode<PanelContainer>("Root/ConfirmLayer/Center/Panel");
        _confirmLabel = GetNode<Label>("Root/ConfirmLayer/Center/Panel/Margin/Content/Message");
        _confirmYes = GetNode<Button>("Root/ConfirmLayer/Center/Panel/Margin/Content/Actions/YesButton");
        _confirmNo = GetNode<Button>("Root/ConfirmLayer/Center/Panel/Margin/Content/Actions/NoButton");

        _backdrop.Pressed += CloseOverlay;
        _closeButton.Pressed += CloseOverlay;
        _retryButton.Pressed += OnRetryPressed;
        _rewardAbandonButton.Pressed += OnRewardAbandonPressed;
        _inventoryButton.Pressed += () => EmitSignal(SignalName.InventoryRequested);
        _confirmYes.Pressed += OnConfirmYes;
        _confirmNo.Pressed += CloseConfirm;
        _root.Resized += LayoutConfirmPanel;
        _root.Visible = false;
        _confirmLayer.Visible = false;
    }

    public void OpenLoading(QuestJournalRetryKind kind, string text = "正在读取个人任务……")
    {
        _retryKind = kind;
        _root.Visible = true;
        State = QuestJournalState.Loading;
        _title.Text = kind == QuestJournalRetryKind.Inventory ? "整理背包" : "个人任务";
        _status.Text = text;
        ShowOnly(_loading);
        _closeButton.Disabled = false;
    }

    public void ShowTasks(IReadOnlyList<QuestTaskDisplay> tasks, string status = "")
    {
        ClearTaskCards();
        _taskNames.Clear();
        foreach (var task in tasks)
        {
            _taskNames[task.PlayerQuestId] = task.QuestName;
            var card = QuestCardScene.Instantiate<QuestTaskCard>();
            card.SubmitRequested += OnTaskSubmitRequested;
            card.AbandonRequested += OnTaskAbandonRequested;
            _taskList.AddChild(card);
            card.Bind(task, _busy);
            _taskCards.Add(card);
        }

        if (tasks.Count == 0)
        {
            var empty = new Label
            {
                Text = "当前没有已接任务。",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(0, 120)
            };
            _taskList.AddChild(empty);
        }

        _root.Visible = true;
        State = QuestJournalState.Tasks;
        _title.Text = "个人任务";
        _status.Text = status;
        ShowOnly(_taskScroll);
        _closeButton.Disabled = false;
    }

    public void ShowError(string message, QuestJournalRetryKind kind)
    {
        _retryKind = kind;
        _root.Visible = true;
        State = QuestJournalState.Error;
        _title.Text = kind == QuestJournalRetryKind.Inventory ? "整理背包" : "个人任务";
        _status.Text = "";
        _errorLabel.Text = message;
        ShowOnly(_error);
        _closeButton.Disabled = false;
    }

    public void ShowSettlement(
        QuestSettlementDto settlement,
        PendingRewardDto? pendingReward,
        long currentPlayerId,
        IReadOnlyList<DeckDrawDto>? deckDraws)
    {
        var shortage = BuildDeckDrawText(deckDraws);
        _settlementLabel.Text =
            $"任务已结算\n实际金币：{settlement.CoinsGranted}\n实际声望：{settlement.PrestigeGranted}\n" +
            $"伙伴金币加成（已包含）：{settlement.PartnerBonusCoins}\n金币余额：{settlement.CoinsAfter}\n" +
            $"声望余额：{settlement.PrestigeAfter}\n待选道具：{settlement.PendingItemRewardCount}" + shortage;
        _root.Visible = true;
        State = QuestJournalState.Settlement;
        _title.Text = "任务结算";
        _status.Text = "";
        ShowOnly(_settlement);
        ClearChildren(_rewardList);
        _rewardScroll.Visible = false;
        _rewardActions.Visible = false;
        _closeButton.Disabled = false;

        if (QuestSystemModel.IsOwnPendingReward(pendingReward, currentPlayerId))
        {
            ShowReward(pendingReward!, currentPlayerId, keepSettlementSummary: true);
        }
    }

    public void ShowReward(PendingRewardDto reward, long currentPlayerId, bool keepSettlementSummary = false)
    {
        _lastReward = reward;
        _lastRewardPlayerId = currentPlayerId;
        _keepSettlementSummary = keepSettlementSummary;
        ClearChildren(_rewardList);
        _root.Visible = true;
        State = QuestJournalState.Reward;
        _title.Text = "领取任务奖励";
        if (!keepSettlementSummary)
        {
            _settlementLabel.Text = "请处理当前 FIFO 奖励队首。";
        }
        _status.Text = reward.DrawResult?.Shortage == true
            ? FirstNonEmpty(reward.DrawResult.Message, "牌库不足，按服务端实际抽取数量展示。")
            : "";
        ShowOnly(_settlement);
        _rewardScroll.Visible = true;
        _rewardActions.Visible = true;
        var isOwnReward = QuestSystemModel.IsOwnPendingReward(reward, currentPlayerId);
        _closeButton.Disabled = isOwnReward;

        if (!isOwnReward)
        {
            AddMessage(_rewardList, "当前 FIFO 队首属于其他玩家，不能查看或操作其私有候选。", 520);
            SetRewardActions(reward, false);
            return;
        }

        if (!string.Equals(reward.Kind, "ITEM", StringComparison.OrdinalIgnoreCase))
        {
            AddMessage(_rewardList, $"当前奖励类型「{FirstNonEmpty(reward.Kind, "未知")}」尚未支持，请等待后续功能。", 520);
            SetRewardActions(reward, false);
            return;
        }

        if (reward.Candidates == null)
        {
            AddMessage(_rewardList, "奖励候选数据缺失，无法可靠展示。", 520);
            SetRewardActions(reward, false);
            EmitSignal(SignalName.RewardDataInvalid);
            return;
        }

        var hasInvalidCandidate = false;
        foreach (var candidate in reward.Candidates)
        {
            if (!QuestSystemModel.TryBuildItemRewardCandidate(
                    reward,
                    candidate,
                    currentPlayerId,
                    out var display,
                    out var validationError))
            {
                AddMessage(_rewardList, validationError, 520);
                hasInvalidCandidate = true;
                continue;
            }

            var column = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };
            if (CardArtCatalog.TryGetItemTexture(display.ItemId, out var texture, out _) && texture != null)
            {
                column.AddChild(new TextureRect
                {
                    Texture = texture,
                    CustomMinimumSize = new Vector2(220, 310),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                });
            }
            else
            {
                AddMessage(column, $"道具 {display.ItemId} 缺少本地卡图。", 220);
            }

            var nameLabel = new Label
            {
                Text = $"{display.ItemName}\n{display.ItemType}",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            nameLabel.AddThemeColorOverride("font_color", new Color("e6ecea"));
            column.AddChild(nameLabel);

            var button = new Button
            {
                Text = "领取",
                Disabled = !display.CanExecute,
                TooltipText = display.CanExecute
                    ? "领取该候选"
                    : display.ExecuteReason
            };
            var rewardId = display.RewardId;
            var cardId = display.CardId;
            button.Pressed += () => EmitSignal(SignalName.RewardResolveRequested, rewardId, cardId);
            column.AddChild(button);
            _rewardList.AddChild(column);
        }

        if (hasInvalidCandidate)
        {
            EmitSignal(SignalName.RewardDataInvalid);
        }

        if (reward.Candidates.Count == 0)
        {
            AddMessage(_rewardList, "服务端本次没有返回候选卡。", 520);
        }
        SetRewardActions(reward, true);
    }

    public void ShowInventory(PlayerItemsResponseDto inventory, bool discardAvailable, string reason)
    {
        ClearChildren(_inventoryList);
        foreach (var item in inventory.Items ?? [])
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(720, 88) };
            if (item.ItemId.HasValue
                && CardArtCatalog.TryGetItemTexture(item.ItemId.Value, out var texture, out _)
                && texture != null)
            {
                row.AddChild(new TextureRect
                {
                    Texture = texture,
                    CustomMinimumSize = new Vector2(58, 82),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                });
            }
            var label = new Label
            {
                Text = $"{FirstNonEmpty(item.ItemName, "未命名道具")}\n实例 ID：{item.PlayerItemId}\n位置：{FirstNonEmpty(item.LocationText, item.Location ?? "未知")}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", new Color("e6ecea"));
            row.AddChild(label);
            var discard = new Button
            {
                Text = "丢弃",
                Disabled = !discardAvailable,
                TooltipText = discardAvailable ? "丢弃该持有实例" : reason,
                CustomMinimumSize = new Vector2(110, 42)
            };
            var playerItemId = item.PlayerItemId ?? 0;
            discard.Pressed += () => AskConfirm(
                ConfirmKind.DiscardItem,
                playerItemId,
                0,
                $"确定丢弃「{FirstNonEmpty(item.ItemName, "该道具")}」吗？"
            );
            row.AddChild(discard);
            _inventoryList.AddChild(row);
        }

        if ((inventory.Items?.Count ?? 0) == 0)
        {
            AddMessage(_inventoryList, "当前没有可列出的持有道具。", 520);
        }
        _root.Visible = true;
        State = QuestJournalState.Inventory;
        _title.Text = "整理背包";
        _status.Text = reason;
        ShowOnly(_inventoryScroll);
        _closeButton.Disabled = false;
    }

    public void SetBusy(bool busy, string message)
    {
        _busy = busy;
        _status.Text = message;
        _closeButton.Disabled = busy && State == QuestJournalState.Reward;
        foreach (var card in _taskCards)
        {
            card.SetBusy(busy);
        }
        if (busy)
        {
            SetButtonsDisabledRecursive(_settlement, true);
            SetButtonsDisabledRecursive(_inventoryScroll, true);
        }
    }

    public void CloseOverlay()
    {
        if (State == QuestJournalState.Inventory && _lastReward != null)
        {
            ShowReward(_lastReward, _lastRewardPlayerId, _keepSettlementSummary);
            return;
        }
        if (!IsOpen || (State == QuestJournalState.Reward && _closeButton.Disabled) || _confirmLayer.Visible)
        {
            return;
        }
        ClearTaskCards();
        _taskNames.Clear();
        _lastReward = null;
        _lastRewardPlayerId = 0;
        _keepSettlementSummary = false;
        ClearChildren(_rewardList);
        ClearChildren(_inventoryList);
        _root.Visible = false;
        State = QuestJournalState.Closed;
        EmitSignal(SignalName.Closed);
    }

    public void ForceClose()
    {
        if (!IsOpen)
        {
            return;
        }
        CloseConfirm();
        ClearTaskCards();
        _taskNames.Clear();
        _lastReward = null;
        _lastRewardPlayerId = 0;
        _keepSettlementSummary = false;
        ClearChildren(_rewardList);
        ClearChildren(_inventoryList);
        _root.Visible = false;
        State = QuestJournalState.Closed;
    }

    private void OnTaskSubmitRequested(long playerQuestId) =>
        EmitSignal(SignalName.SubmitRequested, playerQuestId);

    private void OnTaskAbandonRequested(long playerQuestId, long questId)
    {
        var name = _taskNames.TryGetValue(playerQuestId, out var taskName) ? taskName : "该任务";
        AskConfirm(
            ConfirmKind.AbandonQuest,
            playerQuestId,
            questId,
            $"是否花费1声望取消掉「{name}」？"
        );
    }

    private void OnRewardAbandonPressed()
    {
        var rewardId = _rewardAbandonButton.GetMeta("reward_id", 0L).AsInt64();
        AskConfirm(ConfirmKind.AbandonReward, rewardId, 0, "确定放弃当前奖励吗？此操作不可撤销。");
    }

    private void SetRewardActions(PendingRewardDto reward, bool allowInventory)
    {
        _rewardAbandonButton.SetMeta("reward_id", reward.RewardId ?? 0);
        _rewardAbandonButton.Disabled = reward.CanAbandon != true;
        _rewardAbandonButton.TooltipText = reward.CanAbandon == true
            ? "放弃当前奖励"
            : QuestSystemModel.RewardAbandonReason(reward);
        _inventoryButton.Visible = allowInventory && reward.CapacitySatisfied == false;
    }

    private void AskConfirm(ConfirmKind kind, long primaryId, long secondaryId, string message)
    {
        if (primaryId <= 0)
        {
            return;
        }
        _confirmKind = kind;
        _confirmPrimaryId = primaryId;
        _confirmSecondaryId = secondaryId;
        _confirmLabel.Text = message;
        _confirmLayer.Visible = true;
        // 该层初始为隐藏状态，某些无界面渲染器不会为隐藏容器执行首次布局。
        // 显示时显式同步全屏尺寸，保证确认按钮的可见位置与鼠标命中区域一致。
        _confirmLayer.Position = Vector2.Zero;
        _confirmLayer.Size = _root.Size;
        _confirmCenter.Position = Vector2.Zero;
        _confirmCenter.Size = _root.Size;
        _confirmCenter.QueueSort();
        RefreshControlLayout(_confirmLayer);
        LayoutConfirmPanel();
    }

    private void OnConfirmYes()
    {
        var kind = _confirmKind;
        var primary = _confirmPrimaryId;
        var secondary = _confirmSecondaryId;
        CloseConfirm();
        switch (kind)
        {
            case ConfirmKind.AbandonQuest:
                EmitSignal(SignalName.AbandonRequested, primary, secondary);
                break;
            case ConfirmKind.AbandonReward:
                EmitSignal(SignalName.RewardAbandonRequested, primary);
                break;
            case ConfirmKind.DiscardItem:
                EmitSignal(SignalName.DiscardRequested, primary);
                break;
        }
    }

    private void CloseConfirm()
    {
        _confirmLayer.Visible = false;
        _confirmKind = ConfirmKind.None;
        _confirmPrimaryId = 0;
        _confirmSecondaryId = 0;
    }

    private void LayoutConfirmPanel()
    {
        if (!GodotObject.IsInstanceValid(_confirmPanel) || !GodotObject.IsInstanceValid(_confirmCenter))
        {
            return;
        }
        var panelSize = _confirmPanel.GetCombinedMinimumSize();
        _confirmPanel.Size = panelSize;
        _confirmPanel.Position = (_confirmCenter.Size - panelSize) * 0.5f;
    }

    private static void RefreshControlLayout(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            RefreshControlLayout(child);
        }
        if (node is Control control)
        {
            control.UpdateMinimumSize();
        }
        if (node is Container container)
        {
            container.QueueSort();
        }
    }

    private void OnRetryPressed() => EmitSignal(SignalName.RetryRequested, (int)_retryKind);

    private void ShowOnly(Control visible)
    {
        _loading.Visible = visible == _loading;
        _error.Visible = visible == _error;
        _taskScroll.Visible = visible == _taskScroll;
        _settlement.Visible = visible == _settlement;
        _inventoryScroll.Visible = visible == _inventoryScroll;
    }

    private void ClearTaskCards()
    {
        foreach (var card in _taskCards)
        {
            if (GodotObject.IsInstanceValid(card))
            {
                card.SubmitRequested -= OnTaskSubmitRequested;
                card.AbandonRequested -= OnTaskAbandonRequested;
            }
        }
        _taskCards.Clear();
        ClearChildren(_taskList);
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void SetButtonsDisabledRecursive(Node node, bool disabled)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Button button)
            {
                button.Disabled = disabled;
            }
            SetButtonsDisabledRecursive(child, disabled);
        }
    }

    private static void AddMessage(Node parent, string text, float width)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 90),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", new Color("e6ecea"));
        parent.AddChild(label);
    }

    private static string BuildDeckDrawText(IReadOnlyList<DeckDrawDto>? draws)
    {
        if (draws == null)
        {
            return "";
        }
        foreach (var draw in draws)
        {
            if (draw.Shortage == true)
            {
                return $"\n抽牌：实际 {draw.ActualCount ?? 0}/{draw.RequestedCount ?? 0}（牌库不足）";
            }
        }
        return "";
    }

    private static string FirstNonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private enum ConfirmKind
    {
        None,
        AbandonQuest,
        AbandonReward,
        DiscardItem
    }
}
