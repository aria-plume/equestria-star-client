using Godot;

namespace EquestriaStar.Game.Dice;

public enum DiceThrowTier
{
    None,
    Light,
    Normal,
    Heavy
}

public readonly record struct DiceThrowProfile(
    float HorizontalMin,
    float HorizontalMax,
    float UpwardMin,
    float UpwardMax,
    float AngularMin,
    float AngularMax);

public readonly record struct DiceThrowSolution(
    Vector3 Impulse,
    Vector3 AngularVelocity,
    DiceThrowTier Tier,
    float PixelsPerSecond,
    double DurationSeconds);

public partial class DiceRollController : Node3D
{
    private const string MapInputBlockerGroup = "blocks_map_input";

    [Export]
    public NodePath DicePath { get; set; } = new("../Dice3D");

    [Export]
    public NodePath CameraPath { get; set; } = new("Camera3D");

    [ExportGroup("固定观察镜头")]
    [Export]
    public Vector3 TrayCameraPosition { get; set; } = new(0.0f, 4.35f, 5.15f);

    [Export]
    public Vector3 TrayLookTarget { get; set; } = new(0.0f, 0.35f, 0.0f);

    [Export(PropertyHint.Range, "0.05,1.5,0.01")]
    public float CameraTransitionDuration { get; set; } = 0.32f;

    [Export(PropertyHint.Range, "20.0,100.0,0.5")]
    public float TrayCameraFov { get; set; } = 43.0f;

    private Dice3D _dice = null!;
    private Camera3D _camera = null!;
    private Tween? _cameraTween;
    private bool _dragging;
    private Vector2 _dragStart;
    private double _dragStartSeconds;
    private bool _hasSavedCameraState;
    private Transform3D _savedCameraTransform;
    private Camera3D.ProjectionType _savedProjection;
    private Camera3D.KeepAspectEnum _savedKeepAspect;
    private float _savedFov;
    private float _savedSize;
    private float _savedNear;
    private float _savedFar;

