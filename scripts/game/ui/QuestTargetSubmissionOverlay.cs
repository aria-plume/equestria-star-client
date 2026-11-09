using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

public enum QuestTargetSubmissionOverlayState
{
    Hidden,
    Loading,
    Tasks,
    ItemSelection,
    ConfirmingResource,
    Submitting,
    Error
}

/// <summary>
/// 任务目标提交弹层。只负责展示、选卡和确认，不读取 Session 或发起网络请求。
/// </summary>
public partial class QuestTargetSubmissionOverlay : CanvasLayer
{
    private const float ItemAspectRatio = 1453.0f / 2161.0f;

    [Signal] public delegate void ClosedEventHandler();
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void ItemSubmitRequestedEventHandler(long questId, long playerQuestId, long playerItemId);
    [Signal] public delegate void ResourceSubmitRequestedEventHandler(long questId, long playerQuestId);

    [Export] public PackedScene? InventoryItemCardScene { get; set; }

    private Control _root = null!;
    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private ScrollContainer _taskScroll = null!;
    private VBoxContainer _taskList = null!;
    private Label _taskEmptyLabel = null!;
    private ScrollContainer _itemScroll = null!;
    private HBoxContainer _itemList = null!;
    private Label _itemEmptyLabel = null!;
    private Button _backButton = null!;
    private Button _closeButton = null!;
    private Button _retryButton = null!;
    private Control _confirmLayer = null!;
    private Label _confirmMessage = null!;
    private Button _confirmButton = null!;
    private Button _cancelButton = null!;
    private readonly List<Button> _taskActionButtons = [];
    private readonly List<InventoryItemCard> _itemCards = [];
    private IReadOnlyList<QuestTargetSubmissionTaskView> _tasks = Array.Empty<QuestTargetSubmissionTaskView>();
    private PlayerItemsResponseDto? _items;
    private QuestTargetSubmissionTaskView? _selectedTask;
    private QuestTargetSubmissionTaskView? _confirmTask;

    public QuestTargetSubmissionOverlayState State { get; private set; } = QuestTargetSubmissionOverlayState.Hidden;
    public bool IsOpen => State != QuestTargetSubmissionOverlayState.Hidden;
    public IReadOnlyList<Button> TaskActionButtons => _taskActionButtons;
    public IReadOnlyList<InventoryItemCard> ItemCards => _itemCards;
    public Button ConfirmButton => _confirmButton;
    public Button CancelButton => _cancelButton;
    public Button RetryButton => _retryButton;
    public string StatusText => GodotObject.IsInstanceValid(_statusLabel) ? _statusLabel.Text : "";

