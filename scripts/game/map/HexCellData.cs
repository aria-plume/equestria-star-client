using Godot;

namespace EquestriaStar.Game.Map;

[GlobalClass]
public partial class HexCellData : Resource
{
    [Export]
    public long CellId { get; set; }

    [Export]
    public string CellCode { get; set; } = "";

    [Export]
    public string BoardCode { get; set; } = "";

    [Export]
    public string CellName { get; set; } = "";

    [Export]
    public string CellNameText { get; set; } = "";

    [Export]
    public int Q { get; set; }

    [Export]
    public int R { get; set; }

    [Export]
    public Godot.Collections.Array<string> InteractionTags { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(CellNameText) ? CellName : CellNameText;
    public bool HasValidIdentity => CellId > 0 && !string.IsNullOrWhiteSpace(CellCode);

    public bool HasInteractionTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        foreach (var interactionTag in InteractionTags)
        {
            if (string.Equals(interactionTag, tag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
