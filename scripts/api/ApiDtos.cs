using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace EquestriaStar.Api;

public sealed class ApiEnvelope<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public sealed class ApiResult<T>
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }
    public int HttpStatus { get; init; }
    public bool IsAuthError { get; init; }

    public static ApiResult<T> Success(T? data, string? message, int httpStatus)
    {
        return new ApiResult<T>
        {
            Ok = true,
            Data = data,
            Message = message ?? "",
            HttpStatus = httpStatus
        };
    }

    public static ApiResult<T> Failure(string message, string? errorCode = null, int httpStatus = 0, T? data = default, bool isAuthError = false)
    {
        return new ApiResult<T>
        {
            Ok = false,
            Message = string.IsNullOrWhiteSpace(message) ? "操作失败。" : message,
            ErrorCode = errorCode,
            HttpStatus = httpStatus,
            Data = data,
            IsAuthError = isAuthError
        };
    }
}

public sealed class UserDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
}

public sealed class LoginResponseDto
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("user")]
    public UserDto? User { get; set; }
}

public sealed class RegisterResponseDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";
}

public sealed class ActiveSessionResponseDto
{
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("roomId")]
    public long? RoomId { get; set; }

    [JsonPropertyName("gameId")]
    public long? GameId { get; set; }

    [JsonPropertyName("memberRole")]
    public string? MemberRole { get; set; }

    [JsonPropertyName("readyStatus")]
    public string? ReadyStatus { get; set; }

    [JsonPropertyName("selectedCharacterId")]
    public long? SelectedCharacterId { get; set; }

    [JsonPropertyName("characterSelectionDeadline")]
    public string? CharacterSelectionDeadline { get; set; }
}

public sealed class CharacterDto
{
    [JsonPropertyName("characterId")]
    public long CharacterId { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("characterType")]
    public string CharacterType { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("attrStrength")]
    public int AttrStrength { get; set; }

    [JsonPropertyName("attrSpeed")]
    public int AttrSpeed { get; set; }

    [JsonPropertyName("attrSocial")]
    public int AttrSocial { get; set; }

    [JsonPropertyName("attrCharm")]
    public int AttrCharm { get; set; }

    [JsonPropertyName("attrMagic")]
    public int AttrMagic { get; set; }

    [JsonPropertyName("attrMind")]
    public int AttrMind { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("skillName")]
    public string? SkillName { get; set; }

    [JsonPropertyName("skillText")]
    public string? SkillText { get; set; }

    [JsonPropertyName("skillEffectId")]
    public string? SkillEffectId { get; set; }
}

public sealed class RoomDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("roomName")]
    public string RoomName { get; set; } = "";

    [JsonPropertyName("ownerUserId")]
    public long? OwnerUserId { get; set; }

    [JsonPropertyName("ownerNickname")]
    public string? OwnerNickname { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("statusText")]
    public string? StatusText { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("currentPlayers")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
}

public sealed class RoomPageDto
{
    [JsonPropertyName("records")]
    public List<RoomDto>? Records { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public sealed class JoinRoomResponseDto
{
    [JsonPropertyName("playerId")]
    public long PlayerId { get; set; }

    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    [JsonPropertyName("selectedCharacterId")]
    public long? SelectedCharacterId { get; set; }
}

public sealed class RoomDetailDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("roomName")]
    public string RoomName { get; set; } = "";

    [JsonPropertyName("ownerUserId")]
    public long OwnerUserId { get; set; }

    [JsonPropertyName("ownerNickname")]
    public string OwnerNickname { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("currentPlayers")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("currentGameId")]
    public long? CurrentGameId { get; set; }

    [JsonPropertyName("characterSelectionDeadline")]
    public string? CharacterSelectionDeadline { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("members")]
    public List<RoomMemberDto>? Members { get; set; }
}

public sealed class RoomMemberDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("seatNo")]
    public int SeatNo { get; set; }

    [JsonPropertyName("memberRole")]
    public string MemberRole { get; set; } = "";

    [JsonPropertyName("readyStatus")]
    public string ReadyStatus { get; set; } = "";

    [JsonPropertyName("selectedCharacterId")]
    public long? SelectedCharacterId { get; set; }

    [JsonPropertyName("selectedCharacterCode")]
    public string? SelectedCharacterCode { get; set; }

    [JsonPropertyName("selectedCharacterName")]
    public string? SelectedCharacterName { get; set; }

    [JsonPropertyName("selectedCharacterTitle")]
    public string? SelectedCharacterTitle { get; set; }

    [JsonPropertyName("selectedCharacterSkillName")]
    public string? SelectedCharacterSkillName { get; set; }

    [JsonPropertyName("selectedCharacterSkillText")]
    public string? SelectedCharacterSkillText { get; set; }

    [JsonPropertyName("selectedCharacterSkillEffectId")]
    public string? SelectedCharacterSkillEffectId { get; set; }

    [JsonPropertyName("joinedAt")]
    public string? JoinedAt { get; set; }
}

public sealed class ReadyRoomResponseDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }
}

public sealed class LeaveRoomResponseDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("roomDeleted")]
    public bool RoomDeleted { get; set; }
}

