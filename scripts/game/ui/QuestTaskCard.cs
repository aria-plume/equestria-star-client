using Godot;

namespace EquestriaStar.Game.UI;

public partial class QuestTaskCard : PanelContainer
{
    [Signal]
    public delegate void SubmitRequestedEventHandler(long playerQuestId);

    [Signal]
    public delegate void AbandonRequestedEventHandler(long playerQuestId, long questId);

    private Label _nameLabel = null!;
    private VBoxContainer _objectives = null!;
    private Button _submitButton = null!;
    private Button _abandonButton = null!;
    private long _playerQuestId;
    private long _questId;
    private bool _canSubmit;
    private bool _canAbandon;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Margin/Content/QuestName");
        _objectives = GetNode<VBoxContainer>("Margin/Content/Objectives");
        _submitButton = GetNode<Button>("Margin/Content/Actions/SubmitButton");
        _abandonButton = GetNode<Button>("Margin/Content/Actions/AbandonButton");
        _submitButton.Pressed += OnSubmitPressed;
        _abandonButton.Pressed += OnAbandonPressed;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_submitButton))
        {
            _submitButton.Pressed -= OnSubmitPressed;
        }
        if (GodotObject.IsInstanceValid(_abandonButton))
        {
            _abandonButton.Pressed -= OnAbandonPressed;
        }
    }

    public void Bind(QuestTaskDisplay task, bool busy)
    {
        _playerQuestId = task.PlayerQuestId;
        _questId = task.QuestId;
        _nameLabel.Text = task.QuestName;
        ClearChildren(_objectives);

        foreach (var objective in task.Objectives)
        {
            var row = new HBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            var check = new Label
            {
                Text = objective.Completed ? "☑" : "□",
                CustomMinimumSize = new Vector2(34, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore
            };
            check.AddThemeFontSizeOverride("font_size", 22);
            check.AddThemeColorOverride("font_color", objective.Completed
                ? new Color("d95c5c")
                : new Color("9aa7aa"));
            var text = new Label
            {
                Text = objective.Text + objective.ProgressText,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore
            };
            text.AddThemeColorOverride("font_color", objective.ProgressAvailable
                ? new Color("e7eceb")
                : new Color("e5b66f"));
            row.AddChild(check);
            row.AddChild(text);
            _objectives.AddChild(row);
        }

        _canSubmit = task.CanSubmit;
        _canAbandon = task.CanAbandon;
        _submitButton.Disabled = busy || !_canSubmit;
        _submitButton.TooltipText = task.CanSubmit ? "提交任务" : task.SubmitReason;
        _abandonButton.Disabled = busy || !_canAbandon;
        _abandonButton.TooltipText = task.CanAbandon ? "花费 1 声望放弃任务" : task.AbandonReason;
    }

    public void SetBusy(bool busy)
    {
        _submitButton.Disabled = busy || !_canSubmit;
        _abandonButton.Disabled = busy || !_canAbandon;
    }

    private void OnSubmitPressed() => EmitSignal(SignalName.SubmitRequested, _playerQuestId);

    private void OnAbandonPressed() => EmitSignal(SignalName.AbandonRequested, _playerQuestId, _questId);

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
