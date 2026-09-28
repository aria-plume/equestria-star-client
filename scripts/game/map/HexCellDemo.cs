using Godot;

namespace EquestriaStar.Game.Map;

public partial class HexCellDemo : Node3D
{
    private HexCell3D _cell = null!;
    private HexCell3D _customCell = null!;
    private Label _stateLabel = null!;
    private Label _eventLabel = null!;
    private int _clickCount;

    public override void _Ready()
    {
        _cell = GetNode<HexCell3D>("HexCell3D");
        _customCell = GetNode<HexCell3D>("CustomHexCell3D");
        _stateLabel = GetNode<Label>("Interface/RootMargin/Layout/InfoPanel/InfoMargin/Info/StateLabel");
        _eventLabel = GetNode<Label>("Interface/RootMargin/Layout/InfoPanel/InfoMargin/Info/EventLabel");

        _cell.ShowDebugLabel = true;
        _cell.BindData(new HexCellData
        {
            CellId = 10,
            CellCode = "A10",
            CellName = "测试格",
            CellNameText = "星辉广场",
            Q = 1,
            R = 5,
            InteractionTags = ["SHOP", "STATION"]
        });
        _cell.CellClicked += OnCellClicked;
        _cell.CellHoverChanged += OnCellHoverChanged;
        _cell.VisualStateChanged += OnVisualStateChanged;

        _customCell.ShowDebugLabel = true;
        _customCell.BindData(new HexCellData
        {
            CellId = 21,
            CellCode = "B21",
            CellName = "自定义格",
            CellNameText = "自定义素材",
            Q = 2,
            R = 5,
            InteractionTags = ["DEMO"]
        });
        _customCell.CellClicked += OnCellClicked;
        _customCell.CellHoverChanged += OnCellHoverChanged;
        _customCell.VisualStateChanged += OnVisualStateChanged;

        GetNode<Camera3D>("Camera3D").LookAt(new Vector3(0.0f, -0.06f, 0.0f), Vector3.Up);
        BindStateButton("NormalButton", ApplyNormal);
        BindStateButton("CurrentButton", () => ApplyState(HexCellVisualState.Current));
        BindStateButton("MovableButton", () => ApplyState(HexCellVisualState.Movable));
        BindStateButton("SelectedButton", () => ApplyState(HexCellVisualState.Selected));
        BindStateButton("DisabledButton", () => ApplyState(HexCellVisualState.Disabled));
        ApplyNormal();
    }

    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(_cell))
        {
            return;
        }

        _cell.CellClicked -= OnCellClicked;
        _cell.CellHoverChanged -= OnCellHoverChanged;
        _cell.VisualStateChanged -= OnVisualStateChanged;
        if (GodotObject.IsInstanceValid(_customCell))
        {
            _customCell.CellClicked -= OnCellClicked;
            _customCell.CellHoverChanged -= OnCellHoverChanged;
            _customCell.VisualStateChanged -= OnVisualStateChanged;
        }
    }

    private void BindStateButton(string buttonName, System.Action action)
    {
        var button = GetNode<Button>($"Interface/RootMargin/Layout/StateButtons/{buttonName}");
        button.Pressed += action;
    }

    private void ApplyNormal()
    {
        _cell.ClearStateFlags();
        _customCell.ClearStateFlags();
        _eventLabel.Text = "等待悬停或点击";
    }

    private void ApplyState(HexCellVisualState state)
    {
        _cell.ClearStateFlags();
        _customCell.ClearStateFlags();
        switch (state)
        {
            case HexCellVisualState.Current:
                _cell.SetCurrent(true);
                _customCell.SetCurrent(true);
                break;
            case HexCellVisualState.Movable:
                _cell.SetMovable(true);
                _customCell.SetMovable(true);
                break;
            case HexCellVisualState.Selected:
                _cell.SetSelected(true);
                _customCell.SetSelected(true);
                break;
            case HexCellVisualState.Disabled:
                _cell.SetDisabled(true);
                _customCell.SetDisabled(true);
                break;
        }
    }

    private void OnCellClicked(long cellId)
    {
        _clickCount++;
        _eventLabel.Text = $"点击格子 {cellId}，累计 {_clickCount} 次";
    }

    private void OnCellHoverChanged(long cellId, bool hovered)
    {
        _eventLabel.Text = hovered ? $"悬停格子 {cellId}" : "指针已离开格子";
    }

    private void OnVisualStateChanged(int state)
    {
        _stateLabel.Text = $"当前状态：{(HexCellVisualState)state}";
    }
}
