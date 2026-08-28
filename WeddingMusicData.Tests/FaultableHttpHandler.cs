using System.Net;

namespace WeddingMusicData.Tests;

/// <summary>
/// Fake HTTP handler that streams bytes for a URI until a simulated network
/// dropout is triggered, after which every request throws like a real outage.
/// </summary>
public sealed class FaultableHttpHandler : HttpMessageHandler
{
    private readonly byte[] _payload;
    public volatile bool NetworkDown;
    public int RequestCount { get; private set; }

    public FaultableHttpHandler(byte[] payload) => _payload = payload;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (NetworkDown)
            throw new HttpRequestException("Simulated network dropout.");

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_payload)
        };
        return Task.FromResult(response);
    }
}