    public override void _Ready()
    {
        InventoryItemCardScene ??= GD.Load<PackedScene>("res://scenes/game/ui/InventoryItemCard.tscn");
        _root = GetNode<Control>("OverlayRoot");
        _titleLabel = GetNode<Label>("OverlayRoot/Center/Panel/Margin/Content/Header/TitleLabel");
        _closeButton = GetNode<Button>("OverlayRoot/Center/Panel/Margin/Content/Header/CloseButton");
        _statusLabel = GetNode<Label>("OverlayRoot/Center/Panel/Margin/Content/StatusLabel");
        _taskScroll = GetNode<ScrollContainer>("OverlayRoot/Center/Panel/Margin/Content/TaskScroll");
        _taskList = GetNode<VBoxContainer>("OverlayRoot/Center/Panel/Margin/Content/TaskScroll/TaskList");
        _taskEmptyLabel = GetNode<Label>("OverlayRoot/Center/Panel/Margin/Content/TaskScroll/TaskList/EmptyLabel");
        _itemScroll = GetNode<ScrollContainer>("OverlayRoot/Center/Panel/Margin/Content/ItemScroll");
        _itemList = GetNode<HBoxContainer>("OverlayRoot/Center/Panel/Margin/Content/ItemScroll/ItemList");
        _itemEmptyLabel = GetNode<Label>("OverlayRoot/Center/Panel/Margin/Content/ItemScroll/ItemList/EmptyLabel");
        _backButton = GetNode<Button>("OverlayRoot/Center/Panel/Margin/Content/Footer/BackButton");
        _retryButton = GetNode<Button>("OverlayRoot/Center/Panel/Margin/Content/Footer/RetryButton");
        _confirmLayer = GetNode<Control>("OverlayRoot/ConfirmCenter");
        _confirmMessage = GetNode<Label>("OverlayRoot/ConfirmCenter/Panel/Margin/Content/MessageLabel");
        _confirmButton = GetNode<Button>("OverlayRoot/ConfirmCenter/Panel/Margin/Content/Buttons/ConfirmButton");
        _cancelButton = GetNode<Button>("OverlayRoot/ConfirmCenter/Panel/Margin/Content/Buttons/CancelButton");

        _closeButton.Pressed += OnClosePressed;
        _backButton.Pressed += OnBackPressed;
        _retryButton.Pressed += OnRetryPressed;
        _confirmButton.Pressed += OnConfirmPressed;
        _cancelButton.Pressed += OnCancelPressed;
        CloseOverlay(false);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_closeButton)) _closeButton.Pressed -= OnClosePressed;
        if (GodotObject.IsInstanceValid(_backButton)) _backButton.Pressed -= OnBackPressed;
        if (GodotObject.IsInstanceValid(_retryButton)) _retryButton.Pressed -= OnRetryPressed;
        if (GodotObject.IsInstanceValid(_confirmButton)) _confirmButton.Pressed -= OnConfirmPressed;
        if (GodotObject.IsInstanceValid(_cancelButton)) _cancelButton.Pressed -= OnCancelPressed;
        _taskActionButtons.Clear();
        _itemCards.Clear();
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (!IsOpen || State == QuestTargetSubmissionOverlayState.Submitting
            || inputEvent is not InputEventKey { Pressed: true, Echo: false } key
            || key.Keycode != Key.Escape)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        if (_confirmLayer.Visible)
        {
            OnCancelPressed();
        }
        else if (State == QuestTargetSubmissionOverlayState.ItemSelection)
        {
            ShowTaskList();
        }
        else
        {
            CloseOverlay();
        }
    }

    public void ShowLoading(string message = "正在同步可提交任务……")
    {
        _root.Visible = true;
        _titleLabel.Text = "任务目标提交";
        _statusLabel.Text = message;
        _taskScroll.Visible = false;
        _itemScroll.Visible = false;
        _backButton.Visible = false;
        _retryButton.Visible = false;
        _confirmLayer.Visible = false;
        _closeButton.Disabled = false;
        State = QuestTargetSubmissionOverlayState.Loading;
    }

    public void ShowTasks(
        IReadOnlyList<QuestTargetSubmissionTaskView> tasks,
        PlayerItemsResponseDto items,
        string message = "")
    {
        _tasks = tasks;
        _items = items;
        _root.Visible = true;
        _titleLabel.Text = "任务目标提交";
        _statusLabel.Text = message;
        _confirmLayer.Visible = false;
        _retryButton.Visible = false;
        _closeButton.Disabled = false;
        State = QuestTargetSubmissionOverlayState.Tasks;
        ShowTaskList();
    }

    public void RefreshContent(
        IReadOnlyList<QuestTargetSubmissionTaskView> tasks,
        PlayerItemsResponseDto items,
        string message = "")
    {
        var selectedPlayerQuestId = _selectedTask?.PlayerQuestId;
        _tasks = tasks;
        _items = items;
        _statusLabel.Text = message;
        if (State == QuestTargetSubmissionOverlayState.ItemSelection && selectedPlayerQuestId.HasValue)
        {
            var updated = tasks.FirstOrDefault(task => task.PlayerQuestId == selectedPlayerQuestId.Value);
            if (updated != null)
            {
                OpenItemSelection(updated);
                _statusLabel.Text = message;
                return;
            }
        }
        if (State == QuestTargetSubmissionOverlayState.ConfirmingResource)
        {
            // 费用或资格变化时关闭旧确认，要求玩家基于新快照重新确认。
            _confirmLayer.Visible = false;
        }
        ShowTaskList();
        _statusLabel.Text = message;
    }

    public void ShowError(string message)
    {
        _root.Visible = true;
        _titleLabel.Text = "任务目标提交";
        _statusLabel.Text = message;
        _taskScroll.Visible = false;
        _itemScroll.Visible = false;
        _backButton.Visible = false;
        _retryButton.Visible = true;
        _retryButton.Disabled = false;
        _confirmLayer.Visible = false;
        _closeButton.Disabled = false;
        State = QuestTargetSubmissionOverlayState.Error;
    }

    public void ShowInlineError(string message)
    {
        _statusLabel.Text = message;
        _closeButton.Disabled = false;
        _retryButton.Visible = false;
        if (_selectedTask != null && State == QuestTargetSubmissionOverlayState.ItemSelection)
        {
            OpenItemSelection(_selectedTask);
            _statusLabel.Text = message;
        }
        else
        {
            ShowTaskList();
            _statusLabel.Text = message;
        }
    }

    public void SetSubmitting(string message)
    {
        _statusLabel.Text = message;
        _confirmLayer.Visible = false;
        _closeButton.Disabled = true;
        _retryButton.Visible = false;
        State = QuestTargetSubmissionOverlayState.Submitting;
        foreach (var button in _taskActionButtons) button.Disabled = true;
        foreach (var card in _itemCards) card.UpdateAvailability(new CardActionAvailability(false, "正在提交任务目标……"));
    }

    public void CloseOverlay(bool emitSignal = true)
    {
        var wasOpen = IsOpen;
        State = QuestTargetSubmissionOverlayState.Hidden;
        _selectedTask = null;
        _confirmTask = null;
        if (GodotObject.IsInstanceValid(_root)) _root.Visible = false;
        if (emitSignal && wasOpen) EmitSignal(SignalName.Closed);
    }

    private void ShowTaskList()
    {
        _selectedTask = null;
        _confirmTask = null;
        _confirmLayer.Visible = false;
        _titleLabel.Text = "任务目标提交";
        _taskScroll.Visible = true;
        _itemScroll.Visible = false;
        _backButton.Visible = false;
        _retryButton.Visible = false;
        _closeButton.Disabled = false;
        State = QuestTargetSubmissionOverlayState.Tasks;
        RebuildTaskList();
    }

    private void RebuildTaskList()
    {
        ClearDynamicChildren(_taskList, _taskEmptyLabel);
        _taskActionButtons.Clear();
        _taskEmptyLabel.Visible = _tasks.Count == 0;
        _taskEmptyLabel.Text = "没有需要提交目标的任务";
        foreach (var task in _tasks)
        {
            _taskList.AddChild(BuildTaskCard(task));
        }
    }

    private Control BuildTaskCard(QuestTargetSubmissionTaskView task)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 112), MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddToGroup("blocks_map_input");
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);
        var title = new Label
        {
            Text = task.QuestName,
            TooltipText = task.QuestName,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        title.AddThemeFontSizeOverride("font_size", 19);
        content.AddChild(title);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        content.AddChild(row);
        var requirement = new Label
        {
            Text = task.RequirementText,
            TooltipText = task.RequirementText,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        row.AddChild(requirement);
        var button = new Button
        {
            Text = task.TriggerType == QuestTargetSubmissionModel.SubmitItemTrigger ? "选择道具" : "提交",
            CustomMinimumSize = new Vector2(126, 42),
            Disabled = task.TriggerType == QuestTargetSubmissionModel.PayResourceTrigger && !task.CanExecute,
            TooltipText = task.TriggerType == QuestTargetSubmissionModel.SubmitItemTrigger
                ? "打开符合条件的手牌与已装备实例。"
                : task.CanExecute ? "确认后由服务端扣除任务配置费用。" : task.UnavailableReason
        };
        button.Pressed += () => OnTaskActionPressed(task);
        row.AddChild(button);
        _taskActionButtons.Add(button);
        if (!task.CanExecute && !string.IsNullOrWhiteSpace(task.UnavailableReason))
        {
            var reason = new Label
            {
                Text = task.UnavailableReason,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(0.95f, 0.72f, 0.62f)
            };
            content.AddChild(reason);
        }
        return panel;
    }

    private void OnTaskActionPressed(QuestTargetSubmissionTaskView task)
    {
        if (task.TriggerType == QuestTargetSubmissionModel.SubmitItemTrigger)
        {
            OpenItemSelection(task);
            return;
        }
        if (!task.CanExecute) return;
        _confirmTask = task;
        _confirmMessage.Text = $"确定为「{task.QuestName}」支付：\n{task.RequirementText}？";
        _confirmLayer.Visible = true;
        State = QuestTargetSubmissionOverlayState.ConfirmingResource;
    }

    private void OpenItemSelection(QuestTargetSubmissionTaskView task)
    {
        _selectedTask = task;
        _confirmLayer.Visible = false;
        _titleLabel.Text = $"选择交付道具｜{task.QuestName}";
        _taskScroll.Visible = false;
        _itemScroll.Visible = true;
        _backButton.Visible = true;
        _retryButton.Visible = false;
        State = QuestTargetSubmissionOverlayState.ItemSelection;
        RebuildItemList(task);
    }

    private void RebuildItemList(QuestTargetSubmissionTaskView task)
    {
        ClearDynamicChildren(_itemList, _itemEmptyLabel);
        _itemCards.Clear();
        var views = QuestTargetSubmissionModel.BuildMatchingItems(task, _items?.Items);
        _itemEmptyLabel.Visible = views.Count == 0;
        _itemEmptyLabel.Text = "没有符合条件的道具";
        foreach (var view in views)
        {
            if (!CardArtCatalog.TryGetItemTexture(view.ItemId, out var texture, out var artError) || texture == null)
            {
                _itemEmptyLabel.Visible = true;
                _itemEmptyLabel.Text = artError;
                continue;
            }
            if (InventoryItemCardScene?.Instantiate<InventoryItemCard>() is not { } card)
            {
                _itemEmptyLabel.Visible = true;
                _itemEmptyLabel.Text = "道具卡场景无法实例化。";
                continue;
            }
            var source = _items!.Items!.First(item => item.PlayerItemId == view.PlayerItemId);
            _itemList.AddChild(card);
            card.Bind(
                new CardViewerCard(
                    $"quest-item:{view.PlayerItemId}",
                    texture,
                    ItemId: view.ItemId,
                    PlayerItemId: view.PlayerItemId,
                    ItemName: view.ItemName,
                    Location: view.Location),
                "提交",
                new CardActionAvailability(view.CanSubmit, view.CanSubmit ? "提交该手牌实例。" : view.UnavailableReason),
                260.0f,
                ItemAspectRatio);
            if (string.Equals(source.Location, "EQUIPPED", StringComparison.OrdinalIgnoreCase))
            {
                card.SetReadOnlyStatus("已装备");
            }
            card.ActionRequested += playerItemId =>
                EmitSignal(SignalName.ItemSubmitRequested, task.QuestId, task.PlayerQuestId, playerItemId);
            _itemCards.Add(card);
        }
    }

    private void OnConfirmPressed()
    {
        if (_confirmTask == null || !_confirmTask.CanExecute) return;
        var task = _confirmTask;
        _confirmLayer.Visible = false;
        EmitSignal(SignalName.ResourceSubmitRequested, task.QuestId, task.PlayerQuestId);
    }

    private void OnCancelPressed()
    {
        _confirmTask = null;
        _confirmLayer.Visible = false;
        State = QuestTargetSubmissionOverlayState.Tasks;
    }

    private void OnBackPressed() => ShowTaskList();
    private void OnClosePressed() { if (State != QuestTargetSubmissionOverlayState.Submitting) CloseOverlay(); }
    private void OnRetryPressed() { if (!_retryButton.Disabled) EmitSignal(SignalName.RetryRequested); }

    private static void ClearDynamicChildren(Node parent, Node keep)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child == keep) continue;
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
