using System.Security.Cryptography;
using System.Text;

namespace GameRadar.Core.Xbox;

/// <summary>
/// PKCE (Proof Key for Code Exchange, RFC 7636): how a desktop app, which
/// cannot keep a secret, proves that the sign-in code it redeems is its own.
/// The app sends the challenge (a hash) when opening the sign-in page, then
/// the verifier (the original) when redeeming the code: a program that
/// intercepted the code cannot redeem it without the verifier.
/// </summary>
internal static class Pkce
{
    /// <summary>Random, URL-safe string. Also used for the anti-forgery "state".</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeOf(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    // Base64 without padding, with the two URL-unsafe characters replaced.
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
