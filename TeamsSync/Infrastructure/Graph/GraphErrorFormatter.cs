using System.Net;
using System.Text.Json;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>Graphのエラー応答から、ユーザーへ安全に提示できる診断情報だけを抽出する</summary>
internal static class GraphErrorFormatter
{
    private const int MaximumDiagnosticMessageLength = 512;

    public static string Format(HttpStatusCode status, string responseBody,
        string? requestId, string clientRequestId)
    {
        (string? code, string? message) = Parse(responseBody);
        List<string> details = [$"Graph API エラー ({(int)status} {status})"];
        if (!string.IsNullOrWhiteSpace(code))
        {
            details.Add($"コード: {code}");
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            details.Add($"内容: {message}");
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            details.Add($"request-id: {requestId}");
        }

        details.Add($"client-request-id: {clientRequestId}");
        return string.Join(Environment.NewLine, details);
    }

    public static string DiagnosticSummary(string responseBody)
    {
        (string? code, string? message) = Parse(responseBody);
        string safeMessage = string.IsNullOrWhiteSpace(message)
            ? "構造化されたGraphエラー詳細なし"
            : message[..Math.Min(message.Length, MaximumDiagnosticMessageLength)];
        return $"Code={code ?? "unknown"}, Message={safeMessage}, BodyLength={responseBody.Length}";
    }

    private static (string? Code, string? Message) Parse(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                return default;
            }

            string? code = ReadString(error, "code");
            string? message = ReadString(error, "message");
            return (code, message);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}