public sealed class StartCharacterSelectionResponseDto
{
    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("characterSelectionDeadline")]
    public string? CharacterSelectionDeadline { get; set; }
}

public sealed class GameMapDto
{
    [JsonPropertyName("mapId")]
    public long MapId { get; set; }

    [JsonPropertyName("mapName")]
    public string MapName { get; set; } = "";

    [JsonPropertyName("cells")]
    public List<MapCellDto>? Cells { get; set; }

    [JsonPropertyName("edges")]
    public List<MapEdgeDto>? Edges { get; set; }
}

public sealed class MapCellDto
{
    [JsonPropertyName("cellId")]
    public long CellId { get; set; }

    [JsonPropertyName("cellCode")]
    public string CellCode { get; set; } = "";

    [JsonPropertyName("boardCode")]
    public string BoardCode { get; set; } = "";

    [JsonPropertyName("cellName")]
    public string CellName { get; set; } = "";

    [JsonPropertyName("cellNameText")]
    public string CellNameText { get; set; } = "";

    [JsonPropertyName("q")]
    public int? Q { get; set; }

    [JsonPropertyName("r")]
    public int? R { get; set; }

    [JsonPropertyName("interactionTags")]
    public List<string>? InteractionTags { get; set; }

    // 仅 movable-cells 目标查询返回；普通地图响应不会包含这些字段。
    [JsonPropertyName("costSpeed")]
    public int? CostSpeed { get; set; }

    [JsonPropertyName("costCoins")]
    public int? CostCoins { get; set; }

    [JsonPropertyName("requiresRoll")]
    public bool? RequiresRoll { get; set; }
}

public sealed class MapEdgeDto
{
    [JsonPropertyName("edgeId")]
    public long EdgeId { get; set; }

    [JsonPropertyName("fromCellId")]
    public long FromCellId { get; set; }

    [JsonPropertyName("toCellId")]
    public long ToCellId { get; set; }

    [JsonPropertyName("edgeType")]
    public string EdgeType { get; set; } = "";

    [JsonPropertyName("edgeTypeText")]
    public string? EdgeTypeText { get; set; }

    [JsonPropertyName("passable")]
    public bool Passable { get; set; }

    [JsonPropertyName("requiredAbility")]
    public string? RequiredAbility { get; set; }

    [JsonPropertyName("requiredAbilityText")]
    public string? RequiredAbilityText { get; set; }
}

public sealed class PlayerViewResponseDto
{
    [JsonPropertyName("engineState")]
    public EngineStateDto? EngineState { get; set; }

    [JsonPropertyName("gameId")]
    public long GameId { get; set; }

    [JsonPropertyName("roomId")]
    public long? RoomId { get; set; }

    [JsonPropertyName("mapConfigId")]
    public long MapConfigId { get; set; }

    [JsonPropertyName("gameStatus")]
    public string GameStatus { get; set; } = "";

    [JsonPropertyName("gameStatusText")]
    public string? GameStatusText { get; set; }

    [JsonPropertyName("currentRound")]
    public int CurrentRound { get; set; }

    [JsonPropertyName("currentTurnUserId")]
    public long? CurrentTurnUserId { get; set; }

    [JsonPropertyName("currentTurnSeq")]
    public int CurrentTurnSeq { get; set; }

    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("winnerUserId")]
    public long? WinnerUserId { get; set; }

    [JsonPropertyName("players")]
    public List<PlayerViewPlayerDto>? Players { get; set; }
}

public sealed class EngineStateDto
{
    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }

    [JsonPropertyName("pausedReason")]
    public string? PausedReason { get; set; }

    [JsonPropertyName("connectionPolicy")]
    public GameConnectionPolicyDto? ConnectionPolicy { get; set; }

    // 仅连接接口的私有回执包含该字段，普通 player-view 通常为 null。
    [JsonPropertyName("connection")]
    public GameConnectionInfoDto? Connection { get; set; }

    // CORE_9 的 player-view 与 shop/current 指向同一份公共库存，复用同一 DTO。
    [JsonPropertyName("publicShop")]
    public CurrentShopResponseDto? PublicShop { get; set; }

    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("currentPlayerId")]
    public long? CurrentPlayerId { get; set; }

    [JsonPropertyName("pendingReward")]
    public PendingRewardDto? PendingReward { get; set; }

    [JsonPropertyName("pendingCheck")]
    public PendingCheckDto? PendingCheck { get; set; }

    [JsonPropertyName("availableActions")]
    public List<AvailableActionDto>? AvailableActions { get; set; }

    // 只有新鲜 player-view 明确返回 2 时才启用铁路与森林移动 v2。
    [JsonPropertyName("movementRulesVersion")]
    public int? MovementRulesVersion { get; set; }

    // 只有新鲜 player-view 明确返回 1 时才开放普通任务主动提交。
    [JsonPropertyName("questSubmissionVersion")]
    public int? QuestSubmissionVersion { get; set; }
}

public sealed class PendingRewardDto
{
    [JsonPropertyName("rewardId")]
    public long? RewardId { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("playerId")]
    public long? PlayerId { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("candidates")]
    public List<RewardCandidateDto>? Candidates { get; set; }

