using System.Security.Cryptography;
using System.Text;

namespace BetterGI.RemoteLite.Agent.Storage;

internal static class SecretProtector
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("BetterGI Remote Lite v1 settings"));

    public static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        var protectedBytes = Convert.FromBase64String(value);
        var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string? ProtectBytes(ReadOnlySpan<byte> value) => Protect(Convert.ToBase64String(value));

    public static byte[]? UnprotectBytes(string? value)
    {
        var text = Unprotect(value);
        return text is null ? null : Convert.FromBase64String(text);
    }
}

