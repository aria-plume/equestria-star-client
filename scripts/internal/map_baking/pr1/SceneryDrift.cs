using Godot;
using System.Linq;

namespace EquestriaStar.Internal.MapBaking.Pr1;

/// <summary>Subtle reversible scenery motion; never changes board or gameplay transforms.</summary>
public partial class SceneryDrift : Node
{
    public float Amplitude {get;set;}
    public float Speed {get;set;}
    public float Phase {get;set;}
    public float BoardDiameter {get;set;}=1.1f;
    private Node3D _target=null!;
    private Node3D _anchor=null!;
    private Vector3 _origin;
    private Vector3 _rotation;
    public Node3D Anchor=>_anchor;
    public Node3D VisualPivot=>_target;
    public bool IsBalloon=>_anchor.Name.ToString().Contains("Balloon");
    public long Updates {get;private set;}
    public ulong LastFrame {get;private set;}
    public object Diagnose(Camera3D camera)
    {
        var meshes=_target.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null && m.IsVisibleInTree()).ToArray();
        var box=meshes.Length>0?meshes[0].GlobalTransform*meshes[0].GetAabb():new Aabb(_target.GlobalPosition,Vector3.Zero);
        foreach(var m in meshes.Skip(1))box=box.Merge(m.GlobalTransform*m.GetAabb());
        var centre=box.GetCenter();var screen=camera.UnprojectPosition(centre);
        return new {name=_anchor.Name.ToString(),anchorPath=_anchor.GetPath().ToString(),anchorId=_anchor.GetInstanceId(),pivotPath=_target.GetPath().ToString(),pivotId=_target.GetInstanceId(),viewport=_anchor.GetViewport().GetPath().ToString(),visibleMeshes=meshes.Length,visible=_target.IsVisibleInTree(),inViewport=new Rect2(Vector2.Zero,camera.GetViewport().GetVisibleRect().Size).HasPoint(screen),anchor=new[]{_anchor.GlobalPosition.X,_anchor.GlobalPosition.Y,_anchor.GlobalPosition.Z},world=new[]{centre.X,centre.Y,centre.Z},screen=new[]{screen.X,screen.Y},Updates,LastFrame};
    }
    private float _time;
    public override void _Ready()
    {
        _anchor=(Node3D)GetParent();
        var visual=_anchor.GetChildren().OfType<Node3D>().ToArray();
        _target=new Node3D {Name="MotionPivot"};_anchor.AddChild(_target);
        foreach(var part in visual)part.Reparent(_target,false);
        _origin=_target.Position;_rotation=_target.Rotation;
    }
    public override void _Process(double delta)
    {
        _time+=(float)delta;ApplyAt(_time,false,1);
    }
    public void ApplyAt(double time,bool fairy,float intensity)
    {
        if(!GodotObject.IsInstanceValid(_target))return;
        Updates++;LastFrame=Engine.GetProcessFrames();
        var world=Vector3.Zero;var rotation=Vector3.Zero;
        if(!fairy)
            world=new Vector3(Mathf.Sin((float)time*Speed*.6f+Phase)*Amplitude*.35f,Mathf.Sin((float)time*Speed+Phase)*Amplitude,0);
        else if(!EquestriaStar.Internal.MapBaking.Pr1.PaperWorldBackground.Enabled)
        {
            var a=IsBalloon?BoardDiameter*.0225f:Amplitude;var t=(float)time;
            world=new Vector3(Mathf.Sin(t*(IsBalloon?.48f:.22f)+Phase)*a*(IsBalloon?.25f:1.8f),Mathf.Sin(t*(IsBalloon?.83f:.32f)+Phase)*a*(IsBalloon?1:.25f),0);
        }
        else
        {
            var a=(float)time*Mathf.Tau/(IsBalloon?6.5f:38f)+Phase;
            world=IsBalloon?new Vector3(Mathf.Sin(a*.43f)*BoardDiameter*.012f,Mathf.Sin(a)*BoardDiameter*.045f,Mathf.Sin(a*.31f)*BoardDiameter*.008f):
                new Vector3(Mathf.Sin(a)*BoardDiameter*.20f,Mathf.Sin(a*.77f)*BoardDiameter*.012f,Mathf.Cos(a*.61f)*BoardDiameter*.035f);
            if(IsBalloon)rotation=new Vector3(0,0,Mathf.Sin(a*.71f)*Mathf.DegToRad(1.6f));
        }
        // Amplitudes are world-sized: source-asset scale/yaw must not shrink or redirect the bob.
        _target.Position=_origin+_anchor.GlobalBasis.Inverse()*world*intensity;
        _target.Rotation=_rotation+rotation*intensity;
    }
    public override void _ExitTree()
    {
        if(GodotObject.IsInstanceValid(_target)){_target.Position=_origin;_target.Rotation=_rotation;}
    }
}