    [JsonPropertyName("canResolve")]
    public bool? CanResolve { get; set; }

    [JsonPropertyName("hasAuthority")]
    public bool? HasAuthority { get; set; }

    [JsonPropertyName("capacitySatisfied")]
    public bool? CapacitySatisfied { get; set; }

    [JsonPropertyName("requiresReplacement")]
    public bool? RequiresReplacement { get; set; }

    [JsonPropertyName("canExecute")]
    public bool? CanExecute { get; set; }

    [JsonPropertyName("executeUnavailableReason")]
    public string? ExecuteUnavailableReason { get; set; }

    [JsonPropertyName("unavailableReason")]
    public string? UnavailableReason { get; set; }

    [JsonPropertyName("canAbandon")]
    public bool? CanAbandon { get; set; }

    [JsonPropertyName("abandonUnavailableReason")]
    public string? AbandonUnavailableReason { get; set; }

    [JsonPropertyName("drawResult")]
    public DeckDrawDto? DrawResult { get; set; }
}

public sealed class RewardCandidateDto
{
    [JsonPropertyName("cardId")]
    public long? CardId { get; set; }

    // 当前客户端只处理 ITEM 奖励；字段缺失时不以 cardId 冒充 itemId。
    [JsonPropertyName("itemId")]
    public long? ItemId { get; set; }

    [JsonPropertyName("canExecute")]
    public bool? CanExecute { get; set; }

    [JsonPropertyName("executeUnavailableReason")]
    public string? ExecuteUnavailableReason { get; set; }

    [JsonPropertyName("capacitySatisfied")]
    public bool? CapacitySatisfied { get; set; }

    [JsonPropertyName("configuration")]
    public ItemRewardConfigurationDto? Configuration { get; set; }
}

// ITEM 候选携带的是本局冻结配置，不是玩家持有实例。
// 只声明当前展示和契约校验使用的字段，其他配置键由 System.Text.Json 忽略。
public sealed class ItemRewardConfigurationDto
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("item_name")]
    public string? ItemName { get; set; }

    [JsonPropertyName("item_type")]
    public string? ItemType { get; set; }

    [JsonPropertyName("item_category")]
    public string? ItemCategory { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("buy_price")]
    public int? BuyPrice { get; set; }

    [JsonPropertyName("sell_price")]
    public int? SellPrice { get; set; }

    [JsonPropertyName("enabled")]
    public int? Enabled { get; set; }

    [JsonPropertyName("purchasable")]
    public int? Purchasable { get; set; }

    [JsonPropertyName("effect_config")]
    public string? EffectConfig { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

public sealed class CurrentWorkResponseDto
{
    [JsonPropertyName("cellId")]
    public long? CellId { get; set; }

    [JsonPropertyName("cellName")]
    public string? CellName { get; set; }

    [JsonPropertyName("cellNameText")]
    public string? CellNameText { get; set; }

    [JsonPropertyName("attributeType")]
    public string? AttributeType { get; set; }

    [JsonPropertyName("attributeTypeText")]
    public string? AttributeTypeText { get; set; }

    [JsonPropertyName("difficulty")]
    public int? Difficulty { get; set; }

    [JsonPropertyName("socialCost")]
    public int? SocialCost { get; set; }

    [JsonPropertyName("rewardCoins")]
    public int? RewardCoins { get; set; }
}

public sealed class PendingCheckDto
{
    [JsonPropertyName("checkId")]
    public long? CheckId { get; set; }

    [JsonPropertyName("gameId")]
    public long? GameId { get; set; }

    [JsonPropertyName("playerId")]
    public long? PlayerId { get; set; }

    [JsonPropertyName("questId")]
    public long? QuestId { get; set; }

    [JsonPropertyName("businessType")]
    public string? BusinessType { get; set; }

    [JsonPropertyName("businessSourceId")]
    public long? BusinessSourceId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("rollType")]
    public string? RollType { get; set; }

    [JsonPropertyName("attemptNo")]
    public int? AttemptNo { get; set; }

    [JsonPropertyName("attributeType")]
    public string? AttributeType { get; set; }

    [JsonPropertyName("attributeValue")]
    public int? AttributeValue { get; set; }

    [JsonPropertyName("modifier")]
    public int? Modifier { get; set; }

    [JsonPropertyName("difficulty")]
    public int? Difficulty { get; set; }

    [JsonPropertyName("socialCost")]
    public int? SocialCost { get; set; }

    [JsonPropertyName("remainingSocial")]
    public int? RemainingSocial { get; set; }

    [JsonPropertyName("stateVersion")]
    public long? StateVersion { get; set; }

    [JsonPropertyName("personalTurnNo")]
    public int? PersonalTurnNo { get; set; }

    [JsonPropertyName("authorizationRound")]
    public int? AuthorizationRound { get; set; }

    [JsonPropertyName("canConfirm")]
    public bool? CanConfirm { get; set; }

    [JsonPropertyName("confirmUnavailableReason")]
    public string? ConfirmUnavailableReason { get; set; }

    [JsonPropertyName("canReroll")]
    public bool? CanReroll { get; set; }

    [JsonPropertyName("nextRerollSocialCost")]
    public int? NextRerollSocialCost { get; set; }

    [JsonPropertyName("rerollUnavailableReason")]
    public string? RerollUnavailableReason { get; set; }

    [JsonPropertyName("rerollChain")]
    public JsonNode? RerollChain { get; set; }

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("fallbackUsed")]
    public bool? FallbackUsed { get; set; }

    [JsonPropertyName("questResolution")]
    public JsonNode? QuestResolution { get; set; }

    [JsonPropertyName("settlementOperationId")]
    public string? SettlementOperationId { get; set; }

    [JsonPropertyName("cancellationReason")]
    public string? CancellationReason { get; set; }

    [JsonPropertyName("fixedDice")]
    public int? FixedDice { get; set; }

    [JsonPropertyName("preparation")]
    public PendingCheckPreparationDto? Preparation { get; set; }
}

