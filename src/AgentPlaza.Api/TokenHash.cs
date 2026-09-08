using System.Security.Cryptography;
using System.Text;

namespace AgentPlaza.Api;

internal static class TokenHash
{
    public static string Create(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool EqualsToken(string left, string right)
    {
        var leftBytes = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightBytes = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static bool Verify(string token, string expectedHash)
    {
        var actualBytes = Encoding.ASCII.GetBytes(Create(token));
        var expectedBytes = Encoding.ASCII.GetBytes(expectedHash);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
