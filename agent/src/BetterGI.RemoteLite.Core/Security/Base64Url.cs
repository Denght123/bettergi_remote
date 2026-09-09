namespace BetterGI.RemoteLite.Security;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value, int maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('=') || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new FormatException("Value is not canonical Base64Url.");
        }

        var padding = (value.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid Base64Url length."),
        };
        var decoded = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + padding);
        if (decoded.Length > maximumBytes)
        {
            throw new FormatException("Decoded value is too large.");
        }
        if (!string.Equals(Encode(decoded), value, StringComparison.Ordinal))
        {
            throw new FormatException("Value is not canonical Base64Url.");
        }
        return decoded;
    }
}
