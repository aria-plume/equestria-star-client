using System;
using System.Collections.Generic;
using Godot;
using EquestriaStar.Api;

namespace EquestriaStar.Game.Dice;

public enum WorkDiceOverlayState
{
    Hidden,
    SelectingAttribute,
    SubmittingAttribute,
    AwaitingRoll,
    Rolling,
    AwaitingDecision,
    Submitting,
    RecoveryError,
    Result
}

public partial class WorkDiceOverlay : CanvasLayer
{
    [Signal] public delegate void ConfirmRequestedEventHandler(int dice);
    [Signal] public delegate void RerollRequestedEventHandler();
    [Signal] public delegate void RecoveryRetryRequestedEventHandler();
    [Signal] public delegate void ResultDismissedEventHandler();
    [Signal] public delegate void WorkAttributeSelectedEventHandler(string attributeType);
    [Signal] public delegate void WorkAttributeSelectionCancelledEventHandler();

    private Control _root = null!;
    private Control _throwInput = null!;
    private Control _diceViewportContainer = null!;
    private Control _infoAnchor = null!;
    private Control _attributeSelectionAnchor = null!;
    private Control _decisionPanel = null!;
    private Control _resultDismissArea = null!;
    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private Label _detailLabel = null!;
    private Label _diceValueLabel = null!;
    private Label _deadlineLabel = null!;
    private Label _resultLabel = null!;
    private Button _confirmButton = null!;
    private Button _rerollButton = null!;
    private Button _recoveryRetryButton = null!;
    private Button _attributeCancelButton = null!;
    private Label _attributeWorkLabel = null!;
    private Label _attributeStatusLabel = null!;
    private readonly Dictionary<string, Button> _attributeButtons = new(StringComparer.OrdinalIgnoreCase);
    private Dice3D _dice = null!;
    private DiceRollController _controller = null!;
    private PendingCheckDto? _pendingCheck;
    private int _physicalDice;
    private bool _resultDismissArmed;
    private ulong _resultShownAtMsec;

    public WorkDiceOverlayState State { get; private set; } = WorkDiceOverlayState.Hidden;
    public PendingCheckDto? PendingCheck => _pendingCheck;
    public int PhysicalDice => _physicalDice;
    public bool IsOpen => State != WorkDiceOverlayState.Hidden;
    public string CurrentBusinessType => _pendingCheck?.BusinessType?.Trim().ToUpperInvariant() ?? "";
    public bool ConfirmEnabled => GodotObject.IsInstanceValid(_confirmButton) && !_confirmButton.Disabled;
    public bool RerollEnabled => GodotObject.IsInstanceValid(_rerollButton) && !_rerollButton.Disabled;
    public Button ConfirmButton => _confirmButton;
    public Button RerollButton => _rerollButton;
    public Button RecoveryRetryButton => _recoveryRetryButton;
    public Button AttributeCancelButton => _attributeCancelButton;
    public IReadOnlyDictionary<string, Button> AttributeButtons => _attributeButtons;
    public Dice3D Dice => _dice;
    public DiceRollController Controller => _controller;
    public string ResultText => GodotObject.IsInstanceValid(_resultLabel) ? _resultLabel.Text : "";
    public string StatusText => GodotObject.IsInstanceValid(_statusLabel) ? _statusLabel.Text : "";

