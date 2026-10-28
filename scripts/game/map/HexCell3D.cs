using Godot;
using System.Collections.Generic;
using System.Globalization;

namespace EquestriaStar.Game.Map;

public enum HexCellVisualState
{
    Normal,
    Current,
    Hovered,
    Movable,
    Selected,
    Disabled
}

[Tool]
public partial class HexCell3D : Node3D
{
    [Signal]
    public delegate void CellClickedEventHandler(long cellId);

    [Signal]
    public delegate void CellHoverChangedEventHandler(long cellId, bool hovered);

    [Signal]
    public delegate void VisualStateChangedEventHandler(int state);

    private MeshInstance3D? _baseMesh;
    private MeshInstance3D _customMeshInstance = null!;
    private Node3D _customVisualRoot = null!;
    private Node? _customVisualInstance;
    private MeshInstance3D _highlightMesh = null!;
    private Area3D _interactionArea = null!;
    private CollisionShape3D _collisionShape = null!;
    private Label3D _cellNameLabel = null!;
    private float _radius = 1.0f;
    private float _height = 0.2f;
    private float _bevelWidth = 0.05f;
    private bool _hovered;
    private bool _isCurrent;
    private bool _isMovable;
    private bool _isSelected;
    private bool _isDisabled;
    private bool _showCellNames;
    private int _baseCellNameFontSize;
    private float _cellNameHeight = 0.32f;
    private uint _inputCollisionLayer = 128;
    private Mesh? _customMesh;
    private PackedScene? _customVisualScene;
    private bool _warnedAboutMultipleVisualSources;
    private bool _pendingPointerClick;

    [ExportGroup("初始数据")]
    [Export]
    public HexCellData? InitialData { get; set; }

    [ExportGroup("美术替换")]
    [Export]
    public Mesh? CustomMesh
    {
        get => _customMesh;
        set
        {
            if (ReferenceEquals(_customMesh, value))
            {
                return;
            }

            _customMesh = value;
            RefreshVisualSourceIfReady();
        }
    }

    [Export]
    public PackedScene? CustomVisualScene
    {
        get => _customVisualScene;
        set
        {
            if (ReferenceEquals(_customVisualScene, value))
            {
                return;
            }

            _customVisualScene = value;
            RefreshVisualSourceIfReady();
        }
    }

    [ExportGroup("几何")]
    [Export(PropertyHint.Range, "0.1,10.0,0.01")]
    public float Radius
    {
        get => _radius;
        set => SetGeometryValue(ref _radius, Mathf.Max(0.1f, value));
    }

    [Export(PropertyHint.Range, "0.02,2.0,0.01")]
    public float Height
    {
        get => _height;
        set => SetGeometryValue(ref _height, Mathf.Max(0.02f, value));
    }

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float BevelWidth
    {
        get => _bevelWidth;
        set => SetGeometryValue(ref _bevelWidth, Mathf.Max(0.0f, value));
    }

    [Export(PropertyHint.Range, "0.001,0.1,0.001")]
    public float HighlightOffset { get; set; } = 0.012f;

    [ExportGroup("材质")]
    [Export]
    public Material? TopMaterial { get; set; }

    [Export]
    public Material? SideMaterial { get; set; }

    [ExportGroup("状态颜色")]
    [Export]
    public Color HoveredColor { get; set; } = new(0.22f, 0.78f, 0.95f, 0.42f);

    [Export]
    public Color CurrentColor { get; set; } = new(1.0f, 0.76f, 0.16f, 0.48f);

    [Export]
    public Color MovableColor { get; set; } = new(0.20f, 0.62f, 1.0f, 0.5f);

    [Export]
    public Color SelectedColor { get; set; } = new(1.0f, 0.72f, 0.20f, 0.56f);

    [Export]
    public Color DisabledColor { get; set; } = new(0.20f, 0.24f, 0.27f, 0.62f);

