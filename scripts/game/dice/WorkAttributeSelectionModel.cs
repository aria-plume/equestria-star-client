using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using EquestriaStar.Api;

namespace EquestriaStar.Game.Dice;

/// <summary>
/// 打工属性选择的纯逻辑定义，服务端请求始终使用稳定英文枚举。
/// </summary>
public static class WorkAttributeSelectionModel
{
    public const string Any = "ANY";

    private static readonly IReadOnlyDictionary<string, string> AttributeLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STRENGTH"] = "力量",
            ["SPEED"] = "速度",
            ["SOCIAL"] = "社交",
            ["CHARM"] = "魅力",
            ["MAGIC"] = "魔法",
            ["MIND"] = "心智"
        };

    public static IReadOnlyList<string> AttributeTypes { get; } =
        ["STRENGTH", "SPEED", "SOCIAL", "CHARM", "MAGIC", "MIND"];

    public static bool IsAny(string? attributeType) =>
        string.Equals(attributeType?.Trim(), Any, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeSelectable(string? attributeType, out string normalized)
    {
        normalized = attributeType?.Trim().ToUpperInvariant() ?? "";
        return AttributeLabels.ContainsKey(normalized);
    }

    public static bool TryGetLabel(string? attributeType, out string label)
    {
        var normalized = attributeType?.Trim() ?? "";
        return AttributeLabels.TryGetValue(normalized, out label!);
    }

    public static GameActionRequestDto CreateWorkRequest(
        long playerId,
        long stateVersion,
        string attributeType,
        string requestId)
    {
        if (playerId <= 0 || stateVersion < 0 || string.IsNullOrWhiteSpace(requestId)
            || !TryNormalizeSelectable(attributeType, out var normalized))
        {
            throw new ArgumentException("WORK 请求必须包含有效玩家、版本、requestId 和具体六维属性。");
        }

        return new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = playerId,
            StateVersion = stateVersion,
            ActionType = "WORK",
            Payload = new JsonObject { ["attributeType"] = normalized }
        };
    }
}
