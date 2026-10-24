using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PurchaseAt.MusicBrainz;

/// <summary>
/// Looks up the albums queued by <see cref="PurchaseLinkService"/> in the background.
/// </summary>
public sealed class PurchaseLinkWorker : BackgroundService
{
    private readonly PurchaseLinkService _service;
    private readonly ILogger<PurchaseLinkWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PurchaseLinkWorker"/> class.
    /// </summary>
    /// <param name="service">Instance of the <see cref="PurchaseLinkService"/> class.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PurchaseLinkWorker}"/> interface.</param>
    public PurchaseLinkWorker(PurchaseLinkService service, ILogger<PurchaseLinkWorker> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var ids in _service.Queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _service.RefreshAsync(ids, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                // Not cached, so the album is queued again the next time its page is opened.
                _logger.LogWarning(ex, "Could not look up mail-order links for {Album}", ids.Key);
            }
        }
    }
}
