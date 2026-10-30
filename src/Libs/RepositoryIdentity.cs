namespace Sebastian.Libs;

internal sealed record RepositoryIdentity(string Owner, string Name)
{
    public static RepositoryIdentity? Parse(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var value = remoteUrl.Trim().TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        string path;
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return null;
            }

            path = uri.AbsolutePath.Trim('/');
        }
        else
        {
            var separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                return null;
            }

            path = value[(separator + 1)..].Trim('/');
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 2
            ? null
            : new RepositoryIdentity(segments[^2], segments[^1]);
    }
}