public sealed class PendingCheckPreparationDto
{
    [JsonPropertyName("fromCellId")]
    public long? FromCellId { get; set; }

    [JsonPropertyName("requestedTargetCellId")]
    public long? RequestedTargetCellId { get; set; }

    [JsonPropertyName("fromQ")]
    public int? FromQ { get; set; }

    [JsonPropertyName("fromR")]
    public int? FromR { get; set; }

    [JsonPropertyName("targetQ")]
    public int? TargetQ { get; set; }

    [JsonPropertyName("targetR")]
    public int? TargetR { get; set; }

    [JsonPropertyName("directionIndex")]
    public int? DirectionIndex { get; set; }

    [JsonPropertyName("costSpeed")]
    public int? CostSpeed { get; set; }

    [JsonPropertyName("costCoins")]
    public int? CostCoins { get; set; }

    [JsonPropertyName("sourceId")]
    public long? SourceId { get; set; }

    [JsonPropertyName("cellId")]
    public long? CellId { get; set; }

    [JsonPropertyName("socialCost")]
    public int? SocialCost { get; set; }

    [JsonPropertyName("businessType")]
    public string? BusinessType { get; set; }

    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }
}

public sealed class CheckRerollRequestDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("authorizationRound")]
    public int AuthorizationRound { get; set; }
}

public sealed class CheckRollRequestDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("authorizationRound")]
    public int AuthorizationRound { get; set; }

    [JsonPropertyName("dice")]
    public int Dice { get; set; }
}

public sealed class CheckResultDto
{
    [JsonPropertyName("dice")]
    public int? Dice { get; set; }

    [JsonPropertyName("attributeType")]
    public string? AttributeType { get; set; }

    [JsonPropertyName("attributeTypeText")]
    public string? AttributeTypeText { get; set; }

    [JsonPropertyName("attributeValue")]
    public int? AttributeValue { get; set; }

    [JsonPropertyName("modifier")]
    public int? Modifier { get; set; }

    [JsonPropertyName("finalValue")]
    public int? FinalValue { get; set; }

    [JsonPropertyName("difficulty")]
    public int? Difficulty { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }
}

public sealed class WorkResultDto
{
    [JsonPropertyName("socialCost")]
    public int? SocialCost { get; set; }

    [JsonPropertyName("remainingSocial")]
    public int? RemainingSocial { get; set; }

    [JsonPropertyName("rewardCoins")]
    public int? RewardCoins { get; set; }

    [JsonPropertyName("coinsAfter")]
    public int? CoinsAfter { get; set; }

    [JsonPropertyName("checkLog")]
    public CheckResultDto? CheckLog { get; set; }

    [JsonPropertyName("forceEndTurn")]
    public bool? ForceEndTurn { get; set; }
}

public sealed class DeckDrawDto
{
    [JsonPropertyName("drawBatchId")]
    public string? DrawBatchId { get; set; }

    [JsonPropertyName("deckType")]
    public string? DeckType { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("requestedCount")]
    public int? RequestedCount { get; set; }

    [JsonPropertyName("actualCount")]
    public int? ActualCount { get; set; }

    [JsonPropertyName("shortage")]
    public bool? Shortage { get; set; }

    [JsonPropertyName("shortageCode")]
    public string? ShortageCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // 历史响应可能仍返回单卡标识，保留兼容解析但不用于新 FIFO 领取。
    [JsonPropertyName("cardId")]
    public long? CardId { get; set; }
}

public sealed class CheckRollResponseDto
{
    [JsonPropertyName("pendingCheck")]
    public PendingCheckDto? PendingCheck { get; set; }

    [JsonPropertyName("submittedDice")]
    public int? SubmittedDice { get; set; }

    [JsonPropertyName("effectiveDice")]
    public int? EffectiveDice { get; set; }

    [JsonPropertyName("checkResult")]
    public CheckResultDto? CheckResult { get; set; }

    [JsonPropertyName("questUpdates")]
    public JsonNode? QuestUpdates { get; set; }

    [JsonPropertyName("playerViewState")]
    public PlayerViewResponseDto? PlayerViewState { get; set; }

