using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PurchaseAt.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PurchaseAt.ScheduledTasks;

/// <summary>
/// Looks up the purchase links of every album in the library, so they show up the first time an album page is opened.
/// </summary>
public class RefreshPurchaseLinksTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly PurchaseLinkService _service;
    private readonly ILogger<RefreshPurchaseLinksTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshPurchaseLinksTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="service">Instance of the <see cref="PurchaseLinkService"/> class.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{RefreshPurchaseLinksTask}"/> interface.</param>
    public RefreshPurchaseLinksTask(ILibraryManager libraryManager, PurchaseLinkService service, ILogger<RefreshPurchaseLinksTask> logger)
    {
        _libraryManager = libraryManager;
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Look up album purchase links";

    /// <inheritdoc />
    public string Key => "PurchaseAtRefreshLinks";

    /// <inheritdoc />
    public string Description => "Looks up MusicBrainz \"purchase for mail-order\" links for albums that have none cached.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var albums = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.MusicAlbum],
                Recursive = true
            })
            .Select(AlbumIds.FromItem)
            .OfType<AlbumIds>()
            .DistinctBy(ids => ids.Key)
            .Where(ids => !_service.IsCached(ids))
            .ToList();

        for (var i = 0; i < albums.Count; i++)
        {
            try
            {
                await _service.RefreshAsync(albums[i], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                _logger.LogWarning(ex, "Could not look up mail-order links for {Album}", albums[i].Key);
            }

            progress.Report(100.0 * (i + 1) / albums.Count);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromDays(7).Ticks
            }
        ];
    }
}
