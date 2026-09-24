using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EquestriaStar.Api;

namespace EquestriaStar.State;

public enum CharacterConfirmAction
{
    None,
    LockRandom,
    SubmitCharacter
}

public enum CharacterSubmitFailureAction
{
    ShowError,
    RefreshRoom,
    ExpireAndWait,
    ReturnLobby
}

public sealed class CharacterCardOption
{
    public long? CharacterId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string AssetPath { get; init; } = "";
    public bool IsRandom { get; init; }
    public bool UsesFallbackAsset { get; init; }
}

public readonly record struct CharacterOccupancy(bool IsSelectedByCurrentUser, bool IsSelectedByOther, string OtherNickname)
{
    public static CharacterOccupancy Available => new(false, false, "");
}

public static class CharacterSelectionModel
{
    public const string RandomCode = "RANDOM";
    public const string CardBackPath = "res://assets/characters/cards/角色卡背.jpg";

    private static readonly IReadOnlyDictionary<string, string> CardPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["INITIAL_CHANCE_DICE"] = "res://assets/characters/cards/骰子.png",
        ["INITIAL_BIG_BRIAN"] = "res://assets/characters/cards/大布莱恩.jpg",
        ["INITIAL_ORANGE_FEATHER"] = "res://assets/characters/cards/橙羽.jpg",
        ["INITIAL_XUN_YU"] = "res://assets/characters/cards/循雨.jpg",
        ["INITIAL_HONG_FANG"] = "res://assets/characters/cards/hf.jpg",
        // TODO: API code 为 INITIAL_EGG_WHITE，但正式卡面文件名和文字是“蛋黄”，等待后端明确命名契约。
        ["INITIAL_EGG_WHITE"] = "res://assets/characters/cards/蛋黄.jpg",
        ["INITIAL_TURBID_STAR"] = "res://assets/characters/cards/浊星.jpg",
        ["INITIAL_EQUATIONS"] = "res://assets/characters/cards/正负等式.jpg",
        ["INITIAL_YE_LING"] = "res://assets/characters/cards/夜灵.jpg",
        ["INITIAL_DAN_QING"] = "res://assets/characters/cards/丹青.jpg",
        [RandomCode] = CardBackPath
    };

    public static IReadOnlyDictionary<string, string> KnownCardPaths => CardPaths;

    public static string ResolveCardPath(string code, out bool usedFallback)
    {
        if (CardPaths.TryGetValue(code, out var path))
        {
            usedFallback = false;
            return path;
        }

        usedFallback = true;
        return CardBackPath;
    }

    public static List<CharacterCardOption> BuildOptions(IEnumerable<CharacterDto> characters)
    {
        var options = characters
            .Where(character => string.Equals(character.CharacterType, "INITIAL", StringComparison.OrdinalIgnoreCase))
            .Select(character =>
            {
                var path = ResolveCardPath(character.Code, out var fallback);
                return new CharacterCardOption
                {
                    CharacterId = character.CharacterId,
                    Code = character.Code,
                    Name = character.Name,
                    AssetPath = path,
                    UsesFallbackAsset = fallback
                };
            })
            .ToList();

        options.Add(new CharacterCardOption
        {
            CharacterId = null,
            Code = RandomCode,
            Name = "随机角色",
            AssetPath = CardBackPath,
            IsRandom = true
        });
        return options;
    }

    public static long? CurrentSelectedCharacterId(RoomDetailDto room, long currentUserId)
    {
        return RoomStateModel.CurrentMember(room, currentUserId)?.SelectedCharacterId;
    }

    public static CharacterOccupancy GetOccupancy(RoomDetailDto room, long? characterId, long currentUserId)
    {
        if (!characterId.HasValue)
        {
            return CharacterOccupancy.Available;
        }

        var member = (room.Members ?? []).FirstOrDefault(item => item.SelectedCharacterId == characterId.Value);
        if (member == null)
        {
            return CharacterOccupancy.Available;
        }

        if (member.UserId == currentUserId)
        {
            return new CharacterOccupancy(true, false, "");
        }

        var nickname = string.IsNullOrWhiteSpace(member.Nickname) ? member.Username : member.Nickname;
        return new CharacterOccupancy(false, true, nickname);
    }

    public static bool CanConfirm(
        CharacterCardOption option,
        CharacterOccupancy occupancy,
        bool currentPlayerHasConfirmed,
        bool randomLocked,
        bool deadlineExpired,
        bool requestInProgress,
        bool hasDeadline)
    {
        if (currentPlayerHasConfirmed || randomLocked || deadlineExpired || requestInProgress || !hasDeadline)
        {
            return false;
        }

        return option.IsRandom || (option.CharacterId.HasValue && !occupancy.IsSelectedByOther);
    }

    public static CharacterConfirmAction ConfirmActionFor(CharacterCardOption option, bool canConfirm)
    {
        if (!canConfirm)
        {
            return CharacterConfirmAction.None;
        }

        return option.IsRandom ? CharacterConfirmAction.LockRandom : CharacterConfirmAction.SubmitCharacter;
    }

    public static CharacterSubmitFailureAction FailureActionFor(string? errorCode)
    {
        return errorCode?.ToUpperInvariant() switch
        {
            "ROOM_CHARACTER_DUPLICATED" => CharacterSubmitFailureAction.RefreshRoom,
            "ROOM_CHARACTER_SELECTION_EXPIRED" => CharacterSubmitFailureAction.ExpireAndWait,
            "ROOM_NOT_FOUND" => CharacterSubmitFailureAction.ReturnLobby,
            _ => CharacterSubmitFailureAction.ShowError
        };
    }

    public static bool TryParseServerDeadline(string? value, out DateTimeOffset deadline)
    {
        deadline = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (HasExplicitOffset(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out deadline))
        {
            return true;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var localTime))
        {
            return false;
        }

        // TODO: 服务端尚未明确 characterSelectionDeadline 时区；当前按客户端本机时区解释无偏移值。
        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        deadline = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        return true;
    }

    public static int RemainingSeconds(DateTimeOffset deadline, DateTimeOffset now)
    {
        return Math.Max(0, (int)Math.Ceiling((deadline - now).TotalSeconds));
    }

    private static bool HasExplicitOffset(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var timeSeparator = trimmed.IndexOf('T');
        if (timeSeparator < 0)
        {
            return false;
        }

        return trimmed.IndexOf('+', timeSeparator) >= 0 || trimmed.LastIndexOf('-', trimmed.Length - 1) > timeSeparator;
    }
}
