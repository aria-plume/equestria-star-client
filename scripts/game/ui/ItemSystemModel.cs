using System;
using System.Text.Json.Nodes;
using EquestriaStar.Api;

namespace EquestriaStar.Game.UI;

/// <summary>
/// 道具系统的纯逻辑边界：实例类型、位置和行动请求均由这里统一判断。
/// </summary>
public static class ItemSystemModel
{
    public const string Hand = "HAND";
    public const string Equipped = "EQUIPPED";
    public const string Consumable = "CONSUMABLE";
    public const string Equipment = "EQUIPMENT";

    public static bool IsAt(PlayerItemDto item, string location) =>
        string.Equals(item.Location?.Trim(), location, StringComparison.OrdinalIgnoreCase);

    public static bool TryResolveHandAction(PlayerItemDto item, out string actionType, out string buttonText)
    {
        actionType = "";
        buttonText = "";
        if (!IsAt(item, Hand))
        {
            return false;
        }

        if (string.Equals(item.ItemType?.Trim(), Consumable, StringComparison.OrdinalIgnoreCase))
        {
            actionType = "USE_ITEM";
            buttonText = "使用";
            return true;
        }

        if (string.Equals(item.ItemType?.Trim(), Equipment, StringComparison.OrdinalIgnoreCase))
        {
            actionType = "EQUIP_ITEM";
            buttonText = "装备";
            return true;
        }

        return false;
    }

    public static GameActionRequestDto CreateShopBuyRequest(
        long playerId,
        long stateVersion,
        long shelfSlotId,
        string requestId)
    {
        if (playerId <= 0 || stateVersion < 0 || shelfSlotId <= 0 || string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("购买请求缺少有效身份、版本、货架实例或 requestId。");
        }

        return new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = playerId,
            StateVersion = stateVersion,
            ActionType = "SHOP_BUY_ITEM",
            Payload = new JsonObject { ["shelfSlotId"] = shelfSlotId }
        };
    }

    public static GameActionRequestDto CreateItemRequest(
        long playerId,
        long stateVersion,
        string actionType,
        long playerItemId,
        long? replacePlayerItemId,
        string requestId)
    {
        var normalized = actionType?.Trim().ToUpperInvariant() ?? "";
        if (playerId <= 0
            || stateVersion < 0
            || playerItemId <= 0
            || string.IsNullOrWhiteSpace(requestId)
            || normalized is not ("USE_ITEM" or "EQUIP_ITEM" or "UNEQUIP_ITEM"))
        {
            throw new ArgumentException("道具请求缺少有效身份、版本、实例、行动类型或 requestId。");
        }

        var payload = new JsonObject { ["playerItemId"] = playerItemId };
        if (normalized == "EQUIP_ITEM" && replacePlayerItemId is > 0)
        {
            payload["replacePlayerItemId"] = replacePlayerItemId.Value;
        }

        return new GameActionRequestDto
        {
            RequestId = requestId,
            PlayerId = playerId,
            StateVersion = stateVersion,
            ActionType = normalized,
            Payload = payload
        };
    }
}
