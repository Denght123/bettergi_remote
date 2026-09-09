using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Security;

public sealed class EncryptedEnvelopeCodec
{
    private readonly byte[] _key;
    private readonly string _outgoingDirection;
    private readonly string _incomingDirection;
    private readonly TimeProvider _timeProvider;

    public EncryptedEnvelopeCodec(ReadOnlySpan<byte> key, string outgoingDirection, string incomingDirection, TimeProvider? timeProvider = null)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("AES key must contain 32 bytes.", nameof(key));
        }
        _key = key.ToArray();
        _outgoingDirection = outgoingDirection;
        _incomingDirection = incomingDirection;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public EncryptedEnvelope Encrypt<T>(T message, string requestId)
    {
        var now = _timeProvider.GetUtcNow();
        return EncryptWithValues(message, requestId, now.ToUnixTimeSeconds(), now.AddSeconds(60).ToUnixTimeSeconds(), RandomNumberGenerator.GetBytes(12));
    }

    public EncryptedEnvelope EncryptWithValues<T>(T message, string requestId, long issuedAt, long expiresAt, ReadOnlySpan<byte> nonce)
    {
        ValidateRequestId(requestId);
        if (nonce.Length != 12)
        {
            throw new ArgumentException("AES-GCM nonce must contain 12 bytes.", nameof(nonce));
        }
        if (expiresAt <= issuedAt || expiresAt - issuedAt > ProtocolConstants.MaximumMessageLifetime.TotalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(message, RemoteJson.Options);
        if (plaintext.Length > ProtocolConstants.MaximumPlaintextBytes)
        {
            throw new InvalidOperationException("Plaintext is too large.");
        }
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var associatedData = BuildAssociatedData(_outgoingDirection, requestId, issuedAt, expiresAt);
        try
        {
            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            var combined = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(combined, 0);
            tag.CopyTo(combined, ciphertext.Length);
            return new EncryptedEnvelope(
                ProtocolConstants.Version,
                _outgoingDirection,
                requestId,
                issuedAt,
                expiresAt,
                Base64Url.Encode(nonce),
                Base64Url.Encode(combined));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public T Decrypt<T>(EncryptedEnvelope envelope)
    {
        ValidateEnvelope(envelope);
        var nonce = Base64Url.Decode(envelope.Nonce, 12);
        var combined = Base64Url.Decode(envelope.Ciphertext, ProtocolConstants.MaximumPlaintextBytes + 16);
        if (nonce.Length != 12 || combined.Length < 16)
        {
            throw new CryptographicException("Invalid encrypted envelope.");
        }

        var ciphertextLength = combined.Length - 16;
        var plaintext = new byte[ciphertextLength];
        var associatedData = BuildAssociatedData(envelope.Direction, envelope.RequestId, envelope.IssuedAtUnixSeconds, envelope.ExpiresAtUnixSeconds);
        try
        {
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, combined.AsSpan(0, ciphertextLength), combined.AsSpan(ciphertextLength, 16), plaintext, associatedData);
            return JsonSerializer.Deserialize<T>(plaintext, RemoteJson.Options)
                ?? throw new JsonException("Decrypted message is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(combined);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private void ValidateEnvelope(EncryptedEnvelope envelope)
    {
        if (envelope.ProtocolVersion != ProtocolConstants.Version || !string.Equals(envelope.Direction, _incomingDirection, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unexpected encrypted envelope direction or version.");
        }
        ValidateRequestId(envelope.RequestId);
        if (envelope.ExpiresAtUnixSeconds <= envelope.IssuedAtUnixSeconds ||
            envelope.ExpiresAtUnixSeconds - envelope.IssuedAtUnixSeconds > ProtocolConstants.MaximumMessageLifetime.TotalSeconds)
        {
            throw new CryptographicException("Invalid encrypted envelope lifetime.");
        }
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (envelope.IssuedAtUnixSeconds > now + ProtocolConstants.ClockSkew.TotalSeconds ||
            envelope.ExpiresAtUnixSeconds < now - ProtocolConstants.ClockSkew.TotalSeconds)
        {
            throw new CryptographicException("Encrypted envelope has expired.");
        }
    }

    private static byte[] BuildAssociatedData(string direction, string requestId, long issuedAt, long expiresAt)
        => Encoding.UTF8.GetBytes($"{ProtocolConstants.Version}\n{direction}\n{requestId}\n{issuedAt}\n{expiresAt}");

    private static void ValidateRequestId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128 || requestId.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException("Invalid request ID.", nameof(requestId));
        }
    }
}
