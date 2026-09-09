using System.Net;
using System.Net.Mail;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Notifications;

public sealed record QqSmtpSettings(string SenderAddress, string AuthorizationCode, string RecipientAddress);

public sealed class QqEmailNotifier
{
    public async Task SendReportAsync(QqSmtpSettings settings, RunReportDto report, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var message = new MailMessage(settings.SenderAddress.Trim(), settings.RecipientAddress.Trim())
                {
                    Subject = $"BetterGI 每日任务：{FeishuNotifier.TranslateStatus(report.Status)}",
                    Body = FeishuNotifier.FormatReport(report),
                    IsBodyHtml = false,
                };
                using var client = new SmtpClient("smtp.qq.com", 587)
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(settings.SenderAddress.Trim(), settings.AuthorizationCode.Trim()),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 15_000,
                };
                using var registration = cancellationToken.Register(client.SendAsyncCancel);
                await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (attempt < 3 && exception is SmtpException or TimeoutException)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }
        }
        throw lastError ?? new SmtpException("QQ 邮箱通知发送失败。");
    }

    public static void Validate(QqSmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!MailAddress.TryCreate(settings.SenderAddress?.Trim(), out var sender) ||
            !sender.Host.Equals("qq.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("发件邮箱必须是有效的 QQ 邮箱地址。");
        }
        if (string.IsNullOrWhiteSpace(settings.AuthorizationCode) || settings.AuthorizationCode.Trim().Length < 8)
        {
            throw new InvalidOperationException("请填写 QQ 邮箱 SMTP 授权码，不是 QQ 登录密码。");
        }
        if (!MailAddress.TryCreate(settings.RecipientAddress?.Trim(), out _))
        {
            throw new InvalidOperationException("收件邮箱地址无效。");
        }
    }
}
