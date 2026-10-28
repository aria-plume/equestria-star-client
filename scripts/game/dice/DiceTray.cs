using Godot;

namespace EquestriaStar.Game.Dice;

[Tool]
public partial class DiceTray : StaticBody3D
{
    [Export(PropertyHint.Range, "3.0,14.0,0.1")]
    public float TraySize { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "0.05,0.6,0.01")]
    public float FloorThickness { get; set; } = 0.18f;

    [Export(PropertyHint.Range, "0.05,0.6,0.01")]
    public float WallThickness { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0.2,2.0,0.05")]
    public float WallHeight { get; set; } = 0.85f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float Friction { get; set; } = 0.78f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float Bounce { get; set; } = 0.24f;

    public float InnerHalfExtent => TraySize * 0.5f;

    public override void _Ready()
    {
        RefreshGeometry();
    }

    public void RefreshGeometry()
    {
        var half = TraySize * 0.5f;
        ConfigureBox("VisualRoot/FloorMesh", "FloorCollision", new Vector3(TraySize, FloorThickness, TraySize),
            new Vector3(0.0f, -FloorThickness * 0.5f, 0.0f));
        ConfigureBox("VisualRoot/WallNorthMesh", "WallNorthCollision",
            new Vector3(TraySize + WallThickness * 2.0f, WallHeight, WallThickness),
            new Vector3(0.0f, WallHeight * 0.5f, -half - WallThickness * 0.5f));
        ConfigureBox("VisualRoot/WallSouthMesh", "WallSouthCollision",
            new Vector3(TraySize + WallThickness * 2.0f, WallHeight, WallThickness),
            new Vector3(0.0f, WallHeight * 0.5f, half + WallThickness * 0.5f));
        ConfigureBox("VisualRoot/WallWestMesh", "WallWestCollision",
            new Vector3(WallThickness, WallHeight, TraySize),
            new Vector3(-half - WallThickness * 0.5f, WallHeight * 0.5f, 0.0f));
        ConfigureBox("VisualRoot/WallEastMesh", "WallEastCollision",
            new Vector3(WallThickness, WallHeight, TraySize),
            new Vector3(half + WallThickness * 0.5f, WallHeight * 0.5f, 0.0f));

        PhysicsMaterialOverride ??= new PhysicsMaterial();
        PhysicsMaterialOverride.Friction = Friction;
        PhysicsMaterialOverride.Bounce = Bounce;
    }

    private void ConfigureBox(string meshPath, string collisionPath, Vector3 size, Vector3 position)
    {
        var meshInstance = GetNode<MeshInstance3D>(meshPath);
        var collision = GetNode<CollisionShape3D>(collisionPath);
        if (meshInstance.Mesh is BoxMesh boxMesh)
        {
            boxMesh.Size = size;
        }

        if (collision.Shape is BoxShape3D boxShape)
        {
            boxShape.Size = size;
        }

        meshInstance.Position = position;
        collision.Position = position;
    }
}
