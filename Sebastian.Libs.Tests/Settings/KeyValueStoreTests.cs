using System.Text;
using Sebastian.Libs.Settings;

namespace Sebastian.Libs.Tests.Settings;

public sealed class KeyValueStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sebastian-tests",
        Guid.NewGuid().ToString("N"));

    private readonly InMemorySettings _plainSettings = new();

    [Fact]
    public void SecretRoundTrips()
    {
        var store = CreateStore();

        store.Set("github:pat", "secret-token", isSecret: true);

        Assert.True(store.Has("github:pat"));
        Assert.Equal("secret-token", store["github:pat"]);
        Assert.False(_plainSettings.Has("github:pat"));
    }

    [Fact]
    public void PlainRoundTrips()
    {
        var store = CreateStore();

        store.Set("github:app-id", "12345", isSecret: false);

        Assert.True(store.Has("github:app-id"));
        Assert.Equal("12345", store["github:app-id"]);
    }

    [Fact]
    public void PlainWriteOnSecretThrows()
    {
        var store = CreateStore();
        store.Set("github:pat", "secret-token", isSecret: true);

        _ = Assert.Throws<InvalidOperationException>(
            () => store.Set("github:pat", "plain", isSecret: false));
    }

    [Fact]
    public void SecretWriteRemovesPlainCopy()
    {
        var store = CreateStore();
        store.Set("github:pat", "plain", isSecret: false);

        store.Set("github:pat", "secret-token", isSecret: true);

        Assert.False(_plainSettings.Has("github:pat"));
        Assert.Equal("secret-token", store["github:pat"]);
    }

    [Fact]
    public void CorruptSecretReadsAsMissing()
    {
        var store = CreateStore();
        store.Set("github:pat", "secret-token", isSecret: true);

        var file = Directory.GetFiles(_directory, "*.bin").Single();
        File.WriteAllBytes(file, [1, 2, 3, 4]);

        Assert.False(store.Has("github:pat"));
        Assert.Null(store["github:pat"]);
    }

    [Fact]
    public void SecretFileIsNotPlaintext()
    {
        var store = CreateStore();
        store.Set("github:pat", "secret-token", isSecret: true);

        var file = Directory.GetFiles(_directory, "*.bin").Single();
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));

        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveRemovesSecretAndPlain()
    {
        var store = CreateStore();
        store.Set("github:pat", "secret-token", isSecret: true);
        store.Set("server:url", "http://127.0.0.1:5099", isSecret: false);

        store.Remove("github:pat");
        store.Remove("server:url");

        Assert.False(store.Has("github:pat"));
        Assert.False(store.Has("server:url"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private KeyValueStore CreateStore()
    {
        return new(_plainSettings, _directory);
    }
}
