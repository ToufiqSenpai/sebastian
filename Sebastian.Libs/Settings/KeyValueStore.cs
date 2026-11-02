using System.Security.Cryptography;
using System.Text;

namespace Sebastian.Libs.Settings;

public sealed class KeyValueStore
{
    private static readonly string DefaultSecretDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sebastian",
        "secrets");

    private readonly ISettings _plainSettings;
    private readonly string _secretDirectory;

    public KeyValueStore(ISettings settings, string? secretDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = secretDirectory ?? DefaultSecretDirectory;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _plainSettings = settings;
        _secretDirectory = Path.GetFullPath(directory);
    }

    public bool Has(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return TryReadSecret(key) is not null || _plainSettings.Has(key);
    }

    public string? this[string key]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return TryReadSecret(key) ?? _plainSettings.Read(key);
        }
    }

    public void Set(string key, string value, bool isSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (isSecret)
        {
            WriteSecret(key, value);
            _plainSettings.Remove(key);
            return;
        }

        if (File.Exists(SecretPath(key)))
        {
            throw new InvalidOperationException($"'{key}' already exists as a secret.");
        }

        _plainSettings.Write(key, value);
    }

    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = SecretPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        _plainSettings.Remove(key);
    }

    private void WriteSecret(string key, string value)
    {
        _ = Directory.CreateDirectory(_secretDirectory);

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var path = SecretPath(key);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllBytes(tempPath, protectedBytes);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string? TryReadSecret(string key)
    {
        var path = SecretPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string SecretPath(string key)
    {
        return Path.Combine(
            _secretDirectory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".bin");
    }
}