    [ExportGroup("输入")]
    [Export(PropertyHint.Layers3DPhysics)]
    public uint InputCollisionLayer
    {
        get => _inputCollisionLayer;
        set
        {
            _inputCollisionLayer = value;
            if (GodotObject.IsInstanceValid(_interactionArea))
            {
                _interactionArea.CollisionLayer = value;
            }
        }
    }

    [ExportGroup("格子名称")]
    [Export]
    public bool ShowCellNames
    {
        get => _showCellNames;
        set
        {
            _showCellNames = value;
            if (GodotObject.IsInstanceValid(_cellNameLabel))
            {
                _cellNameLabel.Visible = value;
            }
        }
    }

    [Export(PropertyHint.Range, "0.02,1.5,0.01")]
    public float CellNameHeight
    {
        get => _cellNameHeight;
        set
        {
            _cellNameHeight = Mathf.Max(0.02f, value);
            if (GodotObject.IsInstanceValid(_cellNameLabel))
            {
                _cellNameLabel.Position = new Vector3(0.0f, _cellNameHeight, 0.0f);
            }
        }
    }

    public HexCellData? Data { get; private set; }
    public HexCellVisualState VisualState { get; private set; } = HexCellVisualState.Normal;
    public Mesh? GeneratedMesh => _baseMesh?.Mesh;
    public Shape3D? InteractionShape => _collisionShape.Shape;
    public bool UsesCustomVisual => CustomVisualScene != null || CustomMesh != null;
    public Node3D TokenRoot => GetNode<Node3D>("TokenRoot");

