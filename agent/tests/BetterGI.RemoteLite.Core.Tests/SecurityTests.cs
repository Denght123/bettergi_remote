using System.Security.Cryptography;
using BetterGI.RemoteLite.Protocol;
using BetterGI.RemoteLite.Security;
using System.Text.Json;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void PairingMaterialIsDeterministicAndDirectional()
    {
        var secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        using var first = PairingKeyMaterial.Derive(secret);
        using var second = PairingKeyMaterial.Derive(secret);

        Assert.Equal(22, first.ChannelId.Length);
        Assert.Equal(43, first.RelayToken.Length);
        Assert.Equal(first.ChannelId, second.ChannelId);
        Assert.Equal(first.RelayToken, second.RelayToken);
        Assert.True(first.PhoneToPcKey.SequenceEqual(second.PhoneToPcKey));
        Assert.True(first.PcToPhoneKey.SequenceEqual(second.PcToPhoneKey));
        Assert.False(first.PhoneToPcKey.SequenceEqual(first.PcToPhoneKey));
    }

    [Fact]
    public void EnvelopeRoundTripsAndRejectsTampering()
    {
        var key = SHA256.HashData("test-key"u8);
        var clock = new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_788_364_800));
        var sender = new EncryptedEnvelopeCodec(key, "phone-to-pc", "pc-to-phone", clock);
        var receiver = new EncryptedEnvelopeCodec(key, "pc-to-phone", "phone-to-pc", clock);
        var request = new PairRequest("device-1", "测试手机");
        var envelope = sender.EncryptWithValues(request, "request-1", 1_788_364_800, 1_788_364_860, Enumerable.Range(0, 12).Select(value => (byte)value).ToArray());

        Assert.Equal(request, receiver.Decrypt<PairRequest>(envelope));

        var ciphertext = Base64Url.Decode(envelope.Ciphertext, ProtocolConstants.MaximumPlaintextBytes + 16);
        ciphertext[0] ^= 0x01;
        var tampered = envelope with { Ciphertext = Base64Url.Encode(ciphertext) };
        Assert.ThrowsAny<CryptographicException>(() => receiver.Decrypt<PairRequest>(tampered));
    }

    [Fact]
    public void ReplayGuardRejectsDuplicateRequestId()
    {
        var guard = new RequestReplayGuard();
        Assert.True(guard.TryAccept("same-request"));
        Assert.False(guard.TryAccept("same-request"));
    }

    [Fact]
    public void MatchesPublishedNodeAndBrowserVector()
    {
        var vector = JsonSerializer.Deserialize<Vector>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vector-v1.json")), RemoteJson.Options)!;
        var secret = Base64Url.Decode(vector.Secret, 32);
        using var material = PairingKeyMaterial.Derive(secret);
        Assert.Equal(vector.ChannelId, material.ChannelId);
        Assert.Equal(vector.RelayToken, material.RelayToken);
        Assert.Equal(vector.PhoneToPcKey, Base64Url.Encode(material.PhoneToPcKey));
        Assert.Equal(vector.PcToPhoneKey, Base64Url.Encode(material.PcToPhoneKey));

        var clock = new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(vector.IssuedAtUnixSeconds));
        var codec = new EncryptedEnvelopeCodec(material.PcToPhoneKey, "pc-to-phone", "phone-to-pc", clock);
        var message = new { type = "rpc.response", requestId = vector.RequestId, ok = true, result = new { value = "跨平台" } };
        var envelope = codec.EncryptWithValues(message, vector.RequestId, vector.IssuedAtUnixSeconds, vector.ExpiresAtUnixSeconds, Base64Url.Decode(vector.Nonce, 12));
        Assert.Equal(vector.Ciphertext, envelope.Ciphertext);
    }

    private sealed class ManualTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed record Vector(
        string Secret,
        string ChannelId,
        string RelayToken,
        string PhoneToPcKey,
        string PcToPhoneKey,
        string RequestId,
        long IssuedAtUnixSeconds,
        long ExpiresAtUnixSeconds,
        string Nonce,
        string Plaintext,
        string Ciphertext);
}
