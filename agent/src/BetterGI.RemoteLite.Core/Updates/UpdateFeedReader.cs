using System.Text.Json;

namespace BetterGI.RemoteLite.Updates;

public static class UpdateFeedReader
{
    public static JsonDocument ParseObject(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException($"{source}返回了空内容。");
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new InvalidDataException($"{source}返回的版本信息格式不正确。");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{source}返回了网页或无效的版本信息，暂时无法检查更新。", exception);
        }
    }

    public static string RequiredString(JsonElement root, string property, string label)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"版本信息缺少有效的{label}。");
        return value.GetString()!.Trim();
    }

    public static string? OptionalString(JsonElement root, string property, string label)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"版本信息中的{label}格式不正确。");
        return value.GetString()?.Trim();
    }

    public static string ValidDigest(string digest)
    {
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("版本信息缺少有效的 SHA-256 校验摘要。");
        return digest.ToUpperInvariant();
    }

    public static async Task<T> ReadWithFallbackAsync<T>(HttpClient client, string primaryUrl, string fallbackUrl,
        Func<string, bool, T> parse, string unavailableMessage, CancellationToken cancellationToken = default)
    {
        var failures = new List<Exception>();
        var urls = new[] { primaryUrl, fallbackUrl };
        for (var index = 0; index < urls.Length; index++)
        {
            var url = urls[index];
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await client.GetAsync(url, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null && !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
                    !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新服务返回了网页内容，正在改用备用更新源。");
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                if (body.Length > 1024 * 1024) throw new InvalidDataException("更新服务返回内容过大，暂时无法检查更新。");
                return parse(body, index > 0);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                exception is HttpRequestException or OperationCanceledException or InvalidDataException)
            { failures.Add(exception); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(unavailableMessage, new AggregateException(failures));
    }
}
