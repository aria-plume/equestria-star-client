using System;
using System.Threading.Tasks;
using Godot;

namespace EquestriaStar.Game.Dice;

public partial class DiceDemo : Node3D
{
    private Dice3D _dice = null!;
    private DiceTray _tray = null!;
    private DiceRollController _controller = null!;
    private Label _stateLabel = null!;
    private Label _tierLabel = null!;
    private Label _resultLabel = null!;
    private Button _resetButton = null!;

    public override void _Ready()
    {
        _dice = GetNode<Dice3D>("Dice3D");
        _tray = GetNode<DiceTray>("DiceTray");
        _controller = GetNode<DiceRollController>("CameraRig");
        _stateLabel = GetNode<Label>("Interface/Root/StatusMargin/StatusPanel/StatusContent/StateLabel");
        _tierLabel = GetNode<Label>("Interface/Root/StatusMargin/StatusPanel/StatusContent/TierLabel");
        _resultLabel = GetNode<Label>("Interface/Root/StatusMargin/StatusPanel/StatusContent/ResultLabel");
        _resetButton = GetNode<Button>("Interface/Root/ResetMargin/ResetButton");

        _dice.SafeHalfExtent = _tray.InnerHalfExtent + 0.8f;
        _dice.RollStarted += OnRollStarted;
        _dice.RollCompleted += OnRollCompleted;
        _dice.RollStateChanged += OnRollStateChanged;
        _resetButton.Pressed += OnResetPressed;
        RefreshStatus();

        if (Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--auto-roll"))
        {
            _ = RunAutomaticVisualCheckAsync();
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_dice))
        {
            _dice.RollStarted -= OnRollStarted;
            _dice.RollCompleted -= OnRollCompleted;
            _dice.RollStateChanged -= OnRollStateChanged;
        }

        if (GodotObject.IsInstanceValid(_resetButton))
        {
            _resetButton.Pressed -= OnResetPressed;
        }
    }

    private async Task RunAutomaticVisualCheckAsync()
    {
        await ToSignal(GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);
        if (!IsInsideTree())
        {
            return;
        }

        _dice.BeginRollMode();
        await ToSignal(GetTree().CreateTimer(0.7), Timer.SignalName.Timeout);
        if (!IsInsideTree())
        {
            return;
        }

        _controller.BeginDrag(new Vector2(680.0f, 540.0f), 10.0);
        await ToSignal(GetTree().CreateTimer(0.3), Timer.SignalName.Timeout);
        if (!IsInsideTree())
        {
            return;
        }

        _controller.EndDrag(new Vector2(980.0f, 390.0f), 10.3);
    }

    private void OnResetPressed()
    {
        _controller.CancelDrag();
        _dice.ResetDice();
        RefreshStatus();
    }

    private void OnRollStarted()
    {
        _resultLabel.Text = "点数：—";
        _tierLabel.Text = $"档位：{GetTierText(_dice.LastThrowTier)}";
    }

    private void OnRollCompleted(int value)
    {
        _resultLabel.Text = $"点数：{value}";
    }

    private void OnRollStateChanged(int state)
    {
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _stateLabel.Text = _dice.RollState switch
        {
            DiceRollState.Idle => "状态：待机",
            DiceRollState.AwaitingDrag => "状态：等待拖动",
            DiceRollState.Rolling => "状态：滚动中",
            DiceRollState.Settling => "状态：正在稳定",
            DiceRollState.Completed => "状态：已完成",
            _ => "状态：未知"
        };

        if (_dice.LastRolledValue == 0)
        {
            _resultLabel.Text = "点数：—";
        }

        if (_dice.LastThrowTier == DiceThrowTier.None)
        {
            _tierLabel.Text = "档位：—";
        }
    }

    private static string GetTierText(DiceThrowTier tier)
    {
        return tier switch
        {
            DiceThrowTier.Light => "轻投",
            DiceThrowTier.Normal => "普通",
            DiceThrowTier.Heavy => "重投",
            _ => "—"
        };
    }
}