    [JsonPropertyName("playerFacingMessage")]
    public string? PlayerFacingMessage { get; set; }

    [JsonPropertyName("idempotentReplay")]
    public bool? IdempotentReplay { get; set; }

    [JsonPropertyName("workResult")]
    public WorkResultDto? WorkResult { get; set; }

    [JsonPropertyName("movement")]
    public GameMovementResultDto? Movement { get; set; }

    [JsonPropertyName("partnerOfferId")]
    public long? PartnerOfferId { get; set; }

    [JsonPropertyName("logs")]
    public JsonNode? Logs { get; set; }

    [JsonPropertyName("deckDraws")]
    public List<DeckDrawDto>? DeckDraws { get; set; }
}

public sealed class GameConnectionPolicyDto
{
    [JsonPropertyName("heartbeatSeconds")]
    public int? HeartbeatSeconds { get; set; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    [JsonPropertyName("reconnectWaitSeconds")]
    public int? ReconnectWaitSeconds { get; set; }

    [JsonPropertyName("initialGraceSeconds")]
    public int? InitialGraceSeconds { get; set; }
}

public sealed class GameConnectionInfoDto
{
    [JsonPropertyName("clientInstanceId")]
    public string? ClientInstanceId { get; set; }

    [JsonPropertyName("connectionId")]
    public string? ConnectionId { get; set; }

    [JsonPropertyName("serverTime")]
    public string? ServerTime { get; set; }

    [JsonPropertyName("lastHeartbeatAt")]
    public string? LastHeartbeatAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("serverTimeZone")]
    public string? ServerTimeZone { get; set; }

    [JsonPropertyName("serverUtcOffset")]
    public string? ServerUtcOffset { get; set; }

    [JsonPropertyName("connectionActive")]
    public bool? ConnectionActive { get; set; }

    [JsonPropertyName("playerConnected")]
    public bool? PlayerConnected { get; set; }

    [JsonPropertyName("heartbeatSeconds")]
    public int? HeartbeatSeconds { get; set; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    [JsonPropertyName("reconnectWaitSeconds")]
    public int? ReconnectWaitSeconds { get; set; }

    [JsonPropertyName("initialGraceSeconds")]
    public int? InitialGraceSeconds { get; set; }
}

public sealed class PlayerConnectionRequestDto
{
    [JsonPropertyName("clientInstanceId")]
    public string ClientInstanceId { get; set; } = "";

