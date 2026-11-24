using Microsoft.Extensions.Options;
using StreamingAutomation.MediaServer.Options;

namespace StreamingAutomation.MediaServer.Services;

public sealed record MediaManifest(
    string Key,
    string Title,
    int DurationSeconds,
    int SegmentDurationSeconds,
    int SegmentCount,
    int SegmentSizeBytes,
    int BitrateKbps);

/// <summary>Synthetic media catalogue plus a shared, pre-generated payload buffer (no per-request allocation).</summary>
public sealed class MediaCatalogue
{
    private readonly IOptionsMonitor<MediaServerOptions> _options;
    private readonly byte[] _payload;

    public MediaCatalogue(IOptionsMonitor<MediaServerOptions> options)
    {
        _options = options;
        _payload = new byte[options.CurrentValue.SegmentSizeBytes];
        Random.Shared.NextBytes(_payload); // incompressible "media" bytes
    }

    public MediaManifest? Find(string key)
    {
        var o = _options.CurrentValue;
        var entry = o.Catalogue.Find(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var segmentCount = (int)Math.Ceiling(entry.DurationSeconds / (double)o.SegmentDurationSeconds);
        return new MediaManifest(entry.Key, entry.Title, entry.DurationSeconds, o.SegmentDurationSeconds, segmentCount, o.SegmentSizeBytes, o.BitrateKbps);
    }

    public IEnumerable<MediaManifest> All() =>
        _options.CurrentValue.Catalogue.Select(c => Find(c.Key)!);

    public ReadOnlyMemory<byte> Payload(int length) => _payload.AsMemory(0, Math.Min(length, _payload.Length));
}