    public bool IsDragging => _dragging;
    public bool HasSavedCameraState => _hasSavedCameraState;
    public bool IsCameraTransitionActive => GodotObject.IsInstanceValid(_cameraTween) && _cameraTween!.IsRunning();
    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _dice = GetNode<Dice3D>(DicePath);
        _camera = GetNode<Camera3D>(CameraPath);
        _dice.RollStateChanged += OnRollStateChanged;
    }

    public override void _ExitTree()
    {
        CancelDrag();
        RestoreCamera(true);
        if (GodotObject.IsInstanceValid(_dice))
        {
            _dice.RollStateChanged -= OnRollStateChanged;
        }
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMWindowFocusOut
            || what == (int)NotificationWMMouseExit
            || what == (int)NotificationVpMouseExit)
        {
            CancelDrag();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (IsPointerOverInputBlocker())
        {
            CancelDrag();
            return;
        }

        if (inputEvent is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouseButton.Pressed)
        {
            BeginDrag(mouseButton.Position);
        }
        else
        {
            EndDrag(mouseButton.Position);
        }
    }

    public void BeginDrag(Vector2 screenPosition, double? timestampSeconds = null)
    {
        if (_dice.RollState != DiceRollState.AwaitingDrag || _dragging)
        {
            return;
        }

        _dragging = true;
        _dragStart = screenPosition;
        _dragStartSeconds = timestampSeconds ?? CurrentTimeSeconds();
    }

    public bool EndDrag(Vector2 screenPosition, double? timestampSeconds = null)
    {
        if (!_dragging)
        {
            return false;
        }

        _dragging = false;
        var endSeconds = timestampSeconds ?? CurrentTimeSeconds();
        var duration = Mathf.Max(0.001, endSeconds - _dragStartSeconds);
        return _dice.TryThrow(screenPosition - _dragStart, duration, _camera);
    }

    public void CancelDrag()
    {
        _dragging = false;
        _dragStart = Vector2.Zero;
        _dragStartSeconds = 0.0;
    }

    public void RestoreCamera(bool immediate = false)
    {
        if (!_hasSavedCameraState || !GodotObject.IsInstanceValid(_camera))
        {
            return;
        }

        KillCameraTween();
        ApplySavedProjection();
        if (immediate || !IsInsideTree())
        {
            _camera.GlobalTransform = _savedCameraTransform;
            _hasSavedCameraState = false;
            return;
        }

        _cameraTween = CreateTween();
        _cameraTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _cameraTween.TweenProperty(_camera, "global_transform", _savedCameraTransform, CameraTransitionDuration);
        _cameraTween.Finished += CompleteCameraRestore;
    }

    public static bool TryCalculateThrow(
        Vector2 screenDrag,
        double durationSeconds,
        Basis cameraBasis,
        float minimumDragDistance,
        float lightMaximumPixelsPerSecond,
        float heavyMinimumPixelsPerSecond,
        DiceThrowProfile lightProfile,
        DiceThrowProfile normalProfile,
        DiceThrowProfile heavyProfile,
        float maximumLinearImpulse,
        float maximumAngularSpeed,
        float axisJitter,
        RandomNumberGenerator random,
        out DiceThrowSolution solution)
    {
        solution = default;
        var dragLength = screenDrag.Length();
        if (dragLength < minimumDragDistance || durationSeconds <= 0.0)
        {
            return false;
        }

        var pixelsPerSecond = dragLength / (float)Mathf.Max(durationSeconds, 0.001);
        var tier = pixelsPerSecond <= lightMaximumPixelsPerSecond
            ? DiceThrowTier.Light
            : pixelsPerSecond < heavyMinimumPixelsPerSecond
                ? DiceThrowTier.Normal
                : DiceThrowTier.Heavy;
        var profile = tier switch
        {
            DiceThrowTier.Light => lightProfile,
            DiceThrowTier.Normal => normalProfile,
            _ => heavyProfile
        };

        var cameraRight = cameraBasis.X with { Y = 0.0f };
        var cameraForward = -cameraBasis.Z with { Y = 0.0f };
        cameraRight = cameraRight.LengthSquared() > 0.0001f ? cameraRight.Normalized() : Vector3.Right;
        cameraForward = cameraForward.LengthSquared() > 0.0001f ? cameraForward.Normalized() : Vector3.Forward;
        var dragDirection = screenDrag.Normalized();
        var worldDirection = (cameraRight * dragDirection.X - cameraForward * dragDirection.Y).Normalized();

        var horizontal = RandomRange(random, profile.HorizontalMin, profile.HorizontalMax);
        var upward = Mathf.Max(0.05f, RandomRange(random, profile.UpwardMin, profile.UpwardMax));
        var impulse = worldDirection * horizontal + Vector3.Up * upward;
        if (impulse.Length() > maximumLinearImpulse)
        {
            impulse = impulse.Normalized() * maximumLinearImpulse;
        }

        var spinAxis = worldDirection.Cross(Vector3.Up).Normalized();
        var jitter = new Vector3(
            random.RandfRange(-axisJitter, axisJitter),
            random.RandfRange(-axisJitter * 0.5f, axisJitter * 0.5f),
            random.RandfRange(-axisJitter, axisJitter));
        spinAxis = (spinAxis + jitter).Normalized();
        var angularMagnitude = Mathf.Min(
            RandomRange(random, profile.AngularMin, profile.AngularMax),
            maximumAngularSpeed);
        var angularVelocity = spinAxis * angularMagnitude;
        solution = new DiceThrowSolution(
            impulse,
            angularVelocity,
            tier,
            pixelsPerSecond,
            durationSeconds);
        return true;
    }

    private void OnRollStateChanged(int stateValue)
    {
        var state = (DiceRollState)stateValue;
        if (state == DiceRollState.AwaitingDrag)
        {
            EnterFixedTrayView();
        }
        else if (state is DiceRollState.Completed or DiceRollState.Idle)
        {
            CancelDrag();
            RestoreCamera();
        }
    }

    private void EnterFixedTrayView()
    {
        if (!_hasSavedCameraState)
        {
            _savedCameraTransform = _camera.GlobalTransform;
            _savedProjection = _camera.Projection;
            _savedKeepAspect = _camera.KeepAspect;
            _savedFov = _camera.Fov;
            _savedSize = _camera.Size;
            _savedNear = _camera.Near;
            _savedFar = _camera.Far;
            _hasSavedCameraState = true;
        }

        KillCameraTween();
        _camera.Projection = Camera3D.ProjectionType.Perspective;
        _camera.Fov = TrayCameraFov;
        var position = ToGlobal(TrayCameraPosition);
        var target = ToGlobal(TrayLookTarget);
        var targetTransform = new Transform3D(Basis.Identity, position).LookingAt(target, Vector3.Up);
        _cameraTween = CreateTween();
        _cameraTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _cameraTween.TweenProperty(_camera, "global_transform", targetTransform, CameraTransitionDuration);
    }

    private void ApplySavedProjection()
    {
        _camera.Projection = _savedProjection;
        _camera.KeepAspect = _savedKeepAspect;
        _camera.Fov = _savedFov;
        _camera.Size = _savedSize;
        _camera.Near = _savedNear;
        _camera.Far = _savedFar;
    }

    private void CompleteCameraRestore()
    {
        _hasSavedCameraState = false;
        _cameraTween = null;
    }

    private bool IsPointerOverInputBlocker()
    {
        Node? current = GetViewport().GuiGetHoveredControl();
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

    private void KillCameraTween()
    {
        if (GodotObject.IsInstanceValid(_cameraTween))
        {
            _cameraTween!.Kill();
        }

        _cameraTween = null;
    }

    private static float RandomRange(RandomNumberGenerator random, float minimum, float maximum)
    {
        var low = Mathf.Min(minimum, maximum);
        var high = Mathf.Max(minimum, maximum);
        return random.RandfRange(low, high);
    }

    private static double CurrentTimeSeconds()
    {
        return Time.GetTicksUsec() / 1_000_000.0;
    }
}
