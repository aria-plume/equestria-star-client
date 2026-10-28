using System;
using System.Collections.Generic;
using EquestriaStar.Api;
using Godot;

namespace EquestriaStar.Game.Map;

public partial class PlayerToken3D : Node3D
{
    [Signal]
    public delegate void TokenClickedEventHandler(long playerId);

    private static readonly Color[] PlayerColors =
    [
        new Color("5aa9e6"),
        new Color("f28e8e"),
        new Color("7bc8a4"),
        new Color("d19ae3"),
        new Color("e7bd6f"),
        new Color("73cbd3")
    ];

    private static readonly Dictionary<int, StandardMaterial3D> MaterialsBySlot = new();
    private Node3D _liftRoot = null!;
    private MeshInstance3D _cylinderMesh = null!;
    private MeshInstance3D _currentPlayerIndicator = null!;
    private Area3D _interactionArea = null!;
    private CollisionShape3D _collisionShape = null!;
    private Label3D _nameLabel = null!;
    private bool _pendingPointerClick;
    private float _targetLiftY;

    [ExportGroup("尺寸")]
    [Export(PropertyHint.Range, "0.05,0.4,0.01")]
    public float TokenRadius { get; set; } = 0.13f;

    [Export(PropertyHint.Range, "0.1,1.0,0.01")]
    public float TokenHeight { get; set; } = 0.34f;

    [Export(PropertyHint.Range, "0.05,0.8,0.01")]
    public float LiftHeight { get; set; } = 0.22f;

    [Export(PropertyHint.Range, "1.0,30.0,0.5")]
    public float LiftSmoothSpeed { get; set; } = 14.0f;

    [ExportGroup("输入")]
    [Export(PropertyHint.Layers3DPhysics)]
    public uint InputCollisionLayer { get; set; } = 256;

    public long PlayerId { get; private set; }
    public long UserId { get; private set; }
    public int TurnOrder { get; private set; }
    public string Nickname { get; private set; } = "";
    public bool IsCurrentPlayer { get; private set; }
    public bool IsLifted { get; private set; }
    public int ColorSlot { get; private set; }
    public Node3D LiftRoot => _liftRoot;

    public override void _Ready()
    {
        _currentPlayerIndicator = GetNode<MeshInstance3D>("CurrentPlayerIndicator");
        _liftRoot = GetNode<Node3D>("LiftRoot");
        _cylinderMesh = GetNode<MeshInstance3D>("LiftRoot/VisualRoot/CylinderMesh");
        _interactionArea = GetNode<Area3D>("LiftRoot/InteractionArea");
        _collisionShape = GetNode<CollisionShape3D>("LiftRoot/InteractionArea/CollisionShape3D");
        _nameLabel = GetNode<Label3D>("LiftRoot/NameLabel");

        _interactionArea.InputEvent += OnInputEvent;
        ApplyDimensions();
        ApplyAppearance();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_liftRoot))
        {
            return;
        }

        var blend = 1.0f - Mathf.Exp(-Math.Max(1.0f, LiftSmoothSpeed) * (float)delta);
        var nextY = Mathf.Lerp(_liftRoot.Position.Y, _targetLiftY, blend);
        if (Mathf.Abs(nextY - _targetLiftY) < 0.0005f)
        {
            nextY = _targetLiftY;
        }

        _liftRoot.Position = new Vector3(0.0f, nextY, 0.0f);
    }

    public void BindPlayer(PlayerViewPlayerDto player, bool isCurrentPlayer, int colorSlot)
    {
        PlayerId = player.PlayerId;
        UserId = player.UserId;
        TurnOrder = player.TurnOrder;
        Nickname = player.Nickname;
        IsCurrentPlayer = isCurrentPlayer;
        ColorSlot = Mathf.PosMod(colorSlot, PlayerColors.Length);
        if (IsNodeReady())
        {
            ApplyAppearance();
        }
    }

    public void ConfigureDimensions(float radius, float height, float liftHeight)
    {
        TokenRadius = Math.Max(0.05f, radius);
        TokenHeight = Math.Max(0.1f, height);
        LiftHeight = Math.Max(0.05f, liftHeight);
        _targetLiftY = IsLifted ? LiftHeight : 0.0f;
        if (IsNodeReady())
        {
            ApplyDimensions();
        }
    }

    public void SetLifted(bool lifted, bool immediate = false)
    {
        IsLifted = lifted;
        _targetLiftY = lifted ? LiftHeight : 0.0f;
        if (immediate && GodotObject.IsInstanceValid(_liftRoot))
        {
            _liftRoot.Position = new Vector3(0.0f, _targetLiftY, 0.0f);
        }
    }

    public void CancelPendingClick()
    {
        _pendingPointerClick = false;
    }

    public bool BeginPointerClick()
    {
        _pendingPointerClick = IsCurrentPlayer && PlayerId > 0;
        return _pendingPointerClick;
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

    public bool TryActivate()
    {
        if (!IsCurrentPlayer || PlayerId <= 0)
        {
            return false;
        }

        EmitSignal(SignalName.TokenClicked, PlayerId);
        return true;
    }

    public static Color GetPlayerColor(int colorSlot)
    {
        return PlayerColors[Mathf.PosMod(colorSlot, PlayerColors.Length)];
    }

    private void ApplyDimensions()
    {
        if (_cylinderMesh.Mesh is CylinderMesh cylinder)
        {
            cylinder.TopRadius = TokenRadius;
            cylinder.BottomRadius = TokenRadius;
            cylinder.Height = TokenHeight;
        }

        _cylinderMesh.Position = new Vector3(0.0f, TokenHeight * 0.5f, 0.0f);
        _interactionArea.Position = new Vector3(0.0f, TokenHeight * 0.5f, 0.0f);
        if (_collisionShape.Shape is CylinderShape3D shape)
        {
            shape.Radius = TokenRadius;
            shape.Height = TokenHeight;
        }

        var indicatorScale = TokenRadius / 0.13f;
        _currentPlayerIndicator.Scale = new Vector3(indicatorScale, 1.0f, indicatorScale);
        _nameLabel.Position = new Vector3(0.0f, TokenHeight + 0.08f, 0.0f);
    }

    private void ApplyAppearance()
    {
        _cylinderMesh.MaterialOverride = GetMaterial(ColorSlot);
        _currentPlayerIndicator.Visible = IsCurrentPlayer;
        _interactionArea.CollisionLayer = InputCollisionLayer;
        _interactionArea.InputRayPickable = IsCurrentPlayer;
        _nameLabel.Text = Nickname;
        _nameLabel.Visible = false;
        if (!IsCurrentPlayer)
        {
            CancelPendingClick();
        }
    }

    private static Material GetMaterial(int colorSlot)
    {
        colorSlot = Mathf.PosMod(colorSlot, PlayerColors.Length);
        if (MaterialsBySlot.TryGetValue(colorSlot, out var cached))
        {
            return cached;
        }

        var color = PlayerColors[colorSlot];
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Metallic = 0.08f,
            Roughness = 0.58f
        };
        MaterialsBySlot[colorSlot] = material;
        return material;
    }

    private void OnInputEvent(Node camera, InputEvent inputEvent, Vector3 eventPosition, Vector3 normal, long shapeIndex)
    {
        if (!IsCurrentPlayer
            || inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            return;
        }

        if (mouseButton.Pressed)
        {
            BeginPointerClick();
            return;
        }

        CompletePointerClick();
    }
}
