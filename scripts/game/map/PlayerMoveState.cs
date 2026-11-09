namespace EquestriaStar.Game.Map;

public enum PlayerMoveState
{
    Idle,
    LoadingTargets,
    SelectingTarget,
    SubmittingMove
}

public enum PlayerMoveMode
{
    None,
    Normal,
    Railway
}
