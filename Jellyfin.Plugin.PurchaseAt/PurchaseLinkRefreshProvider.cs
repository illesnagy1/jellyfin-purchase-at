using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PurchaseAt.MusicBrainz;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.PurchaseAt;

/// <summary>
/// Hooks into album metadata refreshes: "Search for missing metadata" and "Replace all metadata"
/// look the album's purchase links up again, other refreshes (such as a library scan finding a new
/// album) look them up only if they are not cached.
/// </summary>
public class PurchaseLinkRefreshProvider : ICustomMetadataProvider<MusicAlbum>
{
    private readonly PurchaseLinkService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="PurchaseLinkRefreshProvider"/> class.
    /// </summary>
    /// <param name="service">Instance of the <see cref="PurchaseLinkService"/> class.</param>
    public PurchaseLinkRefreshProvider(PurchaseLinkService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public string Name => "Purchase At";

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(MusicAlbum item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Custom providers run after the remote ones, so the MusicBrainz ids are up to date here.
        if (item is not null && AlbumIds.FromItem(item) is { } ids)
        {
            if (options.MetadataRefreshMode == MetadataRefreshMode.FullRefresh)
            {
                _service.QueueRefresh(ids);
            }
            else
            {
                _service.GetUrls(ids);
            }
        }

        // The links are kept in the plugin's cache, not on the item.
        return Task.FromResult(ItemUpdateType.None);
    }
}
