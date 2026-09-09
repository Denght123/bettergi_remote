using System.Security.Cryptography;
using System.Text;

namespace BetterGI.RemoteLite.Security;

public sealed class PairingKeyMaterial : IDisposable
{
    private const string ChannelLabel = "BetterGI Remote Lite channel";
    private const string RelayTokenLabel = "BetterGI Remote Lite relay token";
    private const string SaltLabel = "BetterGI Remote Lite v1";
    private const string PhoneToPcLabel = "BetterGI Remote Lite v1 phone-to-pc";
    private const string PcToPhoneLabel = "BetterGI Remote Lite v1 pc-to-phone";

    private readonly byte[] _phoneToPc;
    private readonly byte[] _pcToPhone;
    private bool _disposed;

    private PairingKeyMaterial(string channelId, string relayToken, byte[] phoneToPc, byte[] pcToPhone)
    {
        ChannelId = channelId;
        RelayToken = relayToken;
        _phoneToPc = phoneToPc;
        _pcToPhone = pcToPhone;
    }

    public string ChannelId { get; }
    public string RelayToken { get; }
    public ReadOnlySpan<byte> PhoneToPcKey => _disposed ? throw new ObjectDisposedException(nameof(PairingKeyMaterial)) : _phoneToPc;
    public ReadOnlySpan<byte> PcToPhoneKey => _disposed ? throw new ObjectDisposedException(nameof(PairingKeyMaterial)) : _pcToPhone;

    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(32);

    public static PairingKeyMaterial Derive(ReadOnlySpan<byte> secret)
    {
        if (secret.Length != 32)
        {
            throw new ArgumentException("Pairing secret must contain 32 bytes.", nameof(secret));
        }

        var channelInput = Combine(Encoding.UTF8.GetBytes(ChannelLabel), secret);
        var channelHash = SHA256.HashData(channelInput);
        var channelId = Base64Url.Encode(channelHash.AsSpan(0, 16));

        using var hmac = new HMACSHA256(secret.ToArray());
        var relayToken = Base64Url.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(RelayTokenLabel)));
        var saltInput = Combine(Encoding.UTF8.GetBytes(SaltLabel), Encoding.UTF8.GetBytes(channelId));
        var salt = SHA256.HashData(saltInput);
        var secretBytes = secret.ToArray();
        var phoneToPc = HKDF.DeriveKey(HashAlgorithmName.SHA256, secretBytes, 32, salt, Encoding.UTF8.GetBytes(PhoneToPcLabel));
        var pcToPhone = HKDF.DeriveKey(HashAlgorithmName.SHA256, secretBytes, 32, salt, Encoding.UTF8.GetBytes(PcToPhoneLabel));

        CryptographicOperations.ZeroMemory(channelInput);
        CryptographicOperations.ZeroMemory(channelHash);
        CryptographicOperations.ZeroMemory(saltInput);
        CryptographicOperations.ZeroMemory(salt);
        CryptographicOperations.ZeroMemory(secretBytes);
        return new PairingKeyMaterial(channelId, relayToken, phoneToPc, pcToPhone);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        CryptographicOperations.ZeroMemory(_phoneToPc);
        CryptographicOperations.ZeroMemory(_pcToPhone);
        _disposed = true;
    }

    private static byte[] Combine(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var result = new byte[left.Length + right.Length];
        left.CopyTo(result);
        right.CopyTo(result.AsSpan(left.Length));
        return result;
    }
}
