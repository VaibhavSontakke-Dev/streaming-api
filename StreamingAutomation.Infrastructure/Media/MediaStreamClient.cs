using System.Buffers;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Polly.Timeout;
using StreamingAutomation.Application.Playback;

namespace StreamingAutomation.Infrastructure.Media;

/// <summary>
/// Typed HttpClient for the Test Media Server. Retries/timeouts are applied by the resilience
/// pipeline registered in DI; this class only maps final outcomes to <see cref="MediaStreamException"/>.
/// </summary>
public sealed class MediaStreamClient(HttpClient httpClient) : IMediaStreamClient
{
    public const string SessionIdHeader = "X-Session-Id";
    public const string SessionFailRateHeader = "X-Session-Fail-Rate";

    internal static readonly HttpRequestOptionsKey<RetryCounter> RetryCounterKey = new("retry-counter");

    public async Task<MediaManifest> GetManifestAsync(string mediaKey, StreamRequestContext context, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"media/{Uri.EscapeDataString(mediaKey)}/manifest", context);
        using var response = await SendAsync(request, cancellationToken);

        return await response.Content.ReadFromJsonAsync<MediaManifest>(cancellationToken)
            ?? throw new MediaStreamException("MEDIA_INVALID_MANIFEST", $"Empty manifest for '{mediaKey}'.");
    }

    public async Task<long> DownloadSegmentAsync(string mediaKey, int segmentIndex, StreamRequestContext context, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            $"media/{Uri.EscapeDataString(mediaKey)}/segments/{segmentIndex.ToString(CultureInfo.InvariantCulture)}", context);
        using var response = await SendAsync(request, cancellationToken);

        // Drain the body into a pooled buffer – we measure bytes, we don't keep media in memory.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static HttpRequestMessage CreateRequest(string relativeUrl, StreamRequestContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Add(SessionIdHeader, context.SessionId.ToString());
        if (context.SessionFailRate > 0)
        {
            request.Headers.Add(SessionFailRateHeader, context.SessionFailRate.ToString(CultureInfo.InvariantCulture));
        }

        request.Options.Set(RetryCounterKey, context.Retries);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new MediaStreamException("MEDIA_TIMEOUT", "Media server did not respond in time (after retries).", ex);
        }
        catch (HttpRequestException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MediaStreamException("MEDIA_NETWORK_ERROR", $"Network error contacting media server: {ex.Message}", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var code = await TryReadProblemTitleAsync(response, cancellationToken)
                ?? $"MEDIA_HTTP_{(int)response.StatusCode}";
            throw new MediaStreamException(code, $"Media server returned {(int)response.StatusCode} {response.ReasonPhrase} for {request.RequestUri}.");
        }
    }

    private static async Task<string?> TryReadProblemTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return doc.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t
                && t.All(c => char.IsUpper(c) || c == '_' || char.IsDigit(c))
                ? t
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
