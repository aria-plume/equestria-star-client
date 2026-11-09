using Godot;

namespace EquestriaStar.Game.UI;

/// <summary>
/// 背包和装备区共用的单张道具卡，只负责显示及发出实例操作信号。
/// </summary>
public partial class InventoryItemCard : VBoxContainer
{
    [Signal]
    public delegate void ActionRequestedEventHandler(long playerItemId);

    private AspectRatioContainer _cardAspect = null!;
    private TextureRect _cardTexture = null!;
    private Label _nameLabel = null!;
    private Label _statusLabel = null!;
    private Control _actionArea = null!;
    private Button _actionButton = null!;

    public long PlayerItemId { get; private set; }
    public Button ActionButton => _actionButton;

    public override void _Ready()
    {
        _cardAspect = GetNode<AspectRatioContainer>("CardAspect");
        _cardTexture = GetNode<TextureRect>("CardAspect/CardTexture");
        _nameLabel = GetNode<Label>("ItemName");
        _statusLabel = GetNode<Label>("StatusLabel");
        _actionArea = GetNode<Control>("ActionArea");
        _actionButton = GetNode<Button>("ActionArea/ActionButton");
        _actionButton.Pressed += OnActionPressed;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actionButton))
        {
            _actionButton.Pressed -= OnActionPressed;
        }
    }

    public void Bind(
        CardViewerCard card,
        string actionText,
        CardActionAvailability availability,
        float cardWidth,
        float aspectRatio)
    {
        PlayerItemId = card.PlayerItemId ?? 0;
        _cardTexture.Texture = card.Texture;
        _nameLabel.Text = string.IsNullOrWhiteSpace(card.ItemName) ? $"道具 {card.ItemId}" : card.ItemName.Trim();
        _nameLabel.TooltipText = _nameLabel.Text;
        _actionButton.Text = actionText;
        _actionArea.Visible = true;
        _statusLabel.Visible = false;
        _actionButton.Disabled = PlayerItemId <= 0 || !availability.Enabled;
        _actionButton.TooltipText = availability.TooltipText;
        _cardAspect.Ratio = aspectRatio;
        _cardAspect.CustomMinimumSize = new Vector2(cardWidth, cardWidth / aspectRatio);
        CustomMinimumSize = new Vector2(cardWidth, cardWidth / aspectRatio + 92.0f);
    }

    public void SetReadOnlyStatus(string statusText)
    {
        _actionArea.Visible = false;
        _statusLabel.Text = statusText;
        _statusLabel.Visible = true;
        _actionButton.Disabled = true;
        CustomMinimumSize = new Vector2(
            _cardAspect.CustomMinimumSize.X,
            _cardAspect.CustomMinimumSize.Y + 92.0f);
    }

    public void UpdateAvailability(CardActionAvailability availability)
    {
        if (!_actionArea.Visible)
        {
            return;
        }
        _actionButton.Disabled = PlayerItemId <= 0 || !availability.Enabled;
        _actionButton.TooltipText = availability.TooltipText;
    }

    public void ResizeCard(float cardWidth, float aspectRatio)
    {
        _cardAspect.Ratio = aspectRatio;
        _cardAspect.CustomMinimumSize = new Vector2(cardWidth, cardWidth / aspectRatio);
        CustomMinimumSize = new Vector2(cardWidth, cardWidth / aspectRatio + 92.0f);
    }

    private void OnActionPressed()
    {
        if (_actionButton.Disabled || PlayerItemId <= 0)
        {
            return;
        }

        EmitSignal(SignalName.ActionRequested, PlayerItemId);
    }
}
