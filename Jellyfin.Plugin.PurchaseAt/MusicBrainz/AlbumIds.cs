using System;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PurchaseAt.MusicBrainz;

/// <summary>
/// The MusicBrainz ids of an album.
/// </summary>
/// <param name="ReleaseId">The MusicBrainz release id.</param>
/// <param name="ReleaseGroupId">The MusicBrainz release group id.</param>
public sealed record AlbumIds(string? ReleaseId, string? ReleaseGroupId)
{
    /// <summary>
    /// Gets the key the album's purchase links are cached under.
    /// </summary>
    public string Key => ReleaseId ?? "rg:" + ReleaseGroupId;

    /// <summary>
    /// Reads the MusicBrainz ids of an item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The ids, or <c>null</c> if the item has neither a release nor a release group id.</returns>
    public static AlbumIds? FromItem(BaseItem item)
    {
        var releaseId = Normalize(item.GetProviderId(MetadataProvider.MusicBrainzAlbum));
        var releaseGroupId = Normalize(item.GetProviderId(MetadataProvider.MusicBrainzReleaseGroup));
        if (releaseId is null && releaseGroupId is null)
        {
            return null;
        }

        return new AlbumIds(releaseId, releaseGroupId);
    }

    private static string? Normalize(string? id)
    {
        // The ids end up in request urls, so only accept well formed MBIDs.
        return Guid.TryParse(id, out var guid) ? guid.ToString("D", CultureInfo.InvariantCulture) : null;
    }
}
