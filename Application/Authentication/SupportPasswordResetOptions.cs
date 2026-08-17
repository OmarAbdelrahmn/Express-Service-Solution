using System.Security.Cryptography;
using System.Text;

namespace Application.Authentication;

public sealed class SupportPasswordResetOptions
{
    public const string SectionName = "SupportPasswordReset";
    public const string HeaderName = "X-Support-Key";
    public const int MinimumKeyLength = 32;

    public string Key { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key) && Key.Length >= MinimumKeyLength;

    public bool Matches(string? suppliedKey)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(suppliedKey))
            return false;

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(Key));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