    public override void _Ready()
    {
        _baseMesh = GetNodeOrNull<MeshInstance3D>("VisualRoot/BaseMesh");
        _customMeshInstance = GetNode<MeshInstance3D>("VisualRoot/CustomMeshInstance");
        _customVisualRoot = GetNode<Node3D>("VisualRoot/CustomVisualRoot");
        _highlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        _interactionArea = GetNode<Area3D>("InteractionArea");
        _collisionShape = GetNode<CollisionShape3D>("InteractionArea/CollisionShape3D");
        _cellNameLabel = GetNode<Label3D>("CellNameLabel");
        _baseCellNameFontSize = _cellNameLabel.FontSize;
        _cellNameLabel.Position = new Vector3(0.0f, CellNameHeight, 0.0f);

        _interactionArea.CollisionLayer = InputCollisionLayer;
        _interactionArea.InputRayPickable = true;
        _interactionArea.MouseEntered += OnMouseEntered;
        _interactionArea.MouseExited += OnMouseExited;
        _interactionArea.InputEvent += OnInputEvent;

        ApplyGeometry();
        RefreshVisualSource();
        BindData(InitialData ?? new HexCellData());
        _cellNameLabel.Visible = ShowCellNames;
        RefreshVisualState();
    }

    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(_interactionArea))
        {
            return;
        }

        _interactionArea.MouseEntered -= OnMouseEntered;
        _interactionArea.MouseExited -= OnMouseExited;
        _interactionArea.InputEvent -= OnInputEvent;
    }

    public void BindData(HexCellData data)
    {
        Data = data;
        InitialData = data;
        if (GodotObject.IsInstanceValid(_cellNameLabel))
        {
            ApplyCellName(data.DisplayName);
        }
    }

    public void ConfigureGeometry(float radius, float height, float bevelWidth)
    {
        _radius = Mathf.Max(0.1f, radius);
        _height = Mathf.Max(0.02f, height);
        _bevelWidth = Mathf.Max(0.0f, bevelWidth);
        if (IsNodeReady())
        {
            ApplyGeometry();
        }
    }

    public void RefreshVisualSource()
    {
        if (!IsNodeReady()
            || !GodotObject.IsInstanceValid(_customMeshInstance)
            || !GodotObject.IsInstanceValid(_customVisualRoot))
        {
            return;
        }

        ClearCustomVisualInstance();

        if (CustomVisualScene != null)
        {
            if (CustomMesh != null && !_warnedAboutMultipleVisualSources)
            {
                GD.PushWarning("HexCell3D 同时配置了 CustomMesh 和 CustomVisualScene，将优先使用 CustomVisualScene。");
                _warnedAboutMultipleVisualSources = true;
            }

            SetDefaultMeshVisible(false);
            _customMeshInstance.Visible = false;
            _customVisualInstance = CustomVisualScene.Instantiate();
            _customVisualRoot.AddChild(_customVisualInstance);
            return;
        }

        _warnedAboutMultipleVisualSources = false;
        if (CustomMesh != null)
        {
            SetDefaultMeshVisible(false);
            _customMeshInstance.Mesh = CustomMesh;
            _customMeshInstance.Visible = true;
            return;
        }

        _customMeshInstance.Mesh = null;
        _customMeshInstance.Visible = false;
        ApplyDefaultBaseMesh();
        SetDefaultMeshVisible(true);
    }

    public void ClearCustomVisual()
    {
        _customMesh = null;
        _customVisualScene = null;
        _warnedAboutMultipleVisualSources = false;
        RefreshVisualSourceIfReady();
    }

    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered)
        {
            return;
        }

        _hovered = hovered;
        RefreshVisualState();
        EmitSignal(SignalName.CellHoverChanged, Data?.CellId ?? 0L, hovered);
    }

    public void SetCurrent(bool value)
    {
        _isCurrent = value;
        RefreshVisualState();
    }

    public void SetMovable(bool value)
    {
        _isMovable = value;
        RefreshVisualState();
    }

    public void SetSelected(bool value)
    {
        _isSelected = value;
        RefreshVisualState();
    }

    public void SetDisabled(bool value)
    {
        _isDisabled = value;
        RefreshVisualState();
    }

    public void ClearStateFlags()
    {
        _hovered = false;
        _isCurrent = false;
        _isMovable = false;
        _isSelected = false;
        _isDisabled = false;
        RefreshVisualState();
    }

    public bool TryActivate()
    {
        if (_isDisabled || Data?.HasValidIdentity != true)
        {
            return false;
        }

        EmitSignal(SignalName.CellClicked, Data.CellId);
        return true;
    }

    public void BeginPointerClick()
    {
        _pendingPointerClick = !_isDisabled && Data?.HasValidIdentity == true;
    }

    public bool CompletePointerClick()
    {
        if (!_pendingPointerClick)
        {
            return false;
        }

        _pendingPointerClick = false;
        return TryActivate();
    }

    public void CancelPendingClick()
    {
        _pendingPointerClick = false;
    }

    private void ApplyGeometry()
    {
        if (!GodotObject.IsInstanceValid(_highlightMesh) || !GodotObject.IsInstanceValid(_collisionShape))
        {
            return;
        }

        var resources = HexCellMeshFactory.GetGeometry(Radius, Height, BevelWidth);
        if (!UsesCustomVisual)
        {
            ApplyDefaultBaseMesh(resources);
        }

        _highlightMesh.Mesh = resources.HighlightMesh;
        _highlightMesh.Position = new Vector3(0.0f, HighlightOffset, 0.0f);
        _collisionShape.Shape = resources.CollisionShape;
    }

    private void ApplyDefaultBaseMesh(HexCellGeometryResources? resources = null)
    {
        if (!GodotObject.IsInstanceValid(_baseMesh))
        {
            return;
        }

        resources ??= HexCellMeshFactory.GetGeometry(Radius, Height, BevelWidth);
        _baseMesh!.Mesh = resources.BaseMesh;
        _baseMesh.SetSurfaceOverrideMaterial(0, TopMaterial ?? HexCellMeshFactory.DefaultTopMaterial);
        _baseMesh.SetSurfaceOverrideMaterial(1, SideMaterial ?? HexCellMeshFactory.DefaultSideMaterial);
    }

    private void SetDefaultMeshVisible(bool visible)
    {
        if (GodotObject.IsInstanceValid(_baseMesh))
        {
            _baseMesh!.Visible = visible;
        }
    }

    private void ClearCustomVisualInstance()
    {
        if (!GodotObject.IsInstanceValid(_customVisualInstance))
        {
            _customVisualInstance = null;
            return;
        }

        if (_customVisualInstance!.GetParent() == _customVisualRoot)
        {
            _customVisualRoot.RemoveChild(_customVisualInstance);
        }

        _customVisualInstance.QueueFree();
        _customVisualInstance = null;
    }

    private void RefreshVisualSourceIfReady()
    {
        if (IsNodeReady())
        {
            RefreshVisualSource();
        }
    }

    private void RefreshVisualState()
    {
        var nextState = ResolveVisualState();
        var stateChanged = nextState != VisualState;
        VisualState = nextState;
        if (GodotObject.IsInstanceValid(_highlightMesh))
        {
            _highlightMesh.Visible = nextState != HexCellVisualState.Normal;
            if (_highlightMesh.Visible)
            {
                _highlightMesh.MaterialOverride = HexCellMeshFactory.GetHighlightMaterial(ColorFor(nextState));
            }
        }

        if (stateChanged)
        {
            EmitSignal(SignalName.VisualStateChanged, (int)nextState);
        }
    }

    private HexCellVisualState ResolveVisualState()
    {
        if (_isDisabled)
        {
            return HexCellVisualState.Disabled;
        }

        if (_isSelected)
        {
            return HexCellVisualState.Selected;
        }

        if (_isMovable)
        {
            return HexCellVisualState.Movable;
        }

        if (_hovered)
        {
            return HexCellVisualState.Hovered;
        }

        return _isCurrent ? HexCellVisualState.Current : HexCellVisualState.Normal;
    }

    private Color ColorFor(HexCellVisualState state)
    {
        return state switch
        {
            HexCellVisualState.Disabled => DisabledColor,
            HexCellVisualState.Selected => SelectedColor,
            HexCellVisualState.Movable => MovableColor,
            HexCellVisualState.Hovered => HoveredColor,
            HexCellVisualState.Current => CurrentColor,
            _ => Colors.Transparent
        };
    }

    private void OnMouseEntered()
    {
        SetHovered(true);
    }

    private void OnMouseExited()
    {
        SetHovered(false);
        CancelPendingClick();
    }

    private void OnInputEvent(Node camera, InputEvent inputEvent, Vector3 eventPosition, Vector3 normal, long shapeIndex)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            return;
        }

        if (mouseButton.Pressed)
        {
            BeginPointerClick();
        }
        else
        {
            CompletePointerClick();
        }
    }

    private void SetGeometryValue(ref float field, float value)
    {
        if (Mathf.IsEqualApprox(field, value))
        {
            return;
        }

        field = value;
        if (IsNodeReady())
        {
            ApplyGeometry();
        }
    }

    private void ApplyCellName(string displayName)
    {
        var textElements = SplitTextElements(displayName);
        if (textElements.Count <= 6)
        {
            _cellNameLabel.Text = string.Concat(textElements);
        }
        else
        {
            var visibleElements = textElements.Count > 12
                ? textElements.GetRange(0, 11)
                : textElements;
            if (textElements.Count > 12)
            {
                visibleElements.Add("…");
            }

            var firstLineCount = (visibleElements.Count + 1) / 2;
            _cellNameLabel.Text = string.Concat(visibleElements.GetRange(0, firstLineCount))
                + "\n"
                + string.Concat(visibleElements.GetRange(firstLineCount, visibleElements.Count - firstLineCount));
        }

        var longestLineLength = 0;
        foreach (var line in _cellNameLabel.Text.Split('\n'))
        {
            longestLineLength = Mathf.Max(longestLineLength, SplitTextElements(line).Count);
        }

        var scale = longestLineLength switch
        {
            <= 4 => 1.0f,
            5 => 0.9f,
            _ => 0.8f
        };
        _cellNameLabel.FontSize = Mathf.Max(16, Mathf.RoundToInt(_baseCellNameFontSize * scale));
    }

    private static List<string> SplitTextElements(string value)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return elements;
    }
}