    public override void _Ready()
    {
        _root = GetNode<Control>("OverlayRoot");
        _throwInput = GetNode<Control>("OverlayRoot/ThrowInput");
        _diceViewportContainer = GetNode<Control>("OverlayRoot/DiceViewportContainer");
        _infoAnchor = GetNode<Control>("OverlayRoot/InfoAnchor");
        _attributeSelectionAnchor = GetNode<Control>("OverlayRoot/AttributeSelectionAnchor");
        _decisionPanel = GetNode<Control>("OverlayRoot/DecisionAnchor/DecisionPanel");
        _resultDismissArea = GetNode<Control>("OverlayRoot/ResultDismissArea");
        _titleLabel = GetNode<Label>("OverlayRoot/InfoAnchor/InfoPanel/InfoMargin/Info/TitleLabel");
        _statusLabel = GetNode<Label>("OverlayRoot/InfoAnchor/InfoPanel/InfoMargin/Info/StatusLabel");
        _detailLabel = GetNode<Label>("OverlayRoot/InfoAnchor/InfoPanel/InfoMargin/Info/DetailLabel");
        _diceValueLabel = GetNode<Label>("OverlayRoot/InfoAnchor/InfoPanel/InfoMargin/Info/DiceValueLabel");
        _deadlineLabel = GetNode<Label>("OverlayRoot/InfoAnchor/InfoPanel/InfoMargin/Info/DeadlineLabel");
        _resultLabel = GetNode<Label>("OverlayRoot/ResultAnchor/ResultPanel/ResultMargin/ResultLabel");
        _confirmButton = GetNode<Button>("OverlayRoot/DecisionAnchor/DecisionPanel/DecisionMargin/Buttons/ConfirmButton");
        _rerollButton = GetNode<Button>("OverlayRoot/DecisionAnchor/DecisionPanel/DecisionMargin/Buttons/RerollButton");
        _recoveryRetryButton = GetNode<Button>("OverlayRoot/DecisionAnchor/DecisionPanel/DecisionMargin/Buttons/RecoveryRetryButton");
        _attributeCancelButton = GetNode<Button>("OverlayRoot/AttributeSelectionAnchor/AttributePanel/AttributeMargin/Content/CancelButton");
        _attributeWorkLabel = GetNode<Label>("OverlayRoot/AttributeSelectionAnchor/AttributePanel/AttributeMargin/Content/WorkLabel");
        _attributeStatusLabel = GetNode<Label>("OverlayRoot/AttributeSelectionAnchor/AttributePanel/AttributeMargin/Content/StatusLabel");
        _dice = GetNode<Dice3D>("OverlayRoot/DiceViewportContainer/DiceViewport/WorldRoot/Dice3D");
        _controller = GetNode<DiceRollController>("OverlayRoot/DiceViewportContainer/DiceViewport/WorldRoot/CameraRig");

        _throwInput.GuiInput += OnThrowInput;
        _resultDismissArea.GuiInput += OnResultDismissInput;
        _confirmButton.Pressed += OnConfirmPressed;
        _rerollButton.Pressed += OnRerollPressed;
        _recoveryRetryButton.Pressed += OnRecoveryRetryPressed;
        _attributeCancelButton.Pressed += OnAttributeCancelPressed;
        BindAttributeButton("STRENGTH", "StrengthButton");
        BindAttributeButton("SPEED", "SpeedButton");
        BindAttributeButton("SOCIAL", "SocialButton");
        BindAttributeButton("CHARM", "CharmButton");
        BindAttributeButton("MAGIC", "MagicButton");
        BindAttributeButton("MIND", "MindButton");
        _dice.RollStarted += OnRollStarted;
        _dice.RollCompleted += OnRollCompleted;
        CloseOverlay();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_throwInput)) _throwInput.GuiInput -= OnThrowInput;
        if (GodotObject.IsInstanceValid(_resultDismissArea)) _resultDismissArea.GuiInput -= OnResultDismissInput;
        if (GodotObject.IsInstanceValid(_confirmButton)) _confirmButton.Pressed -= OnConfirmPressed;
        if (GodotObject.IsInstanceValid(_rerollButton)) _rerollButton.Pressed -= OnRerollPressed;
        if (GodotObject.IsInstanceValid(_recoveryRetryButton)) _recoveryRetryButton.Pressed -= OnRecoveryRetryPressed;
        if (GodotObject.IsInstanceValid(_attributeCancelButton)) _attributeCancelButton.Pressed -= OnAttributeCancelPressed;
        _attributeButtons.Clear();
        if (GodotObject.IsInstanceValid(_dice))
        {
            _dice.RollStarted -= OnRollStarted;
            _dice.RollCompleted -= OnRollCompleted;
            _dice.ResetDice();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _pendingCheck == null)
        {
            return;
        }

        _deadlineLabel.Text = FormatDeadline(_pendingCheck.ExpiresAt);
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (!IsOpen || inputEvent is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            // 未完成判定时禁止用 Esc 返回地图；结果页仍按鼠标新点击关闭。
            GetViewport().SetInputAsHandled();
        }
    }

    public void OpenPending(PendingCheckDto pendingCheck, bool recovered)
    {
        _pendingCheck = pendingCheck;
        _physicalDice = 0;
        _root.Visible = true;
        SetDiceContentVisible(true);
        _attributeSelectionAnchor.Visible = false;
        _resultDismissArmed = false;
        _resultDismissArea.Visible = false;
        _decisionPanel.Visible = false;
        GetNode<Control>("OverlayRoot/ResultAnchor").Visible = false;
        var forest = IsForestMove(pendingCheck);
        var quest = IsQuestCheck(pendingCheck);
        _titleLabel.Text = forest
            ? (recovered ? "恢复森林方向骰" : "森林方向骰")
            : quest
                ? (recovered ? "恢复任务判定" : "任务判定")
                : (recovered ? "恢复打工判定" : "打工判定");
        _statusLabel.Text = recovered ? "已恢复服务端待判定，请重新投掷物理骰。" : "按住鼠标拖动，松开后投掷。";
        _detailLabel.Text = BuildCheckDetail(pendingCheck);
        _diceValueLabel.Text = "点数：—";
        _deadlineLabel.Text = FormatDeadline(pendingCheck.ExpiresAt);
        _recoveryRetryButton.Visible = false;
        State = WorkDiceOverlayState.AwaitingRoll;
        StartFreshPhysicalRoll();
    }

    public void OpenAttributeSelection(CurrentWorkResponseDto currentWork)
    {
        _controller.CancelDrag();
        _dice.ResetDice();
        _pendingCheck = null;
        _physicalDice = 0;
        _root.Visible = true;
        SetDiceContentVisible(false);
        _attributeSelectionAnchor.Visible = true;
        _decisionPanel.Visible = false;
        _resultDismissArea.Visible = false;
        GetNode<Control>("OverlayRoot/ResultAnchor").Visible = false;
        _attributeWorkLabel.Text = BuildWorkSelectionDetail(currentWork);
        _attributeStatusLabel.Text = "请选择本次打工使用的具体属性。";
        SetAttributeButtonsDisabled(false);
        _attributeCancelButton.Disabled = false;
        State = WorkDiceOverlayState.SelectingAttribute;
    }

    public void SetAttributeSelectionSubmitting()
    {
        if (State != WorkDiceOverlayState.SelectingAttribute)
        {
            return;
        }

        State = WorkDiceOverlayState.SubmittingAttribute;
        _attributeStatusLabel.Text = "正在核对最新状态并提交打工准备……";
        SetAttributeButtonsDisabled(true);
        _attributeCancelButton.Disabled = true;
    }

    public void UpdatePendingCapabilities(PendingCheckDto pendingCheck)
    {
        if (_pendingCheck?.CheckId != pendingCheck.CheckId)
        {
            OpenPending(pendingCheck, true);
            return;
        }

        _pendingCheck = pendingCheck;
        _detailLabel.Text = BuildCheckDetail(pendingCheck);
        _deadlineLabel.Text = FormatDeadline(pendingCheck.ExpiresAt);
        if (State == WorkDiceOverlayState.AwaitingDecision)
        {
            RefreshDecisionButtons();
        }
    }

    public void ApplyRerollAuthorization(PendingCheckDto pendingCheck)
    {
        _pendingCheck = pendingCheck;
        _physicalDice = 0;
        _statusLabel.Text = "重投已获服务端授权，请投掷新的物理骰。";
        _detailLabel.Text = BuildCheckDetail(pendingCheck);
        _diceValueLabel.Text = "点数：—";
        _decisionPanel.Visible = false;
        State = WorkDiceOverlayState.AwaitingRoll;
        StartFreshPhysicalRoll();
    }

    public void SetSubmitting(string message)
    {
        State = WorkDiceOverlayState.Submitting;
        _statusLabel.Text = message;
        _confirmButton.Disabled = true;
        _rerollButton.Disabled = true;
        _recoveryRetryButton.Disabled = true;
    }

    public void RestoreDecision(PendingCheckDto pendingCheck, string message)
    {
        _pendingCheck = pendingCheck;
        _statusLabel.Text = message;
        State = WorkDiceOverlayState.AwaitingDecision;
        _decisionPanel.Visible = true;
        _recoveryRetryButton.Visible = false;
        RefreshDecisionButtons();
    }

    public void ShowRecoveryError(string message)
    {
        SetDiceContentVisible(true);
        _attributeSelectionAnchor.Visible = false;
        State = WorkDiceOverlayState.RecoveryError;
        _statusLabel.Text = message;
        _decisionPanel.Visible = true;
        _confirmButton.Visible = false;
        _rerollButton.Visible = false;
        _recoveryRetryButton.Visible = true;
        _recoveryRetryButton.Disabled = false;
    }

    public void ShowRecoveryUnavailable(string message)
    {
        _statusLabel.Text = "判定恢复结束";
        _resultLabel.Text = $"{message}\n\n已刷新当前权威状态。点击任意位置返回游戏。";
        EnterResultState();
    }

    public void ShowWorkResult(CheckRollResponseDto response)
    {
        var work = response.WorkResult!;
        var check = response.CheckResult!;
        var success = check.Success == true;
        var submitted = response.SubmittedDice!.Value;
        var effective = response.EffectiveDice!.Value;
        var diceLine = submitted == effective
            ? $"最终点数：{effective}"
            : $"提交点数：{submitted}　服务端有效点数：{effective}";
        var pendingRewardLine = response.PlayerViewState?.EngineState?.PendingReward == null
            ? ""
            : "\n另有服务端 FIFO 奖励待办，请回到游戏后按权威状态继续处理。";
        _resultLabel.Text =
            $"打工{(success ? "成功" : "失败")}\n{diceLine}\n" +
            $"本次收入：{work.RewardCoins} 金币\n金币余额：{work.CoinsAfter}\n" +
            $"社交余额：{work.RemainingSocial}\n基础社交费用：{work.SocialCost}" +
            pendingRewardLine + "\n\n点击任意位置返回游戏。";
        EnterResultState();
    }

    public void ShowForestMoveResult(CheckRollResponseDto response)
    {
        var movement = response.Movement!;
        var effective = response.EffectiveDice!.Value;
        var submitted = response.SubmittedDice!.Value;
        var moved = string.Equals(movement.MovementOutcome, "MOVED", StringComparison.OrdinalIgnoreCase);
        var outcome = moved ? "已抵达" : "原地停留";
        var targetName = FirstNonEmpty(
            movement.ActualTargetCellName,
            FirstNonEmpty(
                movement.ActualTargetCellCode,
                movement.ActualTargetCellId > 0 ? movement.ActualTargetCellId.ToString() : "未知格子"));
        var diceLine = submitted == effective
            ? $"最终点数：{effective}"
            : $"提交点数：{submitted}　服务端有效点数：{effective}";
        var reason = moved
            ? ""
            : $"\n停留原因：{FormatStayReason(movement.StayReason)}";
        var cost = movement.CostCoins.HasValue ? $"{movement.CostCoins.Value} 金币" : "历史回执未提供";
        var balance = movement.CoinsAfter.HasValue ? movement.CoinsAfter.Value.ToString() : "历史回执未提供";
        var message = string.IsNullOrWhiteSpace(response.PlayerFacingMessage)
            ? ""
            : $"\n{response.PlayerFacingMessage!.Trim()}";

        _resultLabel.Text =
            $"森林移动结果：{outcome}\n{diceLine}\n实际位置：{targetName}" +
            $"\n本次金币费用：{cost}\n金币余额：{balance}" +
            reason + message + "\n\n点击任意位置返回游戏。";
        EnterResultState();
    }

    public void ShowQuestResult(CheckRollResponseDto response)
    {
        var success = response.CheckResult?.Success == true;
        var submitted = response.SubmittedDice!.Value;
        var effective = response.EffectiveDice!.Value;
        var diceLine = submitted == effective
            ? $"最终点数：{effective}"
            : $"提交点数：{submitted}　服务端有效点数：{effective}";
        var message = string.IsNullOrWhiteSpace(response.PlayerFacingMessage)
            ? ""
            : $"\n{response.PlayerFacingMessage!.Trim()}";
        _resultLabel.Text =
            $"任务判定{(success ? "成功" : "失败")}\n{diceLine}{message}\n" +
            "任务目标将以重新查询的完整任务列表为准；判定成功不等于任务已提交或奖励已入账。\n\n" +
            "点击任意位置返回判定列表。";
        EnterResultState();
    }

    public void CloseOverlay()
    {
        State = WorkDiceOverlayState.Hidden;
        _pendingCheck = null;
        _physicalDice = 0;
        _resultDismissArmed = false;
        if (GodotObject.IsInstanceValid(_controller)) _controller.CancelDrag();
        if (GodotObject.IsInstanceValid(_dice)) _dice.ResetDice();
        if (GodotObject.IsInstanceValid(_root)) _root.Visible = false;
        if (GodotObject.IsInstanceValid(_attributeSelectionAnchor)) _attributeSelectionAnchor.Visible = false;
    }

    private void StartFreshPhysicalRoll()
    {
        _controller.CancelDrag();
        _dice.ResetDice();
        _dice.BeginRollMode();
    }

    private void EnterResultState()
    {
        SetDiceContentVisible(true);
        _attributeSelectionAnchor.Visible = false;
        State = WorkDiceOverlayState.Result;
        _decisionPanel.Visible = false;
        GetNode<Control>("OverlayRoot/ResultAnchor").Visible = true;
        _resultDismissArea.Visible = true;
        _resultDismissArmed = false;
        _resultShownAtMsec = Time.GetTicksMsec();
        CallDeferred(MethodName.ArmResultDismiss);
    }

    private void BindAttributeButton(string attributeType, string nodeName)
    {
        var button = GetNode<Button>($"OverlayRoot/AttributeSelectionAnchor/AttributePanel/AttributeMargin/Content/AttributeGrid/{nodeName}");
        _attributeButtons[attributeType] = button;
        button.Pressed += () => OnAttributePressed(attributeType);
    }

    private void OnAttributePressed(string attributeType)
    {
        if (State != WorkDiceOverlayState.SelectingAttribute
            || !WorkAttributeSelectionModel.TryNormalizeSelectable(attributeType, out var normalized))
        {
            return;
        }

        EmitSignal(SignalName.WorkAttributeSelected, normalized);
    }

    private void OnAttributeCancelPressed()
    {
        if (State != WorkDiceOverlayState.SelectingAttribute || _attributeCancelButton.Disabled)
        {
            return;
        }

        EmitSignal(SignalName.WorkAttributeSelectionCancelled);
    }

    private void SetAttributeButtonsDisabled(bool disabled)
    {
        foreach (var button in _attributeButtons.Values)
        {
            button.Disabled = disabled;
        }
    }

    private void SetDiceContentVisible(bool visible)
    {
        _diceViewportContainer.Visible = visible;
        _throwInput.Visible = visible;
        _infoAnchor.Visible = visible;
    }

    private static string BuildWorkSelectionDetail(CurrentWorkResponseDto currentWork)
    {
        var name = FirstNonEmpty(currentWork.CellNameText, FirstNonEmpty(currentWork.CellName, "当前位置"));
        var difficulty = currentWork.Difficulty?.ToString() ?? "-";
        var cost = currentWork.SocialCost?.ToString() ?? "-";
        var reward = currentWork.RewardCoins?.ToString() ?? "-";
        return $"{name}\n难度：{difficulty}　基础社交费用：{cost}　基础成功奖励预览：{reward} 金币";
    }

    private void ArmResultDismiss()
    {
        _resultDismissArmed = State == WorkDiceOverlayState.Result;
    }

    private void OnThrowInput(InputEvent inputEvent)
    {
        if (State is not (WorkDiceOverlayState.AwaitingRoll or WorkDiceOverlayState.Rolling))
        {
            return;
        }

        if (inputEvent is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouse.Pressed)
        {
            _controller.BeginDrag(mouse.Position);
        }
        else
        {
            _controller.EndDrag(mouse.Position);
        }

        _throwInput.AcceptEvent();
    }

    private void OnResultDismissInput(InputEvent inputEvent)
    {
        if (State != WorkDiceOverlayState.Result
            || !_resultDismissArmed
            || Time.GetTicksMsec() <= _resultShownAtMsec
            || inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            return;
        }

        _resultDismissArea.AcceptEvent();
        EmitSignal(SignalName.ResultDismissed);
    }

    private void OnConfirmPressed()
    {
        if (State != WorkDiceOverlayState.AwaitingDecision || _confirmButton.Disabled || _physicalDice is < 1 or > 6)
        {
            return;
        }

        EmitSignal(SignalName.ConfirmRequested, _physicalDice);
    }

    private void OnRerollPressed()
    {
        if (State != WorkDiceOverlayState.AwaitingDecision || _rerollButton.Disabled)
        {
            return;
        }

        EmitSignal(SignalName.RerollRequested);
    }

    private void OnRecoveryRetryPressed()
    {
        if (State != WorkDiceOverlayState.RecoveryError || _recoveryRetryButton.Disabled)
        {
            return;
        }

        _recoveryRetryButton.Disabled = true;
        EmitSignal(SignalName.RecoveryRetryRequested);
    }

    private void OnRollStarted()
    {
        State = WorkDiceOverlayState.Rolling;
        _statusLabel.Text = "骰子滚动中……";
    }

    private void OnRollCompleted(int value)
    {
        if (State is not (WorkDiceOverlayState.Rolling or WorkDiceOverlayState.AwaitingRoll))
        {
            return;
        }

        _physicalDice = value;
        State = WorkDiceOverlayState.AwaitingDecision;
        _statusLabel.Text = "物理骰已停止。确认前不会上传点数。";
        _diceValueLabel.Text = $"点数：{value}";
        _decisionPanel.Visible = true;
        _confirmButton.Visible = true;
        _rerollButton.Visible = true;
        _recoveryRetryButton.Visible = false;
        RefreshDecisionButtons();
    }

    private void RefreshDecisionButtons()
    {
        var pending = _pendingCheck;
        var canConfirm = pending?.CanConfirm == true && _physicalDice is >= 1 and <= 6;
        _confirmButton.Text = "确认";
        _confirmButton.Disabled = !canConfirm;
        _confirmButton.TooltipText = canConfirm ? "" : FirstNonEmpty(pending?.ConfirmUnavailableReason, "当前不能确认该点数。");

        var cost = pending?.NextRerollSocialCost;
        _rerollButton.Text = cost.HasValue ? $"重投（{cost.Value} 社交）" : "重投";
        _rerollButton.Disabled = pending?.CanReroll != true;
        _rerollButton.TooltipText = _rerollButton.Disabled
            ? FirstNonEmpty(pending?.RerollUnavailableReason, "当前不能重投。")
            : "先向服务端申请重投授权；授权成功后旧点数立即失效。";
    }

    private static string BuildCheckDetail(PendingCheckDto pending)
    {
        if (IsForestMove(pending))
        {
            var preparation = pending.Preparation;
            var from = preparation?.FromCellId?.ToString() ?? "-";
            var target = preparation?.RequestedTargetCellId?.ToString() ?? "-";
            var direction = preparation?.DirectionIndex?.ToString() ?? "-";
            return $"方向骰不使用六维属性　出发格：{from}　所选目标：{target}　方向索引：{direction}";
        }

        var attribute = FirstNonEmpty(pending.AttributeType, "未知属性");
        var value = pending.AttributeValue?.ToString() ?? "-";
        var difficulty = pending.Difficulty?.ToString() ?? "-";
        return $"属性：{attribute}　有效属性值：{value}　难度：{difficulty}";
    }

    private static bool IsForestMove(PendingCheckDto pending) =>
        string.Equals(pending.BusinessType, "FOREST_MOVE", StringComparison.OrdinalIgnoreCase);

    private static bool IsQuestCheck(PendingCheckDto pending) =>
        string.Equals(pending.BusinessType, "QUEST", StringComparison.OrdinalIgnoreCase);

    private static string FormatStayReason(string? reason) => reason?.Trim().ToUpperInvariant() switch
    {
        "NO_CELL" => "偏移方向没有格子",
        "NO_ROAD" => "偏移方向没有道路",
        "ROAD_CLOSED" => "偏移道路不可通行",
        "RAILWAY_ONLY" => "偏移方向仅有铁路",
        "REQUIRED_ITEM_MISSING" => "缺少通行所需物品",
        "CELL_BLOCKED" => "目标格当前被阻塞",
        null or "" => "服务端未提供详细原因",
        _ => reason!.Trim()
    };

    private static string FormatDeadline(string? expiresAt)
    {
        if (!DateTimeOffset.TryParse(expiresAt, out var deadline))
        {
            return "服务端期限：同步中";
        }

        var remaining = deadline - DateTimeOffset.Now;
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"服务端期限约剩 {seconds} 秒（归零不代表本地结算）";
    }

    private static string FirstNonEmpty(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
