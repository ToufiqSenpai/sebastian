using System.Net;
using System.Text;

namespace Sebastian.Libs.Tests;

internal static class TestHttp
{
    public static HttpResponseMessage Json(
        HttpStatusCode status,
        string body,
        Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        configure?.Invoke(response);
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body, string etag)
    {
        return Json(
            status,
            body,
            response => response.Headers.TryAddWithoutValidation("ETag", etag));
    }
}
