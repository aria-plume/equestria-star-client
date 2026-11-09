using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EquestriaStar.Game.UI;

public enum QuestCheckOverlayState
{
    Hidden,
    Loading,
    Content,
    SelectingAttribute,
    Submitting,
    Error
}

public partial class QuestCheckOverlay : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void CheckRequestedEventHandler(
        long questId,
        long playerQuestId,
        string actionType,
        string attributeType);

    private Control _root = null!;
    private Label _statusLabel = null!;
    private Label _emptyLabel = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _taskList = null!;
    private Button _closeButton = null!;
    private Button _retryButton = null!;
    private Control _selectionPanel = null!;
    private Label _selectionTitle = null!;
    private Button _selectionBackButton = null!;
    private readonly Dictionary<string, Button> _attributeButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _selectedAttributes = [];
    private readonly List<Button> _actionButtons = [];
    private IReadOnlyList<QuestCheckTaskView> _tasks = Array.Empty<QuestCheckTaskView>();
    private QuestCheckTaskView? _selectionTask;

    public QuestCheckOverlayState State { get; private set; } = QuestCheckOverlayState.Hidden;
    public bool IsOpen => State != QuestCheckOverlayState.Hidden;
    public Button CloseButton => _closeButton;
    public Button RetryButton => _retryButton;
    public IReadOnlyDictionary<string, Button> AttributeButtons => _attributeButtons;
    public IReadOnlyList<Button> ActionButtons => _actionButtons;
    public IReadOnlyList<QuestCheckTaskView> Tasks => _tasks;
    public string StatusText => GodotObject.IsInstanceValid(_statusLabel) ? _statusLabel.Text : "";

    public override void _Ready()
    {
        _root = GetNode<Control>("OverlayRoot");
        _statusLabel = GetNode<Label>("OverlayRoot/Center/Panel/ContentMargin/Content/StatusLabel");
        _emptyLabel = GetNode<Label>("OverlayRoot/Center/Panel/ContentMargin/Content/TaskScroll/TaskList/EmptyLabel");
        _scroll = GetNode<ScrollContainer>("OverlayRoot/Center/Panel/ContentMargin/Content/TaskScroll");
        _taskList = GetNode<VBoxContainer>("OverlayRoot/Center/Panel/ContentMargin/Content/TaskScroll/TaskList");
        _closeButton = GetNode<Button>("OverlayRoot/Center/Panel/ContentMargin/Content/Header/CloseButton");
        _retryButton = GetNode<Button>("OverlayRoot/Center/Panel/ContentMargin/Content/RetryButton");
        _selectionPanel = GetNode<Control>("OverlayRoot/AttributeSelectionCenter");
        _selectionTitle = GetNode<Label>("OverlayRoot/AttributeSelectionCenter/Panel/Margin/Content/TitleLabel");
        _selectionBackButton = GetNode<Button>("OverlayRoot/AttributeSelectionCenter/Panel/Margin/Content/BackButton");

        _closeButton.Pressed += OnClosePressed;
        _retryButton.Pressed += OnRetryPressed;
        _selectionBackButton.Pressed += OnSelectionBackPressed;
        BindAttributeButton("STRENGTH", "StrengthButton");
        BindAttributeButton("SPEED", "SpeedButton");
        BindAttributeButton("SOCIAL", "SocialButton");
        BindAttributeButton("CHARM", "CharmButton");
        BindAttributeButton("MAGIC", "MagicButton");
        BindAttributeButton("MIND", "MindButton");
        CloseOverlay(false);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_closeButton)) _closeButton.Pressed -= OnClosePressed;
        if (GodotObject.IsInstanceValid(_retryButton)) _retryButton.Pressed -= OnRetryPressed;
        if (GodotObject.IsInstanceValid(_selectionBackButton)) _selectionBackButton.Pressed -= OnSelectionBackPressed;
        _attributeButtons.Clear();
        _actionButtons.Clear();
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (!IsOpen || inputEvent is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != Key.Escape)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if (State == QuestCheckOverlayState.SelectingAttribute)
        {
            ShowTaskList();
        }
        else if (State is not QuestCheckOverlayState.Submitting)
        {
            CloseOverlay();
        }
    }

    public void ShowLoading(string message = "正在同步任务判定……", bool allowClose = true)
    {
        _root.Visible = true;
        _selectionPanel.Visible = false;
        _scroll.Visible = false;
        _emptyLabel.Visible = false;
        _retryButton.Visible = false;
        _closeButton.Disabled = !allowClose;
        _statusLabel.Text = message;
        State = QuestCheckOverlayState.Loading;
    }

    public void ShowContent(IReadOnlyList<QuestCheckTaskView> tasks, string message = "")
    {
        _tasks = tasks;
        var validIds = tasks.Select(task => task.PlayerQuestId).ToHashSet();
        foreach (var id in _selectedAttributes.Keys.Where(id => !validIds.Contains(id)).ToArray())
        {
            _selectedAttributes.Remove(id);
        }
        _root.Visible = true;
        _selectionPanel.Visible = false;
        _scroll.Visible = true;
        _retryButton.Visible = false;
        _closeButton.Disabled = false;
        _statusLabel.Text = message;
        State = QuestCheckOverlayState.Content;
        RebuildTaskList();
    }

    public void ShowError(string message)
    {
        _root.Visible = true;
        _selectionPanel.Visible = false;
        _scroll.Visible = false;
        _retryButton.Visible = true;
        _retryButton.Disabled = false;
        _closeButton.Disabled = false;
        _statusLabel.Text = message;
        State = QuestCheckOverlayState.Error;
    }

    public void ShowInlineError(string message)
    {
        _statusLabel.Text = message;
        _selectionPanel.Visible = false;
        _scroll.Visible = true;
        _closeButton.Disabled = false;
        State = QuestCheckOverlayState.Content;
        RebuildTaskList();
    }

    public void SetSubmitting(long playerQuestId, string attributeType)
    {
        _statusLabel.Text = $"正在准备“{QuestCheckModel.AttributeName(attributeType)}”任务判定……";
        _selectionPanel.Visible = false;
        _scroll.Visible = true;
        _closeButton.Disabled = true;
        State = QuestCheckOverlayState.Submitting;
        foreach (var button in _actionButtons)
        {
            button.Disabled = true;
        }
    }

    public void CloseOverlay(bool emitSignal = true)
    {
        var wasOpen = IsOpen;
        State = QuestCheckOverlayState.Hidden;
        _selectionTask = null;
        if (GodotObject.IsInstanceValid(_root)) _root.Visible = false;
        if (emitSignal && wasOpen) EmitSignal(SignalName.Closed);
    }

    private void RebuildTaskList()
    {
        foreach (var child in _taskList.GetChildren())
        {
            if (child == _emptyLabel) continue;
            _taskList.RemoveChild(child);
            child.QueueFree();
        }
        _actionButtons.Clear();
        _emptyLabel.Visible = _tasks.Count == 0;
        _emptyLabel.Text = "没有需要判定的任务";

        foreach (var task in _tasks)
        {
            _taskList.AddChild(BuildTaskCard(task));
        }
    }

    private Control BuildTaskCard(QuestCheckTaskView task)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 92),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
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
            Text = task.ReadyToSubmit ? $"{task.QuestName}　[已达标]" : task.QuestName,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = task.QuestName
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(title);

        foreach (var row in task.Rows)
        {
            content.AddChild(BuildCheckRow(task, row));
        }
        return panel;
    }

    private Control BuildCheckRow(QuestCheckTaskView task, QuestCheckRowView row)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 10);
        string selected = "";
        if (row.IsChoice) _selectedAttributes.TryGetValue(task.PlayerQuestId, out selected!);
        var attributeText = row.IsChoice
            ? (string.IsNullOrWhiteSpace(selected)
                ? "任选"
                : $"任选（已选：{QuestCheckModel.AttributeName(selected)}）")
            : QuestCheckModel.AttributeName(row.AttributeType);
        var difficulty = row.Difficulty?.ToString() ?? "未同步";
        var label = new Label
        {
            Text = $"{attributeText}　｜　难度 {difficulty}" + (row.Completed ? "　✓ 已完成" : ""),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Modulate = row.Completed ? new Color(0.65f, 0.68f, 0.7f) : Colors.White
        };
        line.AddChild(label);

        if (row.IsChoice)
        {
            var chooseButton = new Button
            {
                Text = string.IsNullOrWhiteSpace(selected) ? "选择属性" : "更换属性",
                CustomMinimumSize = new Vector2(112, 38),
                Disabled = !row.CanExecute,
                TooltipText = row.CanExecute ? "从服务端允许的属性中选择一个。" : row.UnavailableReason
            };
            chooseButton.Pressed += () => OpenAttributeSelection(task, row);
            line.AddChild(chooseButton);
        }

        var canSubmit = row.CanExecute && (!row.IsChoice || !string.IsNullOrWhiteSpace(selected));
        var actionButton = new Button
        {
            Text = "判定",
            CustomMinimumSize = new Vector2(100, 38),
            Disabled = !canSubmit,
            TooltipText = canSubmit
                ? "准备判定；投骰停止前不会上传点数。"
                : FirstNonEmpty(row.UnavailableReason, row.IsChoice ? "请先选择具体属性。" : "当前不能判定。")
        };
        actionButton.Pressed += () => OnCheckPressed(task, row, selected);
        _actionButtons.Add(actionButton);
        line.AddChild(actionButton);
        return line;
    }

    private void OpenAttributeSelection(QuestCheckTaskView task, QuestCheckRowView row)
    {
        if (State != QuestCheckOverlayState.Content || !row.CanExecute)
        {
            return;
        }
        _selectionTask = task;
        _selectionTitle.Text = $"{task.QuestName}\n请选择本次判定属性";
        var allowed = row.ChoiceAttributes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _attributeButtons)
        {
            pair.Value.Visible = allowed.Contains(pair.Key);
            pair.Value.Disabled = !allowed.Contains(pair.Key);
        }
        _scroll.Visible = false;
        _selectionPanel.Visible = true;
        State = QuestCheckOverlayState.SelectingAttribute;
    }

    private void ShowTaskList()
    {
        _selectionTask = null;
        _selectionPanel.Visible = false;
        _scroll.Visible = true;
        State = QuestCheckOverlayState.Content;
        RebuildTaskList();
    }

    private void BindAttributeButton(string attributeType, string nodeName)
    {
        var button = GetNode<Button>($"OverlayRoot/AttributeSelectionCenter/Panel/Margin/Content/AttributeGrid/{nodeName}");
        _attributeButtons[attributeType] = button;
        button.Pressed += () => OnAttributeSelected(attributeType);
    }

    private void OnAttributeSelected(string attributeType)
    {
        if (State != QuestCheckOverlayState.SelectingAttribute || _selectionTask == null)
        {
            return;
        }
        var task = _selectionTask;
        var row = task.Rows.FirstOrDefault(candidate => candidate.IsChoice);
        if (row == null || !row.ChoiceAttributes.Contains(attributeType, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        _selectedAttributes[task.PlayerQuestId] = attributeType;
        ShowTaskList();
    }

    private void OnCheckPressed(QuestCheckTaskView task, QuestCheckRowView row, string selectedAttribute)
    {
        if (State != QuestCheckOverlayState.Content || !row.CanExecute)
        {
            return;
        }
        var attribute = row.IsChoice ? selectedAttribute : row.AttributeType;
        if (!QuestCheckModel.TryNormalizeAttribute(attribute, out var normalized))
        {
            return;
        }
        EmitSignal(SignalName.CheckRequested, task.QuestId, task.PlayerQuestId, task.ActionType, normalized);
    }

    private void OnClosePressed()
    {
        if (State != QuestCheckOverlayState.Submitting) CloseOverlay();
    }

    private void OnRetryPressed()
    {
        if (State != QuestCheckOverlayState.Error || _retryButton.Disabled) return;
        _retryButton.Disabled = true;
        EmitSignal(SignalName.RetryRequested);
    }

    private void OnSelectionBackPressed()
    {
        if (State == QuestCheckOverlayState.SelectingAttribute) ShowTaskList();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return "";
    }
}
