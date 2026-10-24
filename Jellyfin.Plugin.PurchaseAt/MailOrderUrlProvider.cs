using System.Collections.Generic;
using Jellyfin.Plugin.PurchaseAt.MusicBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.PurchaseAt;

/// <summary>
/// Adds the MusicBrainz "purchase for mail-order" links of an album to the links on its page.
/// </summary>
public class MailOrderUrlProvider : IExternalUrlProvider
{
    private readonly PurchaseLinkService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailOrderUrlProvider"/> class.
    /// </summary>
    /// <param name="service">Instance of the <see cref="PurchaseLinkService"/> class.</param>
    public MailOrderUrlProvider(PurchaseLinkService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public string Name => Plugin.Instance?.Configuration.LinkName ?? "Buy (mail-order)";

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is not MusicAlbum || AlbumIds.FromItem(item) is not { } ids)
        {
            return [];
        }

        return _service.GetUrls(ids);
    }
}