    [JsonPropertyName("connectionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectionId { get; set; }
}

public sealed class QuestOfferListResponseDto
{
    [JsonPropertyName("gameId")]
    public long? GameId { get; set; }

    [JsonPropertyName("playerId")]
    public long? PlayerId { get; set; }

    [JsonPropertyName("refreshSocialCost")]
    public int? RefreshSocialCost { get; set; }

    [JsonPropertyName("offers")]
    public List<QuestOfferDto>? Offers { get; set; }
}

public sealed class QuestOfferDto
{
    [JsonPropertyName("offerId")]
    public long? OfferId { get; set; }

    [JsonPropertyName("questId")]
    public long? QuestId { get; set; }

    [JsonPropertyName("slotNo")]
    public int? SlotNo { get; set; }

    [JsonPropertyName("questCode")]
    public string? QuestCode { get; set; }

    [JsonPropertyName("questName")]
    public string? QuestName { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("rewardText")]
    public string? RewardText { get; set; }

    [JsonPropertyName("locationText")]
    public string? LocationText { get; set; }
}

public sealed class PlayerQuestListResponseDto
{
    [JsonPropertyName("gameId")]
    public long? GameId { get; set; }

    [JsonPropertyName("playerId")]
    public long? PlayerId { get; set; }

    [JsonPropertyName("quests")]
    public List<PlayerQuestDto>? Quests { get; set; }

    // 本轮只展示当前任务，但仍显式解析已完成快照，避免误把它并入当前任务。
    [JsonPropertyName("completedQuests")]
    public List<PlayerQuestDto>? CompletedQuests { get; set; }
}

public sealed class PlayerQuestDto
{
    [JsonPropertyName("playerQuestId")]
    public long? PlayerQuestId { get; set; }

    [JsonPropertyName("questId")]
    public long? QuestId { get; set; }

    [JsonPropertyName("questCode")]
    public string? QuestCode { get; set; }

    [JsonPropertyName("questName")]
    public string? QuestName { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("rewardText")]
    public string? RewardText { get; set; }

    [JsonPropertyName("locationText")]
    public string? LocationText { get; set; }

    [JsonPropertyName("triggerType")]
    public string? TriggerType { get; set; }

    [JsonPropertyName("checkAttributeTypes")]
    public string? CheckAttributeTypes { get; set; }

    [JsonPropertyName("requiredSuccessAttributes")]
    public string? RequiredSuccessAttributes { get; set; }

    [JsonPropertyName("difficultyLevel")]
    public int? DifficultyLevel { get; set; }

    [JsonPropertyName("requiredSocialCost")]
    public int? RequiredSocialCost { get; set; }

    [JsonPropertyName("requiredItemId")]
    public long? RequiredItemId { get; set; }

    [JsonPropertyName("requiredItemCategory")]
    public string? RequiredItemCategory { get; set; }

    [JsonPropertyName("requiredCoinCost")]
    public int? RequiredCoinCost { get; set; }

    [JsonPropertyName("requiredPrestigeCost")]
    public int? RequiredPrestigeCost { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("statusText")]
    public string? StatusText { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [JsonPropertyName("targetCount")]
    public int? TargetCount { get; set; }

    [JsonPropertyName("objectives")]
    public List<QuestObjectiveDto>? Objectives { get; set; }

    [JsonPropertyName("conditions")]
    public List<QuestConditionDto>? Conditions { get; set; }

    [JsonPropertyName("objectivesStatus")]
    public string? ObjectivesStatus { get; set; }

    [JsonPropertyName("objectiveNotes")]
    public List<string>? ObjectiveNotes { get; set; }

    [JsonPropertyName("submission")]
    public QuestSubmissionDto? Submission { get; set; }

    [JsonPropertyName("checkAttributes")]
    public List<QuestCheckAttributeDto>? CheckAttributes { get; set; }
}

public sealed class QuestCheckAttributeDto
{
    [JsonPropertyName("attributeType")]
    public string? AttributeType { get; set; }

    [JsonPropertyName("completed")]
    public bool? Completed { get; set; }

    [JsonPropertyName("eligible")]
    public bool? Eligible { get; set; }

    [JsonPropertyName("available")]
    public bool? Available { get; set; }

    [JsonPropertyName("unavailableReason")]
    public string? UnavailableReason { get; set; }
}

public sealed class QuestObjectiveDto
{
    [JsonPropertyName("objectiveKey")]
    public string? ObjectiveKey { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("currentValue")]
    public int? CurrentValue { get; set; }

    [JsonPropertyName("requiredValue")]
    public int? RequiredValue { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("successful")]
    public bool? Successful { get; set; }

    [JsonPropertyName("stateAvailable")]
    public bool? StateAvailable { get; set; }
}

public sealed class QuestConditionDto
{
    [JsonPropertyName("conditionKey")]
    public string? ConditionKey { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("satisfied")]
    public bool? Satisfied { get; set; }
}

public sealed class QuestSubmissionDto
{
    [JsonPropertyName("protocolVersion")]
    public int? ProtocolVersion { get; set; }

    [JsonPropertyName("targetsLocked")]
    public bool? TargetsLocked { get; set; }

    [JsonPropertyName("achievedPersonalTurn")]
    public int? AchievedPersonalTurn { get; set; }

    [JsonPropertyName("earliestSubmitPersonalTurn")]
    public int? EarliestSubmitPersonalTurn { get; set; }

    [JsonPropertyName("canSubmit")]
    public bool? CanSubmit { get; set; }

    [JsonPropertyName("unavailableReason")]
    public string? UnavailableReason { get; set; }

    [JsonPropertyName("rewardPreview")]
    public QuestRewardPreviewDto? RewardPreview { get; set; }

    [JsonPropertyName("rewardPaid")]
    public QuestRewardPaidDto? RewardPaid { get; set; }
}

public sealed class QuestRewardPreviewDto
{
    [JsonPropertyName("coins")]
    public int? Coins { get; set; }

    [JsonPropertyName("prestige")]
    public int? Prestige { get; set; }

    [JsonPropertyName("partnerCoins")]
    public int? PartnerCoins { get; set; }

    [JsonPropertyName("itemCount")]
    public int? ItemCount { get; set; }
}

public sealed class QuestRewardPaidDto
{
    [JsonPropertyName("coins")]
    public int? Coins { get; set; }

    [JsonPropertyName("prestige")]
    public int? Prestige { get; set; }

    [JsonPropertyName("partnerCoins")]
    public int? PartnerCoins { get; set; }

    [JsonPropertyName("pendingItemRewardCount")]
    public int? PendingItemRewardCount { get; set; }
}

public sealed class PlayerItemsResponseDto
{
    [JsonPropertyName("gameId")]
    public long? GameId { get; set; }

    [JsonPropertyName("playerId")]
    public long? PlayerId { get; set; }

    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<PlayerItemDto>? Items { get; set; }

    [JsonPropertyName("pendingChoices")]
    public JsonNode? PendingChoices { get; set; }
}

public sealed class PlayerItemDto
{
    [JsonPropertyName("playerItemId")]
    public long? PlayerItemId { get; set; }

    [JsonPropertyName("itemId")]
    public long? ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("itemType")]
    public string? ItemType { get; set; }

    [JsonPropertyName("itemTypeText")]
    public string? ItemTypeText { get; set; }

    [JsonPropertyName("itemCategory")]
    public string? ItemCategory { get; set; }

    [JsonPropertyName("buyPrice")]
    public int? BuyPrice { get; set; }

    [JsonPropertyName("sellPrice")]
    public int? SellPrice { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("itemTag")]
    public string? ItemTag { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("locationText")]
    public string? LocationText { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
}

public sealed class CurrentShopResponseDto
{
    [JsonPropertyName("cellId")]
    public long? CellId { get; set; }

    [JsonPropertyName("cellName")]
    public string? CellName { get; set; }

    [JsonPropertyName("cellNameText")]
    public string? CellNameText { get; set; }

    [JsonPropertyName("refreshSocialCost")]
    public int? RefreshSocialCost { get; set; }

    [JsonPropertyName("shelfSize")]
    public int? ShelfSize { get; set; }

    [JsonPropertyName("items")]
    public List<ShopShelfItemDto>? Items { get; set; }

    [JsonPropertyName("inventoryScope")]
    public string? InventoryScope { get; set; }

    [JsonPropertyName("inventoryGameId")]
    public long? InventoryGameId { get; set; }

    [JsonPropertyName("accessAllowed")]
    public bool? AccessAllowed { get; set; }

    [JsonPropertyName("stateVersion")]
    public long? StateVersion { get; set; }
}

public sealed class ShopShelfItemDto
{
    [JsonPropertyName("shelfSlotId")]
    public long? ShelfSlotId { get; set; }

    [JsonPropertyName("itemId")]
    public long? ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("itemType")]
    public string? ItemType { get; set; }

    [JsonPropertyName("itemCategory")]
    public string? ItemCategory { get; set; }

    [JsonPropertyName("buyPrice")]
    public int? BuyPrice { get; set; }

    [JsonPropertyName("sellPrice")]
    public int? SellPrice { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class ShopBuyResultDto
{
    [JsonPropertyName("boughtItem")]
    public ShopBoughtItemDto? BoughtItem { get; set; }

    [JsonPropertyName("coinsAfter")]
    public int? CoinsAfter { get; set; }

    [JsonPropertyName("newShelfItem")]
    public ShopShelfItemDto? NewShelfItem { get; set; }
}

public sealed class ShopBoughtItemDto
{
    [JsonPropertyName("playerItemId")]
    public long? PlayerItemId { get; set; }

    [JsonPropertyName("itemId")]
    public long? ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("locationText")]
    public string? LocationText { get; set; }
}

public sealed class ItemUseResultDto
{
    [JsonPropertyName("playerItemId")]
    public long? PlayerItemId { get; set; }

    [JsonPropertyName("itemId")]
    public long? ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("itemType")]
    public string? ItemType { get; set; }

    [JsonPropertyName("itemTypeText")]
    public string? ItemTypeText { get; set; }

    [JsonPropertyName("locationAfter")]
    public string? LocationAfter { get; set; }

    [JsonPropertyName("locationAfterText")]
    public string? LocationAfterText { get; set; }

    [JsonPropertyName("replacedPlayerItemId")]
    public long? ReplacedPlayerItemId { get; set; }

    [JsonPropertyName("speedDelta")]
    public int? SpeedDelta { get; set; }

    [JsonPropertyName("speedAfter")]
    public int? SpeedAfter { get; set; }

    [JsonPropertyName("socialDelta")]
    public int? SocialDelta { get; set; }

    [JsonPropertyName("socialAfter")]
    public int? SocialAfter { get; set; }

    [JsonPropertyName("fromCellId")]
    public long? FromCellId { get; set; }

    [JsonPropertyName("toCellId")]
    public long? ToCellId { get; set; }
}

public sealed class PlayerViewPlayerDto
{
    [JsonPropertyName("playerId")]
    public long PlayerId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("initialCharacterId")]
    public long InitialCharacterId { get; set; }

    [JsonPropertyName("initialCharacterCode")]
    public string? InitialCharacterCode { get; set; }

    [JsonPropertyName("initialCharacterName")]
    public string? InitialCharacterName { get; set; }

    [JsonPropertyName("initialCharacterTitle")]
    public string? InitialCharacterTitle { get; set; }

    [JsonPropertyName("characterSkillName")]
    public string? CharacterSkillName { get; set; }

    [JsonPropertyName("characterSkillText")]
    public string? CharacterSkillText { get; set; }

    [JsonPropertyName("characterSkillEffectId")]
    public string? CharacterSkillEffectId { get; set; }

    [JsonPropertyName("turnOrder")]
    public int TurnOrder { get; set; }

    [JsonPropertyName("currentCellId")]
    public long? CurrentCellId { get; set; }

    [JsonPropertyName("currentCellCode")]
    public string? CurrentCellCode { get; set; }

    [JsonPropertyName("currentCellName")]
    public string? CurrentCellName { get; set; }

    [JsonPropertyName("currentCellNameText")]
    public string? CurrentCellNameText { get; set; }

    [JsonPropertyName("coins")]
    public int Coins { get; set; }

    [JsonPropertyName("prestige")]
    public int Prestige { get; set; }

    [JsonPropertyName("equivalentPrestige")]
    public int EquivalentPrestige { get; set; }

    [JsonPropertyName("currentSpeedPoints")]
    public int CurrentSpeedPoints { get; set; }

    [JsonPropertyName("currentSocialPoints")]
    public int CurrentSocialPoints { get; set; }

    [JsonPropertyName("isConnected")]
    public int IsConnected { get; set; }

    [JsonPropertyName("effectiveAttributes")]
    public EffectiveAttributesDto? EffectiveAttributes { get; set; }
}

public sealed class EffectiveAttributesDto
{
    [JsonPropertyName("strength")]
    public int Strength { get; set; }

