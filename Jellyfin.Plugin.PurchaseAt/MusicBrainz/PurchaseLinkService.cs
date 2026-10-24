using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.PurchaseAt.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PurchaseAt.MusicBrainz;

/// <summary>
/// Looks up and caches the "purchase for mail-order" links of albums on MusicBrainz.
/// </summary>
public sealed class PurchaseLinkService : IDisposable
{
    /// <summary>
    /// The id of the MusicBrainz "purchase for mail-order" release-url relationship type.
    /// </summary>
    public const string MailOrderRelationshipTypeId = "3ee51e05-a06a-415e-b40c-b3f740dedfd7";

    // MusicBrainz allows one request per second per client.
    private static readonly TimeSpan _requestInterval = TimeSpan.FromSeconds(1.1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PurchaseLinkService> _logger;
    private readonly string _cacheFile;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private readonly ConcurrentDictionary<string, byte> _queued = new();
    private readonly ConcurrentDictionary<string, byte> _forced = new();
    private readonly Channel<AlbumIds> _queue = Channel.CreateUnbounded<AlbumIds>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _lookupLock = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="PurchaseLinkService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PurchaseLinkService}"/> interface.</param>
    public PurchaseLinkService(IHttpClientFactory httpClientFactory, IApplicationPaths applicationPaths, ILogger<PurchaseLinkService> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);

        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheFile = Path.Combine(applicationPaths.CachePath, "purchase-at", "links.json");
        _cache = new ConcurrentDictionary<string, CacheEntry>(LoadCache(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the albums waiting to be looked up.
    /// </summary>
    public ChannelReader<AlbumIds> Queue => _queue.Reader;

    private static PluginConfiguration Configuration => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the cached purchase links of an album. Albums that are not cached, or whose cache entry
    /// has expired, are queued to be looked up in the background.
    /// </summary>
    /// <param name="ids">The album's MusicBrainz ids.</param>
    /// <returns>The cached purchase links, possibly stale.</returns>
    public IReadOnlyList<string> GetUrls(AlbumIds ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        _cache.TryGetValue(ids.Key, out var entry);
        if (!IsFresh(entry))
        {
            Enqueue(ids);
        }

        return entry?.Urls ?? [];
    }

    /// <summary>
    /// Queues an album to be looked up in the background, even if its cache entry has not expired.
    /// The cached links are kept until the lookup succeeds.
    /// </summary>
    /// <param name="ids">The album's MusicBrainz ids.</param>
    public void QueueRefresh(AlbumIds ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        _forced.TryAdd(ids.Key, 0);
        Enqueue(ids);
    }

    /// <summary>
    /// Gets a value indicating whether the album has an unexpired cache entry.
    /// </summary>
    /// <param name="ids">The album's MusicBrainz ids.</param>
    /// <returns>Whether the album has an unexpired cache entry.</returns>
    public bool IsCached(AlbumIds ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return _cache.TryGetValue(ids.Key, out var entry) && IsFresh(entry);
    }

    /// <summary>
    /// Looks up an album's purchase links on MusicBrainz and caches them, unless the album has an
    /// unexpired cache entry and was not queued with <see cref="QueueRefresh"/>.
    /// </summary>
    /// <param name="ids">The album's MusicBrainz ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the lookup.</returns>
    public async Task RefreshAsync(AlbumIds ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        await _lookupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another lookup may have finished while this one was waiting.
            if (!_forced.TryRemove(ids.Key, out _) && IsCached(ids))
            {
                return;
            }

            var urls = await LookupAsync(ids, cancellationToken).ConfigureAwait(false);
            _cache[ids.Key] = new CacheEntry(urls, DateTimeOffset.UtcNow);
            await SaveCacheAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Found {Count} mail-order links for {Album}", urls.Count, ids.Key);
        }
        finally
        {
            _queued.TryRemove(ids.Key, out _);
            _lookupLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lookupLock.Dispose();
    }

    private void Enqueue(AlbumIds ids)
    {
        if (_queued.TryAdd(ids.Key, 0))
        {
            _queue.Writer.TryWrite(ids);
        }
    }

    private static bool IsFresh(CacheEntry? entry)
    {
        return entry is not null && entry.Fetched.AddDays(Configuration.CacheDays) > DateTimeOffset.UtcNow;
    }

    private static IEnumerable<string> GetMailOrderUrls(JsonElement entity)
    {
        if (!entity.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var relation in relations.EnumerateArray())
        {
            if (relation.TryGetProperty("type-id", out var typeId)
                && typeId.ValueEquals(MailOrderRelationshipTypeId)
                && !(relation.TryGetProperty("ended", out var ended) && ended.ValueKind == JsonValueKind.True)
                && relation.TryGetProperty("url", out var url)
                && url.TryGetProperty("resource", out var resource)
                && Uri.TryCreate(resource.GetString(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                yield return uri.AbsoluteUri;
            }
        }
    }

    private async Task<IReadOnlyList<string>> LookupAsync(AlbumIds ids, CancellationToken cancellationToken)
    {
        var releaseGroupId = ids.ReleaseGroupId;
        if (ids.ReleaseId is not null)
        {
            using var release = await GetAsync($"release/{ids.ReleaseId}?inc=url-rels+release-groups", cancellationToken).ConfigureAwait(false);
            if (release is not null)
            {
                var urls = GetMailOrderUrls(release.RootElement).Distinct(StringComparer.Ordinal).ToList();
                if (urls.Count > 0 || !Configuration.IncludeOtherReleases)
                {
                    return urls;
                }

                if (release.RootElement.TryGetProperty("release-group", out var releaseGroup)
                    && releaseGroup.TryGetProperty("id", out var id))
                {
                    releaseGroupId = id.GetString();
                }
            }
        }

        if (releaseGroupId is null || (ids.ReleaseId is not null && !Configuration.IncludeOtherReleases))
        {
            return [];
        }

        using var releases = await GetAsync($"release?release-group={releaseGroupId}&inc=url-rels&limit=100", cancellationToken).ConfigureAwait(false);
        if (releases is null || !releases.RootElement.TryGetProperty("releases", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray().SelectMany(GetMailOrderUrls).Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task<JsonDocument?> GetAsync(string query, CancellationToken cancellationToken)
    {
        var wait = _lastRequest + _requestInterval - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        var server = Configuration.MusicBrainzServer.TrimEnd('/');
        var requestUri = new Uri($"{server}/ws/2/{query}&fmt=json");

        try
        {
            // The MusicBrainz client is configured by the server with a User-Agent identifying Jellyfin.
            var response = await _httpClientFactory.CreateClient(NamedClient.MusicBrainz)
                .GetAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _lastRequest = DateTime.UtcNow;
        }
    }

    private Dictionary<string, CacheEntry> LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFile))
            {
                using var stream = File.OpenRead(_cacheFile);
                return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(stream) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read purchase link cache {Path}", _cacheFile);
        }

        return [];
    }

    private async Task SaveCacheAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
        var stream = File.Create(_cacheFile);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, _cache, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
