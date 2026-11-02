namespace Sebastian.Libs.Tests;

internal sealed record CapturedRequest(
    string Uri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string? UserAgent,
    string? Accept,
    string? ApiVersion);

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}
