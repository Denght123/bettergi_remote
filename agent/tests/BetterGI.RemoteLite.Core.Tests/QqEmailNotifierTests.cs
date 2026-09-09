using BetterGI.RemoteLite.Notifications;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class QqEmailNotifierTests
{
    [Fact]
    public void AcceptsQqSenderAndIndependentRecipient()
    {
        QqEmailNotifier.Validate(new QqSmtpSettings("123456@qq.com", "abcdefghijklmnop", "notice@example.com"));
    }

    [Theory]
    [InlineData("not-an-email", "abcdefghijklmnop", "notice@example.com")]
    [InlineData("user@example.com", "abcdefghijklmnop", "notice@example.com")]
    [InlineData("123456@qq.com", "short", "notice@example.com")]
    [InlineData("123456@qq.com", "abcdefghijklmnop", "bad")]
    public void RejectsInvalidSettings(string sender, string code, string recipient)
    {
        Assert.Throws<InvalidOperationException>(() => QqEmailNotifier.Validate(new QqSmtpSettings(sender, code, recipient)));
    }
}
