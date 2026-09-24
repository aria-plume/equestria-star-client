using Godot;
using System;
using System.Collections.Generic;

namespace EquestriaStar.Api;

public partial class ApiConfig : Node
{
    public const string BaseUrl = "http://182.92.234.193:8080";
    public const string ApiPrefix = "/api/v1";
    public const int PageSize = 20;
    public const int MaxRoomPages = 50;
    public const double RequestTimeoutSeconds = 12.0;

    public static string BuildUrl(string path, IReadOnlyDictionary<string, string?>? query = null)
    {
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        var url = $"{BaseUrl}{ApiPrefix}{normalizedPath}";
        if (query == null || query.Count == 0)
        {
            return url;
        }

        var parts = new List<string>();
        foreach (var (key, value) in query)
        {
            if (value == null)
            {
                continue;
            }

            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        return parts.Count == 0 ? url : $"{url}?{string.Join("&", parts)}";
    }
}
