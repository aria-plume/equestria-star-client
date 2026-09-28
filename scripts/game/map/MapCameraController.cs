using System;
using Godot;

namespace EquestriaStar.Game.Map;

public partial class MapCameraController : Node3D
{
    public const string MapInputBlockerGroup = "blocks_map_input";

    [Signal]
    public delegate void DragStartedEventHandler();

    [Signal]
    public delegate void DragEndedEventHandler();

    [ExportGroup("缩放")]
    [Export(PropertyHint.Range, "0.5,0.99,0.01")]
    public float ZoomFactor { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.1,0.8,0.01")]
    public float MinZoomRatio { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "1.0,2.0,0.01")]
    public float MaxZoomRatio { get; set; } = 1.35f;

    [Export(PropertyHint.Range, "1.0,30.0,0.5")]
    public float ZoomSmoothSpeed { get; set; } = 11.0f;

    [ExportGroup("拖动")]
    [Export(PropertyHint.Range, "1.0,20.0,1.0")]
    public float DragThreshold { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "0.0,0.4,0.01")]
    public float PanBoundaryPaddingRatio { get; set; } = 0.12f;

    private Camera3D _camera = null!;
    private Viewport _viewport = null!;
    private Aabb _mapBounds;
    private bool _hasMap;
    private bool _pointerDown;
    private bool _isDragging;
    private Vector2 _pressScreenPosition;
    private Vector3 _dragAnchorWorld;
    private Vector3 _targetRigPosition;
    private Vector2? _zoomAnchorScreen;
    private Vector3 _zoomAnchorWorld;
    private float _targetSize;
    private float _initialSize;
    private float _minimumSize;
    private float _maximumSize;
    private Vector3 _initialRigPosition;
    private Vector2 _panMinimum;
    private Vector2 _panMaximum;

    public float TargetSize => _targetSize;
    public float InitialSize => _initialSize;
    public float MinimumSize => _minimumSize;
    public float MaximumSize => _maximumSize;
    public bool IsDragging => _isDragging;
    public bool IsPointerDown => _pointerDown;
    public Vector2 PanMinimum => _panMinimum;
    public Vector2 PanMaximum => _panMaximum;
    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        _viewport = GetViewport();
        _viewport.SizeChanged += OnViewportSizeChanged;
        SetProcess(true);
        SetProcessInput(true);
    }

    public override void _ExitTree()
    {
        CancelPointerGesture();
        if (GodotObject.IsInstanceValid(_viewport))
        {
            _viewport.SizeChanged -= OnViewportSizeChanged;
        }
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMWindowFocusOut
            || what == (int)NotificationWMMouseExit
            || what == (int)NotificationVpMouseExit)
        {
            CancelPointerGesture();
        }
    }

    public override void _Process(double delta)
    {
        if (!_hasMap)
        {
            return;
        }

        if (_pointerDown && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            CancelPointerGesture();
        }

        var blend = 1.0f - Mathf.Exp(-ZoomSmoothSpeed * (float)delta);
        var nextSize = Mathf.Lerp(_camera.Size, _targetSize, blend);
        if (Mathf.Abs(nextSize - _targetSize) < 0.0005f)
        {
            nextSize = _targetSize;
        }

        if (!Mathf.IsEqualApprox(nextSize, _camera.Size))
        {
            _camera.Size = Math.Max(0.001f, nextSize);
            PreserveZoomAnchor();
            UpdatePanBounds(_camera.Size);
            ClampRigPosition();
        }

        if (Mathf.IsEqualApprox(_camera.Size, _targetSize))
        {
            _zoomAnchorScreen = null;
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!_hasMap)
        {
            return;
        }

        RouteInput(inputEvent, _viewport.GuiGetHoveredControl());
    }

    public static bool IsMapInputBlocked(Control? hoveredControl)
    {
        Node? current = hoveredControl;
        while (current != null)
        {
            if (current.IsInGroup(MapInputBlockerGroup))
            {
                return true;
            }

            current = current.GetParent();
        }

        return false;
    }

    internal void RouteInput(InputEvent inputEvent, Control? hoveredControl)
    {
        if (!_hasMap)
        {
            return;
        }

        if (IsMapInputBlocked(hoveredControl))
        {
            if (_pointerDown || _isDragging)
            {
                CancelPointerGesture();
            }

            return;
        }

        if (inputEvent is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
            {
                ApplyZoomStep(true, mouseButton.Position);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
            {
                ApplyZoomStep(false, mouseButton.Position);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    if (TryGetGroundIntersection(mouseButton.Position, out var groundPoint))
                    {
                        BeginPointerGesture(mouseButton.Position, groundPoint);
                    }
                }
                else
                {
                    EndPointerGesture();
                }
            }

            return;
        }

        if (inputEvent is InputEventMouseMotion motion
            && _pointerDown
            && TryGetGroundIntersection(motion.Position, out var motionGroundPoint))
        {
            UpdatePointerGesture(motion.Position, motionGroundPoint);
            if (_isDragging)
            {
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void InitializeMap(Aabb bounds)
    {
        _mapBounds = bounds;
        _hasMap = true;
        CancelPointerGesture();

        var viewportSize = _viewport.GetVisibleRect().Size;
        var aspect = viewportSize.Y > 0.0f ? viewportSize.X / viewportSize.Y : 16.0f / 9.0f;
        var plan = MapCameraFitter.Calculate(bounds, aspect);
        Position = new Vector3(plan.Target.X, 0.0f, plan.Target.Z);
        _camera.Position = plan.CameraPosition - Position;
        _camera.LookAt(plan.Target, Vector3.Up);
        _camera.Size = Math.Max(0.001f, plan.OrthographicSize);

        _initialRigPosition = Position;
        _targetRigPosition = Position;
        _initialSize = _camera.Size;
        RecalculateZoomLimits(aspect);
        _targetSize = Mathf.Clamp(_initialSize, _minimumSize, _maximumSize);
        _zoomAnchorScreen = null;
        UpdatePanBounds(_camera.Size);
        ClampRigPosition();
    }

    public void ResetToInitialView()
    {
        if (!_hasMap)
        {
            return;
        }

        CancelPointerGesture();
        Position = _initialRigPosition;
        _targetRigPosition = _initialRigPosition;
        _camera.Size = Math.Max(0.001f, _initialSize);
        _targetSize = _camera.Size;
        _zoomAnchorScreen = null;
        UpdatePanBounds(_camera.Size);
        ClampRigPosition();
    }

    public void ApplyZoomStep(bool zoomIn, Vector2? screenPosition = null)
    {
        if (!_hasMap)
        {
            return;
        }

        if (screenPosition.HasValue && TryGetGroundIntersection(screenPosition.Value, out var anchor))
        {
            _zoomAnchorScreen = screenPosition;
            _zoomAnchorWorld = anchor;
        }
        else
        {
            _zoomAnchorScreen = null;
        }

        var safeFactor = Mathf.Clamp(ZoomFactor, 0.1f, 0.99f);
        var nextTarget = zoomIn ? _targetSize * safeFactor : _targetSize / safeFactor;
        _targetSize = Mathf.Clamp(nextTarget, _minimumSize, _maximumSize);
        UpdatePanBounds(_targetSize);
        ClampRigPosition();
    }

    public void BeginPointerGesture(Vector2 screenPosition, Vector3 groundPoint)
    {
        _zoomAnchorScreen = null;
        _pointerDown = true;
        _isDragging = false;
        _pressScreenPosition = screenPosition;
        _dragAnchorWorld = groundPoint;
    }

    public void UpdatePointerGesture(Vector2 screenPosition, Vector3 groundPoint)
    {
        if (!_pointerDown)
        {
            return;
        }

        if (!_isDragging && screenPosition.DistanceTo(_pressScreenPosition) > Math.Max(1.0f, DragThreshold))
        {
            _isDragging = true;
            EmitSignal(SignalName.DragStarted);
            Input.SetDefaultCursorShape(Input.CursorShape.Drag);
        }

        if (!_isDragging)
        {
            return;
        }

        var difference = _dragAnchorWorld - groundPoint;
        _targetRigPosition += new Vector3(difference.X, 0.0f, difference.Z);
        Position = new Vector3(_targetRigPosition.X, Position.Y, _targetRigPosition.Z);
        ClampRigPosition();
    }

    public void EndPointerGesture()
    {
        if (!_pointerDown)
        {
            return;
        }

        var wasDragging = _isDragging;
        _pointerDown = false;
        _isDragging = false;
        _pressScreenPosition = Vector2.Zero;
        _dragAnchorWorld = Vector3.Zero;
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        if (wasDragging)
        {
            EmitSignal(SignalName.DragEnded);
        }
    }

    public void CancelPointerGesture()
    {
        var wasDragging = _isDragging;
        _pointerDown = false;
        _isDragging = false;
        _pressScreenPosition = Vector2.Zero;
        _dragAnchorWorld = Vector3.Zero;
        _zoomAnchorScreen = null;
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        if (wasDragging)
        {
            EmitSignal(SignalName.DragEnded);
        }
    }

    public void MoveTo(Vector3 requestedPosition)
    {
        if (!_hasMap)
        {
            return;
        }

        _targetRigPosition = new Vector3(requestedPosition.X, _targetRigPosition.Y, requestedPosition.Z);
        Position = new Vector3(_targetRigPosition.X, Position.Y, _targetRigPosition.Z);
        ClampRigPosition();
    }

    private void PreserveZoomAnchor()
    {
        if (!_zoomAnchorScreen.HasValue
            || !TryGetGroundIntersection(_zoomAnchorScreen.Value, out var currentAnchor))
        {
            return;
        }

        var correction = _zoomAnchorWorld - currentAnchor;
        _targetRigPosition += new Vector3(correction.X, 0.0f, correction.Z);
        Position = new Vector3(_targetRigPosition.X, Position.Y, _targetRigPosition.Z);
    }

    private void RecalculateZoomLimits(float aspect)
    {
        var fitPlan = MapCameraFitter.Calculate(_mapBounds, aspect);
        _minimumSize = Math.Max(0.001f, fitPlan.OrthographicSize * Mathf.Clamp(MinZoomRatio, 0.05f, 0.95f));
        _maximumSize = Math.Max(_minimumSize, fitPlan.OrthographicSize * Math.Max(1.0f, MaxZoomRatio));
    }

    private void UpdatePanBounds(float size)
    {
        if (!_hasMap)
        {
            return;
        }

        var center = _mapBounds.GetCenter();
        if (!TryGetGroundFootprint(size, out var footprintSize))
        {
            _panMinimum = new Vector2(_mapBounds.Position.X, _mapBounds.Position.Z);
            _panMaximum = new Vector2(_mapBounds.End.X, _mapBounds.End.Z);
            return;
        }

        var mapWidth = _mapBounds.Size.X;
        var mapDepth = _mapBounds.Size.Z;
        var paddingRatio = Mathf.Clamp(PanBoundaryPaddingRatio, 0.0f, 0.45f);
        if (footprintSize.X >= mapWidth)
        {
            _panMinimum.X = center.X;
            _panMaximum.X = center.X;
        }
        else
        {
            var extra = Math.Min(mapWidth * paddingRatio, footprintSize.X * 0.35f);
            _panMinimum.X = _mapBounds.Position.X - extra;
            _panMaximum.X = _mapBounds.End.X + extra;
        }

        if (footprintSize.Y >= mapDepth)
        {
            _panMinimum.Y = center.Z;
            _panMaximum.Y = center.Z;
        }
        else
        {
            var extra = Math.Min(mapDepth * paddingRatio, footprintSize.Y * 0.35f);
            _panMinimum.Y = _mapBounds.Position.Z - extra;
            _panMaximum.Y = _mapBounds.End.Z + extra;
        }
    }

    private bool TryGetGroundFootprint(float size, out Vector2 footprintSize)
    {
        footprintSize = Vector2.Zero;
        var viewportSize = _viewport.GetVisibleRect().Size;
        if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f || _camera.Size <= 0.0f)
        {
            return false;
        }

        var points = new[]
        {
            Vector2.Zero,
            new Vector2(viewportSize.X, 0.0f),
            new Vector2(0.0f, viewportSize.Y),
            viewportSize
        };
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minZ = float.MaxValue;
        var maxZ = float.MinValue;
        foreach (var point in points)
        {
            if (!TryGetGroundIntersection(point, out var worldPoint))
            {
                return false;
            }

            var relative = worldPoint - GlobalPosition;
            minX = Math.Min(minX, relative.X);
            maxX = Math.Max(maxX, relative.X);
            minZ = Math.Min(minZ, relative.Z);
            maxZ = Math.Max(maxZ, relative.Z);
        }

        var scale = size / _camera.Size;
        footprintSize = new Vector2((maxX - minX) * scale, (maxZ - minZ) * scale);
        return true;
    }

    private bool TryGetGroundIntersection(Vector2 screenPosition, out Vector3 point)
    {
        point = Vector3.Zero;
        if (!GodotObject.IsInstanceValid(_camera))
        {
            return false;
        }

        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        if (Mathf.Abs(direction.Y) < 0.00001f)
        {
            return false;
        }

        var distance = -origin.Y / direction.Y;
        if (distance < 0.0f)
        {
            return false;
        }

        point = origin + direction * distance;
        return true;
    }

    private void ClampRigPosition()
    {
        var clampedX = Mathf.Clamp(_targetRigPosition.X, _panMinimum.X, _panMaximum.X);
        var clampedZ = Mathf.Clamp(_targetRigPosition.Z, _panMinimum.Y, _panMaximum.Y);
        _targetRigPosition = new Vector3(clampedX, _targetRigPosition.Y, clampedZ);
        Position = new Vector3(clampedX, Position.Y, clampedZ);
    }

    private void OnViewportSizeChanged()
    {
        if (!_hasMap)
        {
            return;
        }

        var viewportSize = _viewport.GetVisibleRect().Size;
        var aspect = viewportSize.Y > 0.0f ? viewportSize.X / viewportSize.Y : 16.0f / 9.0f;
        RecalculateZoomLimits(aspect);
        _targetSize = Mathf.Clamp(_targetSize, _minimumSize, _maximumSize);
        _camera.Size = Mathf.Clamp(_camera.Size, _minimumSize, _maximumSize);
        UpdatePanBounds(_targetSize);
        ClampRigPosition();
    }
}
