using Godot;
using EquestriaStar.State;

namespace EquestriaStar.UI;

public partial class CharacterCardView : Button
{
    private TextureRect _cardImage = null!;
    private Label _fallbackLabel = null!;
    private ShaderMaterial? _cardMaterial;

    public CharacterCardOption Option { get; private set; } = new();

    public override void _Ready()
    {
        _cardImage = GetNode<TextureRect>("CardImage");
        _fallbackLabel = GetNode<Label>("FallbackLabel");
        _cardMaterial = _cardImage.Material as ShaderMaterial;
    }

    public void Setup(CharacterCardOption option)
    {
        Option = option;
        _cardImage.Texture = GD.Load<Texture2D>(option.AssetPath);
        _fallbackLabel.Visible = option.UsesFallbackAsset;
        _fallbackLabel.Text = option.UsesFallbackAsset ? $"未知角色：{option.Code}\n暂用卡背" : "";
        TooltipText = option.IsRandom ? "等待服务端随机分配角色" : option.Name;
    }

    public void SetOccupied(bool occupiedByOther)
    {
        _cardMaterial?.SetShaderParameter("grayscale", occupiedByOther);
    }
}