    [JsonPropertyName("speed")]
    public int Speed { get; set; }

    [JsonPropertyName("social")]
    public int Social { get; set; }

    [JsonPropertyName("charm")]
    public int Charm { get; set; }

    [JsonPropertyName("magic")]
    public int Magic { get; set; }

    [JsonPropertyName("mind")]
    public int Mind { get; set; }
}

public sealed class AvailableActionDto
{
    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("actionTypeText")]
    public string? ActionTypeText { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("reasonText")]
    public string? ReasonText { get; set; }
}

public sealed class GameActionRequestDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("playerId")]
    public long PlayerId { get; set; }

    [JsonPropertyName("stateVersion")]
    public long StateVersion { get; set; }

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("payload")]
    public JsonObject Payload { get; set; } = new();
}

public sealed class GameActionResponseDto
{
    [JsonPropertyName("gameId")]
    public long GameId { get; set; }

    [JsonPropertyName("oldStateVersion")]
    public long OldStateVersion { get; set; }

    [JsonPropertyName("newStateVersion")]
    public long NewStateVersion { get; set; }

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("actionTypeText")]
    public string? ActionTypeText { get; set; }

    [JsonPropertyName("resultType")]
    public string? ResultType { get; set; }

    [JsonPropertyName("resultTypeText")]
    public string? ResultTypeText { get; set; }

    [JsonPropertyName("playerFacingMessage")]
    public string? PlayerFacingMessage { get; set; }

    [JsonPropertyName("movement")]
    public GameMovementResultDto? Movement { get; set; }

    [JsonPropertyName("shopBuy")]
    public ShopBuyResultDto? ShopBuy { get; set; }

    [JsonPropertyName("itemUse")]
    public ItemUseResultDto? ItemUse { get; set; }

    [JsonPropertyName("playerViewState")]
    public PlayerViewResponseDto? PlayerViewState { get; set; }

    [JsonPropertyName("nextAvailableActions")]
    public List<AvailableActionDto>? NextAvailableActions { get; set; }

    [JsonPropertyName("pendingCheck")]
    public PendingCheckDto? PendingCheck { get; set; }

    [JsonPropertyName("deckDraws")]
    public List<DeckDrawDto>? DeckDraws { get; set; }

    [JsonPropertyName("questUpdates")]
    public List<PlayerQuestDto>? QuestUpdates { get; set; }

    [JsonPropertyName("questSettlement")]
    public QuestSettlementDto? QuestSettlement { get; set; }
}

