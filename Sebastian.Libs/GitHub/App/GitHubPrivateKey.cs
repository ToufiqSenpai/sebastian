using System.Security.Cryptography;

namespace Sebastian.Libs.GitHub.App;

public static class GitHubPrivateKey
{
    /// <summary>Parses a PEM private key.</summary>
    /// <param name="pem">PKCS#8 or PKCS#1 PEM text.</param>
    /// <returns>The parsed key, which the caller must dispose, or null.</returns>
    public static RSA? TryCreate(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        RSA? key = null;
        try
        {
            key = RSA.Create();
            key.ImportFromPem(pem);
            return key;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            key?.Dispose();
            return null;
        }
    }

    public static bool IsValid(string? pem)
    {
        using var key = TryCreate(pem);
        return key is not null;
    }
}
