using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Notifications;

public sealed class FeishuNotifier(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task SendReportAsync(string webhook, string? signingSecret, RunReportDto report, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(webhook, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("飞书 Webhook 必须是 HTTPS 地址。");
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = new Dictionary<string, object?>
        {
            ["msg_type"] = "text",
            ["content"] = new Dictionary<string, string> { ["text"] = FormatReport(report) },
        };
        if (!string.IsNullOrWhiteSpace(signingSecret))
        {
            payload["timestamp"] = timestamp;
            payload["sign"] = CreateSignature(timestamp, signingSecret);
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(payload, RemoteJson.Options), Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"飞书返回 HTTP {(int)response.StatusCode}: {responseBody}");
                }
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt32() != 0)
                {
                    throw new InvalidOperationException("飞书机器人拒绝了消息: " + responseBody);
                }
                return;
            }
            catch (Exception exception) when (attempt < 3 && exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }
        }
        throw lastError ?? new InvalidOperationException("飞书消息发送失败。");
    }

    public static string CreateSignature(string timestamp, string secret)
    {
        var key = Encoding.UTF8.GetBytes(timestamp + "\n" + secret);
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash([]));
    }

    public static string FormatReport(RunReportDto report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"BetterGI 每日任务: {TranslateStatus(report.Status)}");
        builder.AppendLine($"耗时: {TimeSpan.FromSeconds(report.DurationSeconds):hh\\:mm\\:ss}");
        builder.AppendLine("任务:");
        foreach (var task in report.Tasks)
        {
            builder.AppendLine($"- {task.Name}: {TranslateStatus(task.State)}");
        }
        if (report.Rewards.Count > 0)
        {
            builder.AppendLine("识别奖励:");
            foreach (var reward in report.Rewards.OrderBy(item => item.Key, StringComparer.CurrentCulture))
            {
                builder.AppendLine($"- {reward.Key} x{reward.Value}");
            }
        }
        if (!string.IsNullOrWhiteSpace(report.DailyRewardStatus))
        {
            builder.AppendLine("每日奖励: " + report.DailyRewardStatus);
        }
        if (report.Errors.Count > 0)
        {
            builder.AppendLine("错误:");
            foreach (var error in report.Errors.Take(5))
            {
                builder.AppendLine("- " + error);
            }
        }
        return builder.ToString().TrimEnd();
    }

    public static string TranslateStatus(string status) => status switch
    {
        "success" => "成功",
        "failed" => "失败",
        "stopped" => "已停止",
        "warning" => "有警告",
        "running" => "执行中",
        "pending" => "等待",
        "skipped" => "跳过",
        _ => "未知",
    };
}