public sealed class QuestSettlementDto
{
    [JsonPropertyName("playerQuestId")]
    public long? PlayerQuestId { get; set; }

    [JsonPropertyName("questId")]
    public long? QuestId { get; set; }

    [JsonPropertyName("coinsGranted")]
    public int? CoinsGranted { get; set; }

    [JsonPropertyName("prestigeGranted")]
    public int? PrestigeGranted { get; set; }

    [JsonPropertyName("partnerBonusCoins")]
    public int? PartnerBonusCoins { get; set; }

    [JsonPropertyName("coinsAfter")]
    public int? CoinsAfter { get; set; }

    [JsonPropertyName("prestigeAfter")]
    public int? PrestigeAfter { get; set; }

    [JsonPropertyName("pendingItemRewardCount")]
    public int? PendingItemRewardCount { get; set; }
}

public sealed class GameMovementResultDto
{
    [JsonPropertyName("fromCellId")]
    public long FromCellId { get; set; }

    [JsonPropertyName("fromCellCode")]
    public string? FromCellCode { get; set; }

    [JsonPropertyName("fromCellName")]
    public string? FromCellName { get; set; }

    [JsonPropertyName("requestedTargetCellId")]
    public long RequestedTargetCellId { get; set; }

    [JsonPropertyName("requestedTargetCellCode")]
    public string? RequestedTargetCellCode { get; set; }

    [JsonPropertyName("requestedTargetCellName")]
    public string? RequestedTargetCellName { get; set; }

    [JsonPropertyName("actualTargetCellId")]
    public long ActualTargetCellId { get; set; }

    [JsonPropertyName("actualTargetCellCode")]
    public string? ActualTargetCellCode { get; set; }

    [JsonPropertyName("actualTargetCellName")]
    public string? ActualTargetCellName { get; set; }

    [JsonPropertyName("costSpeed")]
    public int CostSpeed { get; set; }

    [JsonPropertyName("remainingSpeed")]
    public int RemainingSpeed { get; set; }

    // 移动 v2 新字段对历史回执可缺失，因此保持可空，不能缺省成 0。
    [JsonPropertyName("costCoins")]
    public int? CostCoins { get; set; }

    [JsonPropertyName("coinsAfter")]
    public int? CoinsAfter { get; set; }

    [JsonPropertyName("movementOutcome")]
    public string? MovementOutcome { get; set; }

    [JsonPropertyName("stayReason")]
    public string? StayReason { get; set; }
}